"""CropScan XR: the rules behind the Unity VR app's routes (app/routers/xr.py).

* Eligibility: a scan may open in VR only when it is a confident diagnosis whose exact disease_id is a row of the
  shared case map (data/xr_case_map.json, the same file Unity ships) and the server holds that case's recipe.
* Ownership: a browser session token is issued to the website; a VR player pairs to it with a one-time code and gets
  its own device token. Only that browser session and its devices may open, read or write its scans' XR sessions.
  Tokens are random, shown once, and stored only as SHA-256 hashes.
* Treatment results: written once per Idempotency-Key, against the session's pinned recipe and expected revision.
"""

from __future__ import annotations

import hashlib
import json
import secrets
import time
from collections import defaultdict, deque
from datetime import datetime, timedelta, timezone
from functools import lru_cache

from sqlalchemy import select
from sqlalchemy.dialects.postgresql import insert as postgres_insert
from sqlalchemy.dialects.sqlite import insert as sqlite_insert
from sqlalchemy.orm import Session

from app.config import get_settings
from app.content import info_for
from app.models import Scan, XrBrowserSession, XrDevice, XrLaunch, XrRecipe, XrSession

SESSION_HEADER = "X-CropScan-Session"

# Outcome keys Unity's TreatmentResultManager can produce, and what they mean for the specimen.
OUTCOMES = {
    "Managed": "improved", "PestSuppressed": "improved", "Contained": "improved",
    "PartlyManaged": "partial", "PartlySuppressed": "partial", "PoorlyContained": "partial",
    "NotContained": "none", "NotControlled": "none", "NotTreated": "none",
}

# Pairing codes avoid look-alike characters (0/O, 1/I/L) because they are read off a screen and typed.
_CODE_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"


def now() -> datetime:
    return datetime.now(timezone.utc)


def aware(value: datetime | None) -> datetime | None:
    """SQLite hands back naive datetimes; treat them as the UTC they were written as."""
    if value is None or value.tzinfo is not None:
        return value
    return value.replace(tzinfo=timezone.utc)


def iso(value: datetime | None) -> str | None:
    value = aware(value)
    return value.isoformat().replace("+00:00", "Z") if value else None


def sha256(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def new_token() -> str:
    return secrets.token_urlsafe(32)


def new_pairing_code() -> str:
    raw = "".join(secrets.choice(_CODE_ALPHABET) for _ in range(8))
    return f"{raw[:4]}-{raw[4:]}"


def normalise_code(code: str) -> str:
    return "".join(ch for ch in (code or "").upper() if ch.isalnum())


# --- case map and recipes ---------------------------------------------------------------------------------------

@lru_cache(maxsize=1)
def case_map() -> dict:
    """{backend disease_id: row}, from the shared, versioned map."""
    path = get_settings().data_dir / "xr_case_map.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    rows = {}
    for row in data.get("rows", []):
        for field in ("backendDiseaseId", "plantId", "diseaseId", "specimenId", "recipeId"):
            if not row.get(field):
                raise ValueError(f"{path}: a row has no {field}")
        if row["backendDiseaseId"] in rows:
            raise ValueError(f"{path}: {row['backendDiseaseId']} is listed twice")
        rows[row["backendDiseaseId"]] = row
    return rows


def case_map_version() -> int:
    path = get_settings().data_dir / "xr_case_map.json"
    return int(json.loads(path.read_text(encoding="utf-8")).get("mapVersion", 0))


def load_recipe_seed() -> list[dict]:
    path = get_settings().data_dir / "xr_recipes.json"
    if not path.exists():
        return []
    return json.loads(path.read_text(encoding="utf-8")).get("recipes", [])


def seed_recipes(db: Session) -> int:
    """Inserts recipe versions that are not stored yet. Idempotent; an existing version is never changed."""
    recipes = load_recipe_seed()
    if not recipes:
        return 0
    rows = [
        {"recipe_id": recipe["recipeId"], "version": int(recipe["recipeVersion"]),
         "plant_id": recipe["plantId"], "disease_id": recipe["diseaseId"], "body": recipe}
        for recipe in recipes
    ]
    table = XrRecipe.__table__
    insert = postgres_insert if db.get_bind().dialect.name == "postgresql" else sqlite_insert
    statement = insert(table).values(rows).on_conflict_do_nothing(index_elements=list(table.primary_key))
    added = db.execute(statement).rowcount
    db.commit()
    return added


def latest_recipe(db: Session, recipe_id: str) -> XrRecipe | None:
    return db.scalars(select(XrRecipe).where(XrRecipe.recipe_id == recipe_id)
                      .order_by(XrRecipe.version.desc()).limit(1)).first()


def eligibility(db: Session, scan: Scan) -> tuple[bool, str | None, dict | None]:
    """Whether a scan can open in the VR app, the learner-facing reason when not, and its map row."""
    looks_like = (scan.client_meta or {}).get("looks_like")
    if looks_like:
        return False, f"This photo looks like {looks_like}, not the crop that was picked. Scan again first.", None
    if scan.status != "ok" or not scan.disease_id:
        return False, "This scan was not confident enough to identify a disease, so there is nothing to treat in VR.", None
    if scan.disease_id.endswith("healthy"):
        return False, "This leaf looks healthy, so there is no treatment to practise in VR.", None
    row = case_map().get(scan.disease_id)
    if row is None:
        name = (info_for(scan.disease_id) or {}).get("name") or scan.disease_id
        return False, f"VR treatment is not available for {name} yet.", None
    if latest_recipe(db, row["recipeId"]) is None:
        return False, "The VR treatment lesson for this case is not installed on the server yet.", row
    return True, None, row


# --- identities ---------------------------------------------------------------------------------------------------

def browser_session_for(db: Session, token: str | None) -> XrBrowserSession | None:
    if not token:
        return None
    return db.scalars(select(XrBrowserSession).where(XrBrowserSession.token_sha256 == sha256(token))).first()


def device_for(db: Session, authorization: str | None) -> XrDevice | None:
    if not authorization or not authorization.lower().startswith("bearer "):
        return None
    token = authorization[7:].strip()
    device = db.scalars(select(XrDevice).where(XrDevice.token_sha256 == sha256(token))).first()
    return device if device is not None and device.revoked_at is None else None


class RedeemLimiter:
    """At most N redeem attempts per client per minute: an 8-character code cannot be guessed online."""

    def __init__(self) -> None:
        self.attempts: dict[str, deque] = defaultdict(deque)

    def allow(self, client: str) -> bool:
        limit = get_settings().xr_redeem_attempts_per_minute
        window = self.attempts[client]
        cutoff = time.monotonic() - 60
        while window and window[0] < cutoff:
            window.popleft()
        if len(window) >= limit:
            return False
        window.append(time.monotonic())
        return True


redeem_limiter = RedeemLimiter()


# --- shapes the Unity client reads (camelCase, see Docs/CropScan_XR_Unity_Contract.md) --------------------------

def effective_status(launch: XrLaunch) -> str:
    """A launch that never finished opening expires; a finished one keeps its result."""
    if launch.status in ("pending", "opening") and aware(launch.expires_at) < now():
        return "expired"
    return launch.status


def launch_out(launch: XrLaunch | None) -> dict | None:
    if launch is None:
        return None
    return {
        "launchId": launch.id, "xrSessionId": launch.xr_session_id, "deviceId": launch.device_id,
        "source": launch.source, "status": effective_status(launch), "detail": launch.detail,
        "xrReady": launch.xr_ready, "createdUtc": iso(launch.created_at), "updatedUtc": iso(launch.updated_at),
        "expiresUtc": iso(launch.expires_at),
    }


def latest_launch(db: Session, session_id: str) -> XrLaunch | None:
    return db.scalars(select(XrLaunch).where(XrLaunch.xr_session_id == session_id)
                      .order_by(XrLaunch.created_at.desc()).limit(1)).first()


def session_out(db: Session, session: XrSession) -> dict:
    return {
        "xrSessionId": session.id, "scanId": session.scan_id, "backendDiseaseId": session.backend_disease_id,
        "plantId": session.plant_id, "diseaseId": session.disease_id, "specimenId": session.specimen_id,
        "recipeId": session.recipe_id, "recipeVersion": session.recipe_version, "revision": session.revision,
        "treatmentResult": session.treatment_result, "latestLaunch": launch_out(latest_launch(db, session.id)),
        "updatedUtc": iso(session.updated_at),
    }


def device_out(device: XrDevice, connected: bool) -> dict:
    stale_after = timedelta(seconds=get_settings().xr_heartbeat_stale_seconds)
    seen = aware(device.last_seen_at)
    fresh = seen is not None and now() - seen <= stale_after
    return {
        "deviceId": device.id, "name": device.name, "platform": device.platform, "appVersion": device.app_version,
        "pairedUtc": iso(device.created_at), "lastSeenUtc": iso(seen), "socketConnected": connected and fresh,
        "xrReady": bool(device.xr_ready) and fresh, "display": device.display, "stale": not fresh,
    }


# --- treatment results --------------------------------------------------------------------------------------------

def check_submission(recipe: dict, body: dict) -> str | None:
    """Validates a treatment result against the session's pinned recipe. Returns the reason it is refused, or None."""
    outcome = body.get("outcome")
    if outcome not in OUTCOMES:
        return f"'{outcome}' is not a known outcome."
    for field in ("initialSeverity", "finalSeverity", "initialHealth", "finalHealth", "score"):
        value = body.get(field)
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not 0.0 <= float(value) <= 1.0:
            return f"{field} must be a number between 0 and 1."
    controls = {c["controlId"]: c for c in recipe.get("controls", [])}
    for item in body.get("appliedValues") or []:
        control = controls.get(item.get("controlId"))
        if control is None:
            return f"Control '{item.get('controlId')}' is not part of recipe {recipe.get('recipeId')}."
        value = item.get("value")
        if not isinstance(value, (int, float)) or not control["min"] - 1e-4 <= float(value) <= control["max"] + 1e-4:
            return f"Control '{control['controlId']}' was set outside its range {control['min']}-{control['max']}."
    if body.get("applyCount", 0) < 0 or body.get("healthChecks", 0) < 0 or body.get("elapsedSeconds", 0) < 0:
        return "Counts and times cannot be negative."
    return None


def result_from(body: dict, revision: int, recipe_version: int) -> dict:
    """The confirmed result the session stores. The improvement band is the server's, derived from the outcome."""
    return {
        "outcome": body["outcome"], "improvement": OUTCOMES[body["outcome"]],
        "initialSeverity": float(body["initialSeverity"]), "finalSeverity": float(body["finalSeverity"]),
        "initialHealth": float(body["initialHealth"]), "finalHealth": float(body["finalHealth"]),
        "score": float(body["score"]), "valid": bool(body.get("valid")), "recipeVersion": recipe_version,
        "revision": revision, "savedUtc": iso(now()),
    }
