"""Run the CropScan API locally without Docker, Postgres or MinIO.

    cd services/api
    py -3.12 -m venv .venv
    .venv\\Scripts\\python -m pip install -r requirements.txt
    .venv\\Scripts\\python dev_local.py            # http://127.0.0.1:8000  (docs at /docs)

Uses a SQLite file and a media folder under <repo>/.local/ (git-ignored), serves stored photos at /media, seeds the
product catalogue and the VR treatment recipes, then starts uvicorn. Any variable already set in the environment
(DATABASE_URL, LOCAL_MEDIA_DIR, PORT, ...) wins over services/api/.env. Development only: photos are served without a signature here.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
LOCAL = REPO / ".local"


def load_env_file(path: Path) -> None:
    """KEY=value lines from services/api/.env into the environment (comments and blank lines skipped). A variable
    already set in the shell wins, so one run can override a value without editing the file."""
    if not path.is_file():
        return
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = (part.strip() for part in line.split("=", 1))
        value = value.strip('"').strip("'")
        if key and value and key not in os.environ:
            os.environ[key] = value


def main() -> int:
    # Everything this PC needs lives in services/api/.env. The defaults below only fill in what it leaves out.
    load_env_file(HERE / ".env")
    host = os.environ.setdefault("HOST", "127.0.0.1")
    port = int(os.environ.setdefault("PORT", "8000"))
    os.environ.setdefault("DATABASE_URL", f"sqlite:///{(LOCAL / 'cropscan.db').as_posix()}")
    os.environ.setdefault("LOCAL_MEDIA_DIR", str(LOCAL / "media"))
    os.environ.setdefault("PUBLIC_BASE_URL", f"http://{host}:{port}")
    os.environ.setdefault("DATA_DIR", str(REPO / "data"))
    os.environ.setdefault("MODEL_DIR", str(HERE / "model"))
    os.environ.setdefault("XR_LOCAL_LAUNCH", "true")
    # Not in .env: find an installed player, and the Unity Editor this project asks for (ProjectVersion.txt).
    installed = Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "CropScanXR" / "PlantDiseaseVR.exe"
    if "XR_PLAYER_PATH" not in os.environ and installed.is_file():
        os.environ["XR_PLAYER_PATH"] = str(installed)
    unity_project = REPO.parent
    version_file = unity_project / "ProjectSettings" / "ProjectVersion.txt"
    if version_file.is_file():
        os.environ.setdefault("XR_UNITY_PROJECT_PATH", str(unity_project))
        version = next((line.split(":", 1)[1].strip() for line in version_file.read_text(encoding="utf-8").splitlines()
                        if line.startswith("m_EditorVersion:")), "")
        editor = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity.exe"
        if version and editor.is_file():
            os.environ.setdefault("XR_UNITY_EDITOR_PATH", str(editor))
    # The SQLite file's folder and the media folder must exist before the first write.
    database = os.environ["DATABASE_URL"]
    if database.startswith("sqlite:///"):
        Path(database[len("sqlite:///"):]).parent.mkdir(parents=True, exist_ok=True)
    Path(os.environ["LOCAL_MEDIA_DIR"]).mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(HERE))

    from app.config import get_settings
    from app.db import Base, get_engine
    from app import models  # noqa: F401  (registers the tables)

    settings = get_settings()
    if not settings.database_url.startswith("sqlite"):
        print("dev_local.py is for the SQLite setup; use alembic upgrade head for Postgres.", file=sys.stderr)
        return 2

    # SQLite cannot run migration 0001's Postgres grant block, so the local schema comes from the models.
    Base.metadata.create_all(get_engine())
    import seed

    seed.main()   # products, videos and the VR treatment recipes

    import uvicorn

    print(f"CropScan API: http://{host}:{port}   docs: http://{host}:{port}/docs   health: http://{host}:{port}/healthz")
    uvicorn.run("app.main:app", host=host, port=port, log_level="info")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
