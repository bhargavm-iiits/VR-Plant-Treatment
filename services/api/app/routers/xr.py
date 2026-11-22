"""CropScan XR routes for the Unity VR app and the result page.

Who may call what:
  browser  -- X-CropScan-Session: <token> from POST /api/xr/browser-sessions (the website keeps it)
  device   -- Authorization: Bearer <token> from POST /api/xr/pairings/redeem (the paired Unity player keeps it)
A device acts for the browser session it was paired to. Every lookup and write checks that ownership.

The same-laptop Enter the field button opens the installed app with cropscanxr://open-field?scan=<id>; the player
then calls POST /api/xr/sessions itself, so the backend decides here whether that device may open the scan.
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
from datetime import timedelta

from fastapi import APIRouter, Body, Depends, Header, HTTPException, Query, Request, WebSocket, WebSocketDisconnect
from fastapi.responses import JSONResponse, Response
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app import ledger, local_launch, window_focus, xr
from app.config import get_settings
from app.db import get_db, get_sessionmaker
from app.models import (
    Scan, XrBrowserSession, XrDevice, XrLaunch, XrPairing, XrRecipe, XrResultWrite, XrSession,
)

router = APIRouter(tags=["xr"])
log = logging.getLogger(__name__)

# Devices with an open socket, on this API instance. Several instances behind a load balancer would need shared
# delivery (for example Postgres LISTEN/NOTIFY); readiness itself is durable in xr_device.
_sockets: dict[str, WebSocket] = {}


# --- callers ------------------------------------------------------------------------------------------------------

class Caller:
    def __init__(self, browser_session_id: str, device: XrDevice | None):
        self.browser_session_id = browser_session_id
        self.device = device


def browser(db: Session = Depends(get_db),
            token: str | None = Header(None, alias=xr.SESSION_HEADER)) -> XrBrowserSession:
    session = xr.browser_session_for(db, token)
    if session is None:
        raise HTTPException(401, "This CropScan session has expired. Start a new session and try again.")
    return session


def device(db: Session = Depends(get_db), authorization: str | None = Header(None)) -> XrDevice:
    found = xr.device_for(db, authorization)
    if found is None:
        raise HTTPException(401, "This VR player is not paired. Press Pair VR on the CropScan website.")
    return found


def caller(db: Session = Depends(get_db),
           token: str | None = Header(None, alias=xr.SESSION_HEADER),
           authorization: str | None = Header(None)) -> Caller:
    found = xr.device_for(db, authorization)
    if found is not None:
        return Caller(found.browser_session_id, found)
    session = xr.browser_session_for(db, token)
    if session is not None:
        return Caller(session.id, None)
    raise HTTPException(401, "Pair this VR player, or open CropScan in the browser that made the scan.")


def owned_session(db: Session, session_id: str, who: Caller) -> XrSession:
    session = db.get(XrSession, session_id)
    # Not found and not yours look the same, so session IDs cannot be probed.
    if session is None or session.browser_session_id != who.browser_session_id:
        raise HTTPException(404, "No such XR session for this CropScan session.")
    return session


# --- browser session and pairing ----------------------------------------------------------------------------------

@router.post("/api/xr/browser-sessions", status_code=201)
def create_browser_session(db: Session = Depends(get_db)) -> dict:
    """A server-issued identity for this browser. The token is returned once; only its hash is stored."""
    token = xr.new_token()
    session = XrBrowserSession(token_sha256=xr.sha256(token))
    db.add(session)
    db.commit()
    return {"sessionToken": token, "header": xr.SESSION_HEADER}


@router.post("/api/xr/pairings", status_code=201)
def create_pairing(db: Session = Depends(get_db), who: XrBrowserSession = Depends(browser)) -> dict:
    """A short-lived, one-time code to type into the Windows player's pairing window."""
    code = xr.new_pairing_code()
    expires = xr.now() + timedelta(seconds=get_settings().xr_pairing_ttl_seconds)
    db.add(XrPairing(browser_session_id=who.id, code_sha256=xr.sha256(xr.normalise_code(code)), expires_at=expires))
    db.commit()
    return {"code": code, "expiresUtc": xr.iso(expires)}


@router.post("/api/xr/pairings/redeem")
def redeem_pairing(request: Request, body: dict = Body(...), db: Session = Depends(get_db)) -> dict:
    client = request.client.host if request.client else "unknown"
    if not xr.redeem_limiter.allow(client):
        raise HTTPException(429, "Too many pairing attempts. Wait a minute and use a new code.")
    code = xr.normalise_code(str(body.get("code", "")))
    pairing = db.scalars(select(XrPairing).where(XrPairing.code_sha256 == xr.sha256(code))).first() if code else None
    if pairing is None or pairing.redeemed_at is not None or xr.aware(pairing.expires_at) < xr.now():
        raise HTTPException(400, "This pairing code is wrong, already used or expired. Press Pair VR again for a new one.")

    token = xr.new_token()
    found = XrDevice(
        browser_session_id=pairing.browser_session_id, token_sha256=xr.sha256(token),
        name=str(body.get("deviceName") or "VR player")[:128], platform=str(body.get("platform") or "unknown")[:32],
        app_version=str(body.get("appVersion") or "")[:32] or None,
    )
    db.add(found)
    db.flush()
    pairing.redeemed_at = xr.now()
    pairing.device_id = found.id
    db.commit()
    settings = get_settings()
    return {"deviceId": found.id, "deviceToken": token, "apiBaseUrl": settings.public_base_url, "expiresUtc": None}


@router.get("/api/xr/devices")
def list_devices(db: Session = Depends(get_db), who: XrBrowserSession = Depends(browser)) -> list[dict]:
    rows = db.scalars(select(XrDevice).where(XrDevice.browser_session_id == who.id, XrDevice.revoked_at.is_(None))
                      .order_by(XrDevice.created_at.desc())).all()
    return [xr.device_out(d, d.id in _sockets) for d in rows]


@router.delete("/api/xr/devices/{device_id}", status_code=204, response_class=Response, response_model=None)
def revoke_device(device_id: str, db: Session = Depends(get_db), who: XrBrowserSession = Depends(browser)) -> Response:
    found = db.get(XrDevice, device_id)
    if found is None or found.browser_session_id != who.id:
        raise HTTPException(404, "No such paired device.")
    found.revoked_at = xr.now()
    db.commit()
    return Response(status_code=204)


# --- sessions -----------------------------------------------------------------------------------------------------

@router.post("/api/xr/sessions")
def open_session(body: dict = Body(...), db: Session = Depends(get_db), who: Caller = Depends(caller)) -> JSONResponse:
    """Creates or reopens this learner's run for a scan. The scan must belong to the caller's browser session."""
    scan_id = str(body.get("scanId") or "")
    scan = db.get(Scan, scan_id) if scan_id else None
    if scan is None:
        raise HTTPException(404, "CropScan has no scan with this ID.")
    if scan.owner_session_id is None:
        raise HTTPException(403, "This scan was made without a CropScan session, so it cannot open in VR. Scan the leaf again.")
    if scan.owner_session_id != who.browser_session_id:
        raise HTTPException(403, "This scan belongs to another CropScan session.")

    existing = db.scalars(select(XrSession).where(XrSession.browser_session_id == who.browser_session_id,
                                                  XrSession.scan_id == scan.id)).first()
    if existing is not None:
        return JSONResponse(xr.session_out(db, existing), status_code=200)

    ok, reason, row = xr.eligibility(db, scan)
    if not ok:
        raise HTTPException(422, reason)
    recipe = xr.latest_recipe(db, row["recipeId"])
    session = XrSession(
        browser_session_id=who.browser_session_id, scan_id=scan.id, backend_disease_id=scan.disease_id,
        plant_id=row["plantId"], disease_id=row["diseaseId"], specimen_id=row["specimenId"],
        recipe_id=recipe.recipe_id, recipe_version=recipe.version, revision=0,
    )
    db.add(session)
    try:
        db.commit()
    except IntegrityError:
        # Two clicks raced; the first created it.
        db.rollback()
        session = db.scalars(select(XrSession).where(XrSession.browser_session_id == who.browser_session_id,
                                                     XrSession.scan_id == scan.id)).one()
        return JSONResponse(xr.session_out(db, session), status_code=200)
    return JSONResponse(xr.session_out(db, session), status_code=201)


@router.get("/api/xr/sessions")
def session_for_scan(scan_id: str = Query(..., alias="scanId"), db: Session = Depends(get_db),
                     who: Caller = Depends(caller)) -> dict:
    """The result page's view of a scan's VR run (with its latest launch status)."""
    session = db.scalars(select(XrSession).where(XrSession.browser_session_id == who.browser_session_id,
                                                 XrSession.scan_id == scan_id)).first()
    if session is None:
        raise HTTPException(404, "This scan has not been opened in VR yet.")
    return xr.session_out(db, session)


@router.get("/api/xr/sessions/{session_id}")
def get_session(session_id: str, db: Session = Depends(get_db), who: Caller = Depends(caller)) -> dict:
    return xr.session_out(db, owned_session(db, session_id, who))


@router.get("/api/xr/sessions/{session_id}/recipe")
def get_recipe(session_id: str, db: Session = Depends(get_db), who: Caller = Depends(caller)) -> dict:
    session = owned_session(db, session_id, who)
    recipe = db.get(XrRecipe, (session.recipe_id, session.recipe_version))
    if recipe is None:
        raise HTTPException(404, f"Recipe {session.recipe_id} v{session.recipe_version} is missing on the server.")
    return recipe.body


@router.post("/api/xr/sessions/{session_id}/treatment-result")
def save_treatment_result(session_id: str, body: dict = Body(...),
                          idempotency_key: str | None = Header(None, alias="Idempotency-Key"),
                          db: Session = Depends(get_db), who: XrDevice = Depends(device)) -> JSONResponse:
    session = owned_session(db, session_id, Caller(who.browser_session_id, who))
    if not idempotency_key or len(idempotency_key) > 160:
        raise HTTPException(400, "An Idempotency-Key header (up to 160 characters) is required.")

    # A retried save returns what the first one wrote; it never writes again.
    previous = db.scalars(select(XrResultWrite).where(XrResultWrite.xr_session_id == session.id,
                                                      XrResultWrite.idempotency_key == idempotency_key)).first()
    if previous is not None:
        return JSONResponse(xr.session_out(db, session), status_code=200)

    if body.get("recipeId") != session.recipe_id or body.get("recipeVersion") != session.recipe_version:
        raise HTTPException(422, f"The assessment used recipe {body.get('recipeId')} v{body.get('recipeVersion')}; "
                                 f"this session pins {session.recipe_id} v{session.recipe_version}.")
    if body.get("expectedRevision") != session.revision:
        # The client reloads this canonical session before allowing another save.
        return JSONResponse(xr.session_out(db, session), status_code=409)
    recipe = db.get(XrRecipe, (session.recipe_id, session.recipe_version))
    problem = xr.check_submission(recipe.body if recipe else {}, body)
    if problem:
        raise HTTPException(422, problem)

    session.revision += 1
    session.treatment_result = xr.result_from(body, session.revision, session.recipe_version)
    session.updated_at = xr.now()
    db.add(XrResultWrite(xr_session_id=session.id, idempotency_key=idempotency_key, revision=session.revision, body=body))
    ledger.append(db, event_type="xr.treatment_result", subject_id=session.id, payload={
        "xr_session_id": session.id, "scan_id": session.scan_id, "recipe_id": session.recipe_id,
        "recipe_version": session.recipe_version, "revision": session.revision,
        "outcome": session.treatment_result["outcome"], "improvement": session.treatment_result["improvement"],
    })
    try:
        db.commit()
    except IntegrityError:
        # The same key raced in from a retry: the other request wrote it.
        db.rollback()
        db.refresh(session)
        return JSONResponse(xr.session_out(db, session), status_code=200)
    return JSONResponse(xr.session_out(db, session), status_code=201)


# --- launches -----------------------------------------------------------------------------------------------------

@router.post("/api/xr/sessions/{session_id}/launches", status_code=201)
def create_launch(session_id: str, body: dict = Body(default={}),
                  idempotency_key: str | None = Header(None, alias="Idempotency-Key"),
                  db: Session = Depends(get_db), who: XrDevice = Depends(device)) -> dict:
    """Recorded by the player after it accepts an app link, so the result page can show live status."""
    session = owned_session(db, session_id, Caller(who.browser_session_id, who))
    if not idempotency_key or len(idempotency_key) > 160:
        raise HTTPException(400, "An Idempotency-Key header (up to 160 characters) is required.")
    existing = db.scalars(select(XrLaunch).where(XrLaunch.device_id == who.id,
                                                 XrLaunch.idempotency_key == idempotency_key)).first()
    if existing is not None:
        return xr.launch_out(existing)
    source = body.get("source") if body.get("source") in ("app-link", "remote") else "app-link"
    launch = XrLaunch(xr_session_id=session.id, device_id=who.id, source=source,
                      status="opening" if source == "app-link" else "pending", idempotency_key=idempotency_key,
                      xr_ready=bool(who.xr_ready),
                      expires_at=xr.now() + timedelta(seconds=get_settings().xr_launch_ttl_seconds))
    db.add(launch)
    db.commit()
    return xr.launch_out(launch)


@router.get("/api/xr/launches/pending")
def pending_launches(db: Session = Depends(get_db), who: XrDevice = Depends(device)) -> dict:
    """Remote launches still waiting for this device (the optional socket flow); app-link launches never wait here."""
    rows = db.scalars(select(XrLaunch).where(XrLaunch.device_id == who.id, XrLaunch.status == "pending")).all()
    return {"items": [xr.launch_out(r) for r in rows if xr.effective_status(r) == "pending"]}


@router.get("/api/xr/launches/{launch_id}")
def get_launch(launch_id: str, db: Session = Depends(get_db), who: Caller = Depends(caller)) -> dict:
    launch = db.get(XrLaunch, launch_id)
    if launch is None:
        raise HTTPException(404, "No such launch.")
    owned_session(db, launch.xr_session_id, who)
    return xr.launch_out(launch)


@router.post("/api/xr/launches/{launch_id}/status")
def report_launch(launch_id: str, body: dict = Body(...), db: Session = Depends(get_db),
                  who: XrDevice = Depends(device)) -> dict:
    launch = db.get(XrLaunch, launch_id)
    if launch is None or launch.device_id != who.id:
        raise HTTPException(404, "No such launch for this device.")
    status = body.get("status")
    if status not in ("opening", "opened", "failed"):
        raise HTTPException(422, "status must be opening, opened or failed.")
    current = xr.effective_status(launch)
    if current in ("opened", "failed", "expired") and status == "opening":
        return xr.launch_out(launch)   # a late "opening" never undoes a finished launch
    launch.status = status
    launch.detail = (str(body.get("detail"))[:512] if body.get("detail") else None)
    launch.xr_ready = bool(body.get("xrReady"))
    launch.updated_at = xr.now()
    who.xr_ready = bool(body.get("xrReady"))
    who.last_seen_at = xr.now()
    db.commit()
    return xr.launch_out(launch)


# --- local launch (laptop demo) ---------------------------------------------------------------------------------

_LOOPBACK = {"127.0.0.1", "::1", "localhost"}


def _local_launch_allowed(request: Request) -> bool:
    """Only when the API runs on the learner's own laptop (XR_LOCAL_LAUNCH) and the caller is that laptop."""
    return get_settings().xr_local_launch and request.client is not None and request.client.host in _LOOPBACK


@router.get("/api/xr/local-launch")
def local_launch_info(request: Request) -> dict:
    """Whether Enter the field can ask this API to open CropScan XR, and whether the app is running now."""
    if not _local_launch_allowed(request):
        return {"available": False}
    settings = get_settings()
    player, editor = settings.xr_player_path, settings.xr_unity_editor_path
    return {"available": True, "appRunning": local_launch.receiver_running(),
            "playerInstalled": bool(player and player.is_file()),
            "unityEditor": bool(editor and editor.is_file() and settings.xr_unity_project_path)}


@router.post("/api/xr/local-launch")
def local_launch_scan(request: Request, body: dict = Body(...), db: Session = Depends(get_db),
                      who: XrBrowserSession = Depends(browser)) -> dict:
    """Enter the field on the laptop: hand this browser's scan to CropScan XR running here, or start it."""
    if not _local_launch_allowed(request):
        raise HTTPException(404, "Opening CropScan XR from the website is only available when the API runs on this computer.")
    scan = db.get(Scan, str(body.get("scanId") or ""))
    if scan is None:
        raise HTTPException(404, "CropScan has no scan with this ID.")
    if scan.owner_session_id is None:
        raise HTTPException(403, "This scan was saved without a browser session. Upload the leaf again to open it in VR.")
    if scan.owner_session_id != who.id:
        raise HTTPException(403, "This scan belongs to another CropScan session. Open it in its original browser, or upload the leaf again.")
    ok, reason, _ = xr.eligibility(db, scan)
    if not ok:
        raise HTTPException(422, reason)
    try:
        settings = get_settings()
        project = settings.xr_unity_project_path
        # Bring Unity (or the player) in front of the browser once it is on its way; a starting one is focused as
        # soon as its window appears.
        focus = lambda seconds: window_focus.focus_app(project.name if project else None, wait_seconds=seconds)  # noqa: E731
        how = local_launch.open_scan(scan.id, settings.xr_player_path, settings.xr_unity_editor_path, project, focus)
    except local_launch.LaunchError as exc:
        raise HTTPException(409, str(exc)) from exc
    log.info("scan %s handed to CropScan XR (%s)", scan.id, how)
    return {"status": how, "scanId": scan.id}


@router.post("/api/xr/local-demo")
def local_demo(request: Request) -> dict:
    """See VR Demo on the laptop: open CropScan XR with no scan, starting Unity (or the installed player) when it is
    closed. Loopback callers with XR_LOCAL_LAUNCH only, the same gate as Enter the field. No scan is involved, so
    there is nothing to own and no session to check; the request body is ignored."""
    if not _local_launch_allowed(request):
        raise HTTPException(404, "Opening CropScan XR from the website is only available when the API runs on this computer.")
    try:
        settings = get_settings()
        project = settings.xr_unity_project_path
        focus = lambda seconds: window_focus.focus_app(project.name if project else None, wait_seconds=seconds)  # noqa: E731
        how = local_launch.open_demo(settings.xr_player_path, settings.xr_unity_editor_path, project, focus)
    except local_launch.LaunchError as exc:
        raise HTTPException(409, str(exc)) from exc
    log.info("CropScan XR demo opened (%s)", how)
    return {"status": how}


@router.post("/api/xr/local-pair")
def local_pair(request: Request, body: dict = Body(...), db: Session = Depends(get_db)) -> dict:
    """Laptop demo: the CropScan XR app on this computer pairs itself for the browser session that owns a scan it was
    just handed (Enter the field), so no pairing code has to be typed. Loopback callers with XR_LOCAL_LAUNCH only; a
    remote headset still pairs with the one-time code."""
    if not _local_launch_allowed(request):
        raise HTTPException(404, "Automatic pairing is only available when the API runs on this computer.")
    scan = db.get(Scan, str(body.get("scanId") or ""))
    if scan is None:
        raise HTTPException(404, "CropScan has no scan with this ID.")
    if scan.owner_session_id is None:
        raise HTTPException(403, "This scan was made without a CropScan session, so it cannot open in VR. Scan the leaf again.")
    # One local pairing per browser session: an earlier one is retired, so the device list stays short.
    for old in db.scalars(select(XrDevice).where(XrDevice.browser_session_id == scan.owner_session_id,
                                                 XrDevice.platform == "windows-local", XrDevice.revoked_at.is_(None))):
        old.revoked_at = xr.now()
    token = xr.new_token()
    found = XrDevice(browser_session_id=scan.owner_session_id, token_sha256=xr.sha256(token),
                     name=str(body.get("deviceName") or "This PC")[:128], platform="windows-local",
                     app_version=str(body.get("appVersion") or "")[:32] or None)
    db.add(found)
    db.commit()
    return {"deviceId": found.id, "deviceToken": token, "apiBaseUrl": get_settings().public_base_url, "expiresUtc": None}


# --- device socket ------------------------------------------------------------------------------------------------

@router.websocket("/ws/xr")
async def device_socket(socket: WebSocket) -> None:
    """Unity connects out to the API: hello and heartbeats carry headset readiness; the server pings to keep the
    connection alive. Session and recipe data never travel here."""
    with get_sessionmaker()() as db:
        found = xr.device_for(db, socket.headers.get("authorization"))
        device_id = found.id if found else None
    if device_id is None:
        await socket.close(code=4401)
        return
    await socket.accept()
    previous = _sockets.get(device_id)
    _sockets[device_id] = socket
    if previous is not None:
        with contextlib.suppress(Exception):
            await previous.close(code=4409)

    async def ping() -> None:
        while True:
            await asyncio.sleep(20)
            await socket.send_json({"type": "ping"})

    pinger = asyncio.create_task(ping())
    try:
        while True:
            message = await socket.receive_json()
            if message.get("type") in ("device.hello", "device.heartbeat", "pong"):
                with get_sessionmaker()() as db:
                    row = db.get(XrDevice, device_id)
                    if row is None or row.revoked_at is not None:
                        await socket.close(code=4401)
                        return
                    row.last_seen_at = xr.now()
                    if message.get("type") != "pong":
                        row.xr_ready = bool(message.get("xrReady"))
                        row.display = str(message.get("display") or "")[:128] or None
                        row.app_version = str(message.get("appVersion") or row.app_version or "")[:32] or None
                    db.commit()
    except (WebSocketDisconnect, RuntimeError, ValueError):
        pass
    finally:
        pinger.cancel()
        if _sockets.get(device_id) is socket:
            _sockets.pop(device_id, None)
