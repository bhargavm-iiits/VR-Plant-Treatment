"""CropScan XR: browser sessions, paired VR players, XR sessions, launches, recipes and result writes.

Revision ID: 0005
Revises: 0004

The website gets a server-issued browser session; a Unity player pairs to it with a one-time code. A scan records
the browser session that uploaded it, and only that session and its paired players may open the scan in VR.
Treatment results are written once per Idempotency-Key (the unique key on xr_result_write), against a recipe version
pinned on the session.
"""
from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects.postgresql import JSONB

revision = "0005"
down_revision = "0004"
branch_labels = None
depends_on = None

Json = sa.JSON().with_variant(JSONB(), "postgresql")


def ts():
    return sa.DateTime(timezone=True)


def upgrade() -> None:
    op.add_column("scan", sa.Column("owner_session_id", sa.String(36), nullable=True))
    op.create_index("ix_scan_owner_session_id", "scan", ["owner_session_id"])

    op.create_table(
        "xr_browser_session",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("token_sha256", sa.String(64), nullable=False, unique=True),
        sa.Column("created_at", ts(), nullable=False),
    )
    op.create_index("ix_xr_browser_session_token_sha256", "xr_browser_session", ["token_sha256"])

    op.create_table(
        "xr_pairing",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("browser_session_id", sa.String(36), sa.ForeignKey("xr_browser_session.id"), nullable=False),
        sa.Column("code_sha256", sa.String(64), nullable=False, unique=True),
        sa.Column("created_at", ts(), nullable=False),
        sa.Column("expires_at", ts(), nullable=False),
        sa.Column("redeemed_at", ts()),
        sa.Column("device_id", sa.String(36)),
    )
    op.create_index("ix_xr_pairing_browser_session_id", "xr_pairing", ["browser_session_id"])
    op.create_index("ix_xr_pairing_code_sha256", "xr_pairing", ["code_sha256"])

    op.create_table(
        "xr_device",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("browser_session_id", sa.String(36), sa.ForeignKey("xr_browser_session.id"), nullable=False),
        sa.Column("token_sha256", sa.String(64), nullable=False, unique=True),
        sa.Column("name", sa.String(128), nullable=False),
        sa.Column("platform", sa.String(32), nullable=False),
        sa.Column("app_version", sa.String(32)),
        sa.Column("created_at", ts(), nullable=False),
        sa.Column("last_seen_at", ts()),
        sa.Column("xr_ready", sa.Boolean(), nullable=False, server_default=sa.false()),
        sa.Column("display", sa.String(128)),
        sa.Column("revoked_at", ts()),
    )
    op.create_index("ix_xr_device_browser_session_id", "xr_device", ["browser_session_id"])
    op.create_index("ix_xr_device_token_sha256", "xr_device", ["token_sha256"])

    op.create_table(
        "xr_recipe",
        sa.Column("recipe_id", sa.String(64), primary_key=True),
        sa.Column("version", sa.Integer(), primary_key=True),
        sa.Column("plant_id", sa.String(64), nullable=False),
        sa.Column("disease_id", sa.String(64), nullable=False),
        sa.Column("body", Json, nullable=False),
        sa.Column("created_at", ts(), nullable=False),
    )

    op.create_table(
        "xr_session",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("browser_session_id", sa.String(36), sa.ForeignKey("xr_browser_session.id"), nullable=False),
        sa.Column("scan_id", sa.String(36), sa.ForeignKey("scan.id"), nullable=False),
        sa.Column("backend_disease_id", sa.String(128), nullable=False),
        sa.Column("plant_id", sa.String(64), nullable=False),
        sa.Column("disease_id", sa.String(64), nullable=False),
        sa.Column("specimen_id", sa.String(128), nullable=False),
        sa.Column("recipe_id", sa.String(64), nullable=False),
        sa.Column("recipe_version", sa.Integer(), nullable=False),
        sa.Column("revision", sa.Integer(), nullable=False),
        sa.Column("treatment_result", Json),
        sa.Column("created_at", ts(), nullable=False),
        sa.Column("updated_at", ts(), nullable=False),
        sa.UniqueConstraint("browser_session_id", "scan_id", name="xr_session_one_per_scan"),
        sa.CheckConstraint("revision >= 0", name="xr_session_revision_valid"),
    )
    op.create_index("ix_xr_session_browser_session_id", "xr_session", ["browser_session_id"])
    op.create_index("ix_xr_session_scan_id", "xr_session", ["scan_id"])

    op.create_table(
        "xr_launch",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("xr_session_id", sa.String(36), sa.ForeignKey("xr_session.id"), nullable=False),
        sa.Column("device_id", sa.String(36), sa.ForeignKey("xr_device.id"), nullable=False),
        sa.Column("source", sa.String(16), nullable=False),
        sa.Column("status", sa.String(16), nullable=False),
        sa.Column("detail", sa.String(512)),
        sa.Column("xr_ready", sa.Boolean(), nullable=False, server_default=sa.false()),
        sa.Column("idempotency_key", sa.String(160), nullable=False),
        sa.Column("created_at", ts(), nullable=False),
        sa.Column("updated_at", ts(), nullable=False),
        sa.Column("expires_at", ts(), nullable=False),
        sa.UniqueConstraint("device_id", "idempotency_key", name="xr_launch_idempotent"),
        sa.CheckConstraint("source in ('app-link','remote')", name="xr_launch_source_valid"),
        sa.CheckConstraint("status in ('pending','opening','opened','failed','expired')", name="xr_launch_status_valid"),
    )
    op.create_index("ix_xr_launch_xr_session_id", "xr_launch", ["xr_session_id"])
    op.create_index("ix_xr_launch_device_id", "xr_launch", ["device_id"])
    op.create_index("ix_xr_launch_status", "xr_launch", ["status"])
    op.create_index("ix_xr_launch_created_at", "xr_launch", ["created_at"])

    op.create_table(
        "xr_result_write",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("xr_session_id", sa.String(36), sa.ForeignKey("xr_session.id"), nullable=False),
        sa.Column("idempotency_key", sa.String(160), nullable=False),
        sa.Column("revision", sa.Integer(), nullable=False),
        sa.Column("body", Json, nullable=False),
        sa.Column("created_at", ts(), nullable=False),
        sa.UniqueConstraint("xr_session_id", "idempotency_key", name="xr_result_write_idempotent"),
    )
    op.create_index("ix_xr_result_write_xr_session_id", "xr_result_write", ["xr_session_id"])


def downgrade() -> None:
    for table in ("xr_result_write", "xr_launch", "xr_session", "xr_recipe", "xr_device", "xr_pairing",
                  "xr_browser_session"):
        op.drop_table(table)
    op.drop_index("ix_scan_owner_session_id", table_name="scan")
    op.drop_column("scan", "owner_session_id")
