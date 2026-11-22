"""Allow scan images and heatmaps to be stored with their scan records.

Revision ID: 0007
Revises: 0006
"""
from alembic import op
import sqlalchemy as sa

revision = "0007"
down_revision = "0006"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column("scan", sa.Column("image_data", sa.LargeBinary(), nullable=True))
    op.add_column("scan", sa.Column("gradcam_data", sa.LargeBinary(), nullable=True))


def downgrade() -> None:
    op.drop_column("scan", "gradcam_data")
    op.drop_column("scan", "image_data")
