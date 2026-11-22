"""The study's activity log: what a participant did on the website and in the Unity app.

    POST /api/activity          a batch of events from one visit (a page load, or one run of the app)
    GET  /api/activity          read them back, newest first     -- this computer only
    GET  /api/activity.csv      the same as a spreadsheet        -- this computer only

Writing is open to any client, like a scan upload, and is bounded instead: a batch is capped, every string is
clipped, and `data` must be small JSON. Reading is not open: the log describes people, so it is served only to a
caller on the machine the API runs on (the researcher's laptop), never over the network.

A batch can be retried safely. (visit_id, seq) is unique, so events that already arrived are skipped, not doubled.
"""

from __future__ import annotations

import csv
import io
import json
import logging
import uuid
from datetime import datetime, timezone

from fastapi import APIRouter, Body, Depends, Header, HTTPException, Query, Request
from fastapi.responses import Response
from sqlalchemy import select
from sqlalchemy.orm import Session

from app import xr
from app.db import get_db
from app.models import ActivityEvent

router = APIRouter(tags=["activity"])
log = logging.getLogger(__name__)

MAX_BATCH = 200
MAX_DATA_BYTES = 2048
SOURCES = {"web", "unity"}
_LOOPBACK = {"127.0.0.1", "::1", "localhost"}


def _clip(value, length: int) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text[:length] or None


def _visit(value) -> str:
    try:
        return str(uuid.UUID(str(value)))
    except (ValueError, AttributeError, TypeError) as exc:
        raise HTTPException(422, "visitId must be a UUID.") from exc


def _when(value) -> datetime | None:
    """The device's clock for this event: epoch milliseconds, or an ISO string. Unreadable values are dropped, not
    guessed at; created_at (the server's clock) is always there."""
    try:
        if isinstance(value, (int, float)) and not isinstance(value, bool):
            return datetime.fromtimestamp(value / 1000, tz=timezone.utc)
        if isinstance(value, str) and value:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
            return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)
    except (ValueError, OverflowError, OSError):
        pass
    return None


def _data(value) -> dict | None:
    """Small JSON only. Anything else, or anything large, is dropped: the log must not become a place to put files."""
    if not isinstance(value, dict) or not value:
        return None
    try:
        encoded = json.dumps(value, ensure_ascii=False, allow_nan=False)
    except (TypeError, ValueError):
        return None
    return value if len(encoded.encode("utf-8")) <= MAX_DATA_BYTES else {"dropped": "data too large"}


def _local_only(request: Request) -> None:
    if request.client is None or request.client.host not in _LOOPBACK:
        # Not found, not forbidden: the log's existence is not advertised to the network.
        raise HTTPException(404, "Not found.")


@router.post("/api/activity", status_code=202)
def record(body: dict = Body(...), db: Session = Depends(get_db),
           token: str | None = Header(None, alias=xr.SESSION_HEADER),
           user_agent: str | None = Header(None)) -> dict:
    source = str(body.get("source") or "web")
    if source not in SOURCES:
        raise HTTPException(422, "source must be 'web' or 'unity'.")
    visit_id = _visit(body.get("visitId"))
    events = body.get("events")
    if not isinstance(events, list):
        raise HTTPException(422, "events must be a list.")
    if len(events) > MAX_BATCH:
        raise HTTPException(413, f"At most {MAX_BATCH} events in one batch.")

    session = xr.browser_session_for(db, token)
    known = set(db.scalars(select(ActivityEvent.seq).where(ActivityEvent.visit_id == visit_id)))
    accepted = 0
    for event in events:
        if not isinstance(event, dict):
            continue
        seq, kind = event.get("seq"), _clip(event.get("kind"), 48)
        if not isinstance(seq, int) or isinstance(seq, bool) or seq < 0 or seq in known or not kind:
            continue
        known.add(seq)
        db.add(ActivityEvent(
            source=source, visit_id=visit_id, seq=seq, kind=kind,
            browser_session_id=session.id if session else None,
            client_ts=_when(event.get("ts")),
            path=_clip(event.get("path"), 256), target=_clip(event.get("target"), 160),
            data=_data(event.get("data")),
            lang=_clip(body.get("lang"), 8), viewport=_clip(body.get("viewport"), 16),
            user_agent=_clip(user_agent, 256),
        ))
        accepted += 1
    db.commit()
    return {"accepted": accepted, "skipped": len(events) - accepted}


def _rows(db: Session, visit_id: str | None, source: str | None, kind: str | None, limit: int) -> list[ActivityEvent]:
    query = select(ActivityEvent).order_by(ActivityEvent.created_at.desc(), ActivityEvent.seq.desc()).limit(limit)
    if visit_id:
        query = query.where(ActivityEvent.visit_id == _visit(visit_id))
    if source:
        query = query.where(ActivityEvent.source == source)
    if kind:
        query = query.where(ActivityEvent.kind == kind)
    return list(db.scalars(query))


def _public(e: ActivityEvent) -> dict:
    return {
        "id": e.id, "source": e.source, "visitId": e.visit_id, "seq": e.seq, "sessionId": e.browser_session_id,
        "kind": e.kind, "path": e.path, "target": e.target, "data": e.data, "lang": e.lang, "viewport": e.viewport,
        "clientTs": e.client_ts.isoformat() if e.client_ts else None, "createdAt": e.created_at.isoformat(),
        "userAgent": e.user_agent,
    }


@router.get("/api/activity")
def read(request: Request, db: Session = Depends(get_db), visit_id: str | None = Query(None, alias="visitId"),
         source: str | None = None, kind: str | None = None, limit: int = Query(500, ge=1, le=5000)) -> dict:
    _local_only(request)
    rows = _rows(db, visit_id, source, kind, limit)
    return {"count": len(rows), "events": [_public(e) for e in rows]}


@router.get("/api/activity.csv")
def read_csv(request: Request, db: Session = Depends(get_db), visit_id: str | None = Query(None, alias="visitId"),
             source: str | None = None, kind: str | None = None, limit: int = Query(5000, ge=1, le=50000)) -> Response:
    _local_only(request)
    columns = ["createdAt", "clientTs", "source", "visitId", "seq", "sessionId", "kind", "path", "target", "data",
               "lang", "viewport", "userAgent"]
    out = io.StringIO()
    writer = csv.writer(out)
    writer.writerow(columns)
    for e in reversed(_rows(db, visit_id, source, kind, limit)):   # oldest first reads as a timeline
        row = _public(e)
        row["data"] = json.dumps(row["data"], ensure_ascii=False) if row["data"] else ""
        # A leading = + - or @ is a formula to a spreadsheet; the apostrophe keeps a logged value a value.
        cells = [("" if row[c] is None else str(row[c])) for c in columns]
        writer.writerow(["'" + c if c[:1] in ("=", "+", "-", "@") else c for c in cells])
    return Response(out.getvalue(), media_type="text/csv; charset=utf-8",
                    headers={"Content-Disposition": 'attachment; filename="cropscan-activity.csv"'})
