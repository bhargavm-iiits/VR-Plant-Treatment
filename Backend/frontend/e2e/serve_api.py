"""Run the real API with isolated SQLite and media for the browser integration test."""

import os
from pathlib import Path
import sys
from tempfile import TemporaryDirectory

SERVICE = Path(__file__).resolve().parents[2] / "services" / "api"
HOST, PORT = "127.0.0.1", 8299


def main():
    sys.path.insert(0, str(SERVICE))
    with TemporaryDirectory(prefix="cropscan-playwright-") as directory:
        root = Path(directory)
        os.environ.update({
            "DATABASE_URL": f"sqlite:///{(root / 'e2e.db').as_posix()}",
            "LOCAL_MEDIA_DIR": str(root / "media"),
            "PUBLIC_BASE_URL": f"http://{HOST}:{PORT}",
            "DATA_DIR": str(SERVICE.parents[1] / "data"),
            "MODEL_DIR": str(SERVICE / "model"),
            "XR_LOCAL_LAUNCH": "false",
            "RAZORPAY_KEY_ID": "",
            "RAZORPAY_KEY_SECRET": "",
            "LEDGER_ANCHOR_ENABLED": "false",
        })
        from app.config import Settings

        Settings.model_config["env_file"] = None

        from app import models  # noqa: F401
        from app.db import Base, get_engine
        from app.main import app
        import seed
        import uvicorn

        Base.metadata.create_all(get_engine())
        seed.main()
        uvicorn.run(app, host=HOST, port=PORT)


if __name__ == "__main__":
    main()
