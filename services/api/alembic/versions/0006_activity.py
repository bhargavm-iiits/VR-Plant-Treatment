"""The study's activity log: one row per thing a participant did on the website or in the Unity app.

Revision ID: 0006
Revises: 0005

(visit_id, seq) is unique so a retried batch is skipped instead of counted twice. See app/routers/activity.py.
"""
from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects.postgresql import JSONB

revision = "0006"
down_revision = "0005"
branch_labels = None
depends_on = None

Json = sa.JSON().with_variant(JSONB(), "postgresql")


def upgrade() -> None:
    op.create_table(
        "activity_event",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("client_ts", sa.DateTime(timezone=True)),
        sa.Column("source", sa.String(16), nullable=False),
        sa.Column("visit_id", sa.String(36), nullable=False),
        sa.Column("seq", sa.Integer, nullable=False),
        sa.Column("browser_session_id", sa.String(36), sa.ForeignKey("xr_browser_session.id")),
        sa.Column("kind", sa.String(48), nullable=False),
        sa.Column("path", sa.String(256)),
        sa.Column("target", sa.String(160)),
        sa.Column("data", Json),
        sa.Column("lang", sa.String(8)),
        sa.Column("viewport", sa.String(16)),
        sa.Column("user_agent", sa.String(256)),
        sa.UniqueConstraint("visit_id", "seq", name="activity_event_once"),
        sa.CheckConstraint("source in ('web','unity')", name="activity_event_source_valid"),
        sa.CheckConstraint("seq >= 0", name="activity_event_seq_valid"),
    )
    for column in ("created_at", "source", "visit_id", "browser_session_id", "kind"):
        op.create_index(f"ix_activity_event_{column}", "activity_event", [column])


def downgrade() -> None:
    op.drop_table("activity_event")
