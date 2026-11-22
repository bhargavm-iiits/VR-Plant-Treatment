from __future__ import annotations

from functools import lru_cache
from pathlib import Path
from typing import Literal

from pydantic import Field, SecretStr, field_validator, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

# Paths are derived from where this file actually sits, not from an assumed
# checkout depth. In the container the app is copied to /app/app, so
# parents[3] does not exist and raised IndexError at import -- which every
# entrypoint hits, so neither uvicorn nor `alembic upgrade head` could start.
_APP_DIR = Path(__file__).resolve().parent          # .../app
SERVICE_DIR = _APP_DIR.parent                       # .../services/api, or /app
_REPO_ROOT = (
    SERVICE_DIR.parents[1] if len(SERVICE_DIR.parents) >= 2 else SERVICE_DIR
)


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    database_url: str = "postgresql+psycopg://cropscan:cropscan@localhost:5432/cropscan"

    model_dir: Path = SERVICE_DIR / "model"
    data_dir: Path = _REPO_ROOT / "data"
    inference_threads: int = Field(default=0, ge=0)
    scan_concurrency: int = Field(default=2, ge=1)

    s3_endpoint_url: str = "http://localhost:9000"
    s3_access_key_id: str = ""
    s3_secret_access_key: str = ""
    s3_bucket: str = "cropscan-media"
    s3_region: str = "us-east-1"
    signed_url_ttl_seconds: int = 3600
    scan_media_backend: Literal["s3", "database"] = "s3"
    scan_media_signing_key: SecretStr = SecretStr("")
    render_external_url: str = ""

    razorpay_key_id: str = ""
    razorpay_key_secret: str = ""
    razorpay_webhook_secret: str = ""

    max_upload_bytes: int = 10 * 1024 * 1024   # 10 MB, section 9.3
    ledger_anchor_enabled: bool = False        # Phase 10 stretch, default off

    cors_origins: str = "*"

    # Local development without MinIO: media goes to this folder and the API serves it at /media. Never set in a
    # deployment -- scan photos would then be served without a signature.
    local_media_dir: Path | None = None
    # How the API addresses itself in local media URLs.
    public_base_url: str = "http://127.0.0.1:8000"

    # CropScan XR (the Unity VR app). Short-lived pairing codes, launch jobs that expire, and a heartbeat window after
    # which a headset's readiness is reported as stale.
    xr_pairing_ttl_seconds: int = 600
    xr_launch_ttl_seconds: int = 900
    xr_heartbeat_stale_seconds: int = 45
    xr_redeem_attempts_per_minute: int = 10
    # Laptop demo only (dev_local.py turns it on): Enter the field asks this API to open the scan in CropScan XR on
    # the same computer -- through the running app's pipe, or by starting XR_PLAYER_PATH. Loopback callers only.
    xr_local_launch: bool = False
    xr_player_path: Path | None = None
    # ...or the Unity Editor on the project, when no player is installed (it opens XRBootstrap and presses Play).
    xr_unity_editor_path: Path | None = None
    xr_unity_project_path: Path | None = None

    @field_validator("database_url")
    @classmethod
    def _psycopg_driver(cls, v: str) -> str:
        """Hosts like Render hand out postgres:// or postgresql:// URLs, which make
        SQLAlchemy reach for psycopg2. Only psycopg 3 is installed."""
        for prefix in ("postgres://", "postgresql://"):
            if v.startswith(prefix):
                return "postgresql+psycopg://" + v[len(prefix):]
        return v

    @model_validator(mode="after")
    def _require_scan_media_key(self) -> "Settings":
        if self.scan_media_backend == "database" and len(self.scan_media_signing_key.get_secret_value()) < 32:
            raise ValueError("Database scan media requires a signing key of at least 32 characters.")
        return self

    @property
    def is_payment_configured(self) -> bool:
        return bool(self.razorpay_key_id and self.razorpay_key_secret)


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings()
