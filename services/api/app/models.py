"""Tables per PRD 7. Seven of them, one Postgres, no extensions.

ledger_entry is append-only. There is no update or delete path anywhere in this
codebase, and the migration revokes UPDATE and DELETE on it from the application
role. Both halves matter: the code discipline is what a reviewer checks, the
grant is what actually holds when the code is wrong.
"""

from __future__ import annotations

import uuid
from datetime import datetime, timezone

from sqlalchemy import (
    JSON, BigInteger, Boolean, CheckConstraint, DateTime, ForeignKey, Index,
    Integer, LargeBinary, String, UniqueConstraint,
)
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.db import Base

# Postgres in deployment, portable JSON everywhere else, so the payment and
# ledger tests run on SQLite in-memory instead of needing a live database.
Json = JSON().with_variant(JSONB, "postgresql")


def _uuid() -> str:
    return str(uuid.uuid4())


def _now() -> datetime:
    return datetime.now(timezone.utc)


class Scan(Base):
    __tablename__ = "scan"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now, index=True)
    image_key: Mapped[str] = mapped_column(String(512))
    image_sha256: Mapped[str] = mapped_column(String(64), index=True)
    model_version: Mapped[str] = mapped_column(String(128))
    disease_id: Mapped[str | None] = mapped_column(String(128), index=True)
    confidence: Mapped[float | None] = mapped_column()
    top3: Mapped[list] = mapped_column(Json, default=list)
    status: Mapped[str] = mapped_column(String(16))
    gradcam_key: Mapped[str | None] = mapped_column(String(512))
    image_data: Mapped[bytes | None] = mapped_column(LargeBinary, deferred=True)
    gradcam_data: Mapped[bytes | None] = mapped_column(LargeBinary, deferred=True)
    client_meta: Mapped[dict] = mapped_column(Json, default=dict)
    # The browser session (CropScan XR) that uploaded the scan. Only that session, and the VR devices paired to it,
    # may open the scan in VR. Null for scans uploaded without one.
    owner_session_id: Mapped[str | None] = mapped_column(String(36), index=True)

    __table_args__ = (
        CheckConstraint("status in ('ok','uncertain')", name="scan_status_valid"),
        # An uncertain scan must not carry a disease id. The XR module keys off
        # this, and a stray id would let it launch on a guess.
        CheckConstraint(
            "(status = 'ok' and disease_id is not null) or "
            "(status = 'uncertain' and disease_id is null)",
            name="scan_uncertain_has_no_disease",
        ),
    )


class LedgerEntry(Base):
    __tablename__ = "ledger_entry"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    seq: Mapped[int] = mapped_column(BigInteger, unique=True, index=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now, index=True)
    event_type: Mapped[str] = mapped_column(String(64), index=True)
    subject_id: Mapped[str] = mapped_column(String(64), index=True)
    payload: Mapped[dict] = mapped_column(Json)
    payload_sha256: Mapped[str] = mapped_column(String(64))
    prev_hash: Mapped[str] = mapped_column(String(64))
    entry_hash: Mapped[str] = mapped_column(String(64), unique=True)

    __table_args__ = (Index("ix_ledger_seq_desc", seq.desc()),)


class MerkleRoot(Base):
    __tablename__ = "merkle_root"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    day: Mapped[str] = mapped_column(String(10), unique=True, index=True)   # YYYY-MM-DD
    root_hash: Mapped[str] = mapped_column(String(64))
    entry_count: Mapped[int] = mapped_column(Integer)
    anchored_tx: Mapped[str | None] = mapped_column(String(128))


class Product(Base):
    __tablename__ = "product"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    sku: Mapped[str] = mapped_column(String(64), unique=True)
    kind: Mapped[str] = mapped_column(String(16))
    title: Mapped[str] = mapped_column(String(256))
    # Integer paise, server-side. The client sends a product_id, never a price.
    price_paise: Mapped[int] = mapped_column(Integer)
    disease_id: Mapped[str | None] = mapped_column(String(128), index=True)
    active: Mapped[bool] = mapped_column(Boolean, default=True)

    __table_args__ = (
        CheckConstraint("kind in ('kit','consult','module')", name="product_kind_valid"),
        CheckConstraint("price_paise > 0", name="product_price_positive"),
    )


class Order(Base):
    __tablename__ = "order"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now, index=True)
    product_id: Mapped[str] = mapped_column(ForeignKey("product.id"))
    scan_id: Mapped[str | None] = mapped_column(ForeignKey("scan.id"))
    amount_paise: Mapped[int] = mapped_column(Integer)
    status: Mapped[str] = mapped_column(String(16), default="created", index=True)
    rzp_order_id: Mapped[str | None] = mapped_column(String(64), unique=True, index=True)
    rzp_payment_id: Mapped[str | None] = mapped_column(String(64), index=True)
    paid_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))

    product: Mapped[Product] = relationship()

    __table_args__ = (
        CheckConstraint("status in ('created','paid','failed','refunded')",
                        name="order_status_valid"),
    )


class WebhookEvent(Base):
    __tablename__ = "webhook_event"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    # THIS unique constraint is the idempotency mechanism. Razorpay retries, and
    # a replayed event must collide here rather than being handled twice.
    rzp_event_id: Mapped[str] = mapped_column(String(128), unique=True, index=True)
    event_type: Mapped[str] = mapped_column(String(64))
    received_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    processed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    raw: Mapped[dict] = mapped_column(Json)


class Video(Base):
    __tablename__ = "video"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    disease_id: Mapped[str] = mapped_column(String(128), index=True)
    kind: Mapped[str] = mapped_column(String(32))
    title: Mapped[str] = mapped_column(String(256))
    hls_key: Mapped[str] = mapped_column(String(512))
    poster_key: Mapped[str | None] = mapped_column(String(512))
    duration_s: Mapped[int | None] = mapped_column(Integer)
    # Source pixels. The player sizes its panel from these before the video
    # has loaded, so the panel does not jump when metadata arrives.
    width: Mapped[int | None] = mapped_column(Integer)
    height: Mapped[int | None] = mapped_column(Integer)
    projection: Mapped[str] = mapped_column(String(16), default="flat")
    stereo: Mapped[str] = mapped_column(String(16), default="none")
    language: Mapped[str] = mapped_column(String(8), default="en")

    # Licence obligations travel with the media. Most of this footage is CC BY
    # or CC BY-SA, which REQUIRE attribution -- so it is a column the UI reads,
    # not a note in a README that nobody renders.
    caption: Mapped[str | None] = mapped_column(String(2048))
    license: Mapped[str | None] = mapped_column(String(64))
    attribution: Mapped[str | None] = mapped_column(String(512))
    source_url: Mapped[str | None] = mapped_column(String(1024))
    # Which cycle stage this clip illustrates, so the player can follow the
    # timeline instead of being a separate gallery.
    stage_id: Mapped[str | None] = mapped_column(String(32), index=True)

    __table_args__ = (
        # 'field' is flat field/orchard footage; 'field360' is specifically
        # equirectangular. Folding them together would make the UI label lie
        # about what the viewer is getting.
        CheckConstraint("kind in ('treatment','field','field360','symptom_closeup')",
                        name="video_kind_valid"),
        CheckConstraint("projection in ('flat','equirect','equirect180')",
                        name="video_projection_valid"),
        CheckConstraint("stereo in ('none','top_bottom','left_right')",
                        name="video_stereo_valid"),
    )


# --- CropScan XR (the Unity VR app) ---------------------------------------------------------------------------
#
# Identities stay separate: a scan is the backend diagnosis; an XR session is one learner's run for one scan; the
# specimen and (plant, disease) inside it point at Unity content. Ownership is a server-issued browser session:
# the website gets one, a VR player pairs to it, and only those two may open the browser session's scans.


class XrBrowserSession(Base):
    __tablename__ = "xr_browser_session"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    # Only the hash is stored; the token itself lives in the browser.
    token_sha256: Mapped[str] = mapped_column(String(64), unique=True, index=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)


class XrPairing(Base):
    __tablename__ = "xr_pairing"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    browser_session_id: Mapped[str] = mapped_column(ForeignKey("xr_browser_session.id"), index=True)
    code_sha256: Mapped[str] = mapped_column(String(64), unique=True, index=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    # One-time: set when a player redeems the code.
    redeemed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    device_id: Mapped[str | None] = mapped_column(String(36))


class XrDevice(Base):
    __tablename__ = "xr_device"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    browser_session_id: Mapped[str] = mapped_column(ForeignKey("xr_browser_session.id"), index=True)
    token_sha256: Mapped[str] = mapped_column(String(64), unique=True, index=True)
    name: Mapped[str] = mapped_column(String(128))
    platform: Mapped[str] = mapped_column(String(32))
    app_version: Mapped[str | None] = mapped_column(String(32))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    last_seen_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    xr_ready: Mapped[bool] = mapped_column(Boolean, default=False)
    display: Mapped[str | None] = mapped_column(String(128))
    revoked_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))


class XrRecipe(Base):
    """Versioned treatment recipes (seeded from the Unity treatment content). A session pins one version, so editing
    the catalogue never changes a lesson in progress."""

    __tablename__ = "xr_recipe"

    recipe_id: Mapped[str] = mapped_column(String(64), primary_key=True)
    version: Mapped[int] = mapped_column(Integer, primary_key=True)
    plant_id: Mapped[str] = mapped_column(String(64))
    disease_id: Mapped[str] = mapped_column(String(64))
    body: Mapped[dict] = mapped_column(Json)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)


class XrSession(Base):
    __tablename__ = "xr_session"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    browser_session_id: Mapped[str] = mapped_column(ForeignKey("xr_browser_session.id"), index=True)
    scan_id: Mapped[str] = mapped_column(ForeignKey("scan.id"), index=True)
    backend_disease_id: Mapped[str] = mapped_column(String(128))
    plant_id: Mapped[str] = mapped_column(String(64))
    disease_id: Mapped[str] = mapped_column(String(64))
    specimen_id: Mapped[str] = mapped_column(String(128))
    recipe_id: Mapped[str] = mapped_column(String(64))
    recipe_version: Mapped[int] = mapped_column(Integer)
    revision: Mapped[int] = mapped_column(Integer, default=0)
    treatment_result: Mapped[dict | None] = mapped_column(Json)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)

    __table_args__ = (
        # Reopening the same scan from the same browser session returns the same run and progress.
        UniqueConstraint("browser_session_id", "scan_id", name="xr_session_one_per_scan"),
        CheckConstraint("revision >= 0", name="xr_session_revision_valid"),
    )


class XrLaunch(Base):
    __tablename__ = "xr_launch"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    xr_session_id: Mapped[str] = mapped_column(ForeignKey("xr_session.id"), index=True)
    device_id: Mapped[str] = mapped_column(ForeignKey("xr_device.id"), index=True)
    source: Mapped[str] = mapped_column(String(16))
    status: Mapped[str] = mapped_column(String(16), index=True)
    detail: Mapped[str | None] = mapped_column(String(512))
    xr_ready: Mapped[bool] = mapped_column(Boolean, default=False)
    idempotency_key: Mapped[str] = mapped_column(String(160))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now, index=True)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))

    __table_args__ = (
        # A retried create (same device, same key) returns the first launch instead of a second one.
        UniqueConstraint("device_id", "idempotency_key", name="xr_launch_idempotent"),
        CheckConstraint("source in ('app-link','remote')", name="xr_launch_source_valid"),
        CheckConstraint("status in ('pending','opening','opened','failed','expired')", name="xr_launch_status_valid"),
    )


class XrResultWrite(Base):
    """One row per accepted treatment-result write. The unique key is the idempotency mechanism: a retried save with
    the same Idempotency-Key collides here instead of writing a second outcome."""

    __tablename__ = "xr_result_write"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    xr_session_id: Mapped[str] = mapped_column(ForeignKey("xr_session.id"), index=True)
    idempotency_key: Mapped[str] = mapped_column(String(160))
    revision: Mapped[int] = mapped_column(Integer)
    body: Mapped[dict] = mapped_column(Json)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)

    __table_args__ = (UniqueConstraint("xr_session_id", "idempotency_key", name="xr_result_write_idempotent"),)


class ActivityEvent(Base):
    """One thing a participant did, on the website or in the Unity app, for the study log.

    Append-only in practice: the API only ever inserts. A `visit_id` is one page load or one app run, and `seq` orders
    the events inside it, so a retried batch collides on the unique key instead of being counted twice. `created_at`
    is the server's clock and `client_ts` the device's, because the two disagree and analysis needs to know which is
    which. `data` holds only what the client chose to describe the action; no photo, file name or free text is logged.
    """

    __tablename__ = "activity_event"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_uuid)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now, index=True)
    client_ts: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    source: Mapped[str] = mapped_column(String(16), index=True)
    visit_id: Mapped[str] = mapped_column(String(36), index=True)
    seq: Mapped[int] = mapped_column(Integer)
    browser_session_id: Mapped[str | None] = mapped_column(ForeignKey("xr_browser_session.id"), index=True)
    kind: Mapped[str] = mapped_column(String(48), index=True)
    path: Mapped[str | None] = mapped_column(String(256))
    target: Mapped[str | None] = mapped_column(String(160))
    data: Mapped[dict | None] = mapped_column(Json)
    lang: Mapped[str | None] = mapped_column(String(8))
    viewport: Mapped[str | None] = mapped_column(String(16))
    user_agent: Mapped[str | None] = mapped_column(String(256))

    __table_args__ = (
        UniqueConstraint("visit_id", "seq", name="activity_event_once"),
        CheckConstraint("source in ('web','unity')", name="activity_event_source_valid"),
        CheckConstraint("seq >= 0", name="activity_event_seq_valid"),
    )
