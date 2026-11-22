from __future__ import annotations

import io
import logging
import uuid
from typing import Literal

from fastapi import APIRouter, Depends, File, Header, HTTPException, Query, Request, Response, UploadFile
from fastapi.concurrency import run_in_threadpool
from sqlalchemy import select
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session

from app import inference, ledger, storage, xr
from app.content import LANG_PATTERN, disease_info, info_for
from app.cycle import get_cycle
from app.config import get_settings
from app.db import get_db
from app.models import Scan
from app.schemas import ScanOut

router = APIRouter(prefix="/api/scans", tags=["scans"])
log = logging.getLogger(__name__)


def _to_out(scan: Scan, lang: str, db: Session) -> ScanOut:
    cycle = get_cycle(scan.disease_id) if scan.disease_id else None
    looks_like = (scan.client_meta or {}).get("looks_like")

    if looks_like:
        blocked = (f"This photo looks like {looks_like}, not the crop that was picked. "
                   f"Pick {looks_like}, or no crop, and scan again.")
    elif scan.status == "uncertain":
        blocked = ("This scan was not confident enough to identify a disease. "
                   "The field module would tell a story about the wrong pathogen.")
    elif cycle is None:
        blocked = "There is no disease cycle written up for this diagnosis yet."
    else:
        blocked = None

    # The installed VR app is gated separately from the browser Field viewer: an exact case-map row and a recipe.
    can_enter_vr, vr_blocked, _ = xr.eligibility(db, scan)

    return ScanOut(
        id=scan.id,
        created_at=scan.created_at,
        status=scan.status,
        disease_id=scan.disease_id,
        confidence=scan.confidence,
        top3=[{**t, "name": (info_for(t["disease_id"], lang) or {}).get("name")}
              for t in scan.top3],
        model_version=scan.model_version,
        image_url=storage.signed_url(scan.image_key),
        gradcam_url=storage.signed_url(scan.gradcam_key),
        info=info_for(scan.disease_id, lang) if scan.disease_id else None,
        has_cycle=cycle is not None,
        can_enter_field=blocked is None,
        field_blocked_reason=blocked,
        can_enter_vr=can_enter_vr,
        vr_blocked_reason=vr_blocked,
        looks_like_crop=looks_like,
    )


@router.post("", response_model=ScanOut, status_code=201)
async def create_scan(
    request: Request,
    file: UploadFile = File(...),
    lang: str = Query("en", pattern=LANG_PATTERN),
    crop: str | None = Query(None, max_length=40, description="the plant, as in disease info, e.g. 'Tomato'"),
    session_token: str | None = Header(None, alias=xr.SESSION_HEADER,
                                       description="CropScan browser session; only its owner may open the scan in VR"),
    db: Session = Depends(get_db),
) -> ScanOut:
    async with request.app.state.scan_slots:
        return await run_in_threadpool(_create_scan, file, lang, crop, session_token, db)


def _create_scan(file: UploadFile, lang: str, crop: str | None, session_token: str | None, db: Session) -> ScanOut:
    # Decode, infer, encode and persist in the worker. Requests waiting for a
    # scan slot leave the other API routes and database connections available.
    settings = get_settings()
    owner = xr.browser_session_for(db, session_token)
    if session_token is not None and owner is None:
        raise HTTPException(401, "This CropScan session has expired. Start a new session and try again.")
    owner_id = owner.id if owner else None
    if owner is not None:
        db.rollback()  # Release the read-only session lookup before inference.
    # A farmer knows the crop. Naming it stops look-alike crops competing
    # (corn vs rice/wheat/sugarcane, pepper vs chilli): field accuracy rises
    # from about 59% to 81% on PlantDoc test photos.
    allowed = None
    if crop:
        allowed = {cid for cid, info in disease_info().items() if info.get("plant") == crop}
        if not allowed:
            raise HTTPException(422, f"Unknown crop: {crop}")

    # Read one byte past the limit, not the whole body. Reading everything and
    # then measuring it means the limit protects nothing -- a 2 GB upload is
    # already resident by the time it is rejected.
    limit = settings.max_upload_bytes
    raw = file.file.read(limit + 1)
    if not raw:
        raise HTTPException(400, "Empty upload.")
    if len(raw) > limit:
        raise HTTPException(413, f"Image is larger than {limit // (1024 * 1024)} MB.")

    try:
        image = inference.decode_image(raw)  # Validate the bytes, not Content-Type.
        prediction = inference.predict(image, allowed)
    except ValueError as exc:
        raise HTTPException(400, str(exc)) from exc
    except inference.ModelUnavailable as exc:
        raise HTTPException(503, f"Inference is unavailable: {exc}") from exc

    image_sha = inference.sha256_bytes(raw)
    scan_id = str(uuid.uuid4())
    database_media = settings.scan_media_backend == "database"

    # Store the EXIF-stripped image, never the original bytes. The original may
    # carry GPS coordinates of someone's farm.
    def prepare_media():
        buf = io.BytesIO()
        image.save(buf, format="JPEG", quality=90)
        photo = buf.getvalue()
        overlay = None
        if prediction.cam is not None:
            overlay = inference.cam_overlay(image, prediction.cam)

        if database_media:
            image_key = storage.database_scan_key(scan_id, "image.jpg")
            gradcam_key = storage.database_scan_key(scan_id, "gradcam.png") if overlay else None
            return image_key, gradcam_key, photo, overlay

        key = storage.put(f"scans/{scan_id}/image.jpg", photo, "image/jpeg")
        cam_key = None
        if overlay:
            cam_key = storage.put(f"scans/{scan_id}/gradcam.png", overlay, "image/png")
        return key, cam_key, None, None

    # A storage failure must come back as an HTTP error. Left unhandled it is a
    # bare 500 without CORS headers, which the browser reports as "could not
    # reach the server" -- pointing the user at their connection, not at the
    # misconfigured bucket. Nothing is written to the database before this.
    try:
        image_key, gradcam_key, image_data, gradcam_data = prepare_media()
    except Exception as exc:
        log.exception("storing scan %s failed", scan_id)
        # The S3 error code (AccessDenied, NoSuchBucket, ...) names the fix;
        # it carries no secret, unlike the full message.
        code = getattr(exc, "response", {}).get("Error", {}).get("Code") or type(exc).__name__
        raise HTTPException(
            502, f"Image storage is unavailable ({code}). Check the S3_* settings."
        ) from exc

    scan = Scan(
        id=scan_id,
        owner_session_id=owner_id,
        image_key=image_key,
        image_sha256=image_sha,
        model_version=prediction.model_version,
        disease_id=prediction.disease_id,
        confidence=prediction.confidence,
        top3=[t for t in prediction.top3],
        status=prediction.status,
        gradcam_key=gradcam_key,
        image_data=image_data,
        gradcam_data=gradcam_data,
        client_meta={
            "filename": file.filename, "content_type": file.content_type, "crop": crop,
            "looks_like": (info_for(prediction.looks_like) or {}).get("plant") if prediction.looks_like else None,
        },
    )
    db.add(scan)

    # The scan row and its ledger entry commit together. A diagnosis that exists
    # without a ledger record would break the one property this system sells.
    try:
        ledger.append(
            db,
            event_type="scan.created" if prediction.status == "ok" else "scan.uncertain",
            subject_id=scan_id,
            payload={
                "scan_id": scan_id,
                "image_sha256": image_sha,
                "model_version": prediction.model_version,
                "disease_id": prediction.disease_id,
                "confidence": round(prediction.confidence, 6),
                "status": prediction.status,
            },
        )
        db.commit()
    except SQLAlchemyError as exc:
        db.rollback()
        log.error("Could not commit scan %s (%s)", scan_id, type(exc).__name__)
        raise HTTPException(503, "The scan could not be saved. Please try again.") from exc
    return _to_out(scan, lang, db)


@router.get("/media/{scan_id}/{filename}", include_in_schema=False)
def get_scan_media(
    scan_id: uuid.UUID,
    filename: Literal["image.jpg", "gradcam.png"],
    expires: int = Query(0),
    sig: str | None = Query(None, pattern=r"^[0-9a-f]{64}$"),
    db: Session = Depends(get_db),
) -> Response:
    scan_id_str = str(scan_id)
    key = storage.database_scan_key(scan_id_str, filename)
    if sig is None or not storage.valid_scan_signature(key, expires, sig):
        raise HTTPException(403, "This scan image link is invalid or has expired.")

    if filename == "image.jpg":
        column, key_column, media_type = Scan.image_data, Scan.image_key, "image/jpeg"
    else:
        column, key_column, media_type = Scan.gradcam_data, Scan.gradcam_key, "image/png"

    data = db.scalar(select(column).where(Scan.id == scan_id_str, key_column == key))
    if data is None:
        raise HTTPException(404, "No such scan image.")
    return Response(
        data,
        media_type=media_type,
        headers={"Cache-Control": "private, no-store", "X-Content-Type-Options": "nosniff"},
    )


@router.get("/{scan_id}", response_model=ScanOut)
def get_scan(
    scan_id: str,
    lang: str = Query("en", pattern=LANG_PATTERN),
    db: Session = Depends(get_db),
) -> ScanOut:
    scan = db.get(Scan, scan_id)
    if scan is None:
        raise HTTPException(404, "No such scan.")
    return _to_out(scan, lang, db)
