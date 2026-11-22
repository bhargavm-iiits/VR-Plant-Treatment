"""Opening a scan in CropScan XR on the computer the API runs on (the laptop demo, services/api/dev_local.py).

When the website and the API run on the same Windows laptop as the VR app, Enter the field asks the API to open the
scan, and the API does what CropScanLauncher.exe does for a cropscanxr: link:
  1. CropScan XR already running (the built player, or the Unity Editor in Play mode): send "OPEN <scan id>" over
     the current Windows user's named pipe; the app opens the scan's Farm plant.
  2. Otherwise, with XR_PLAYER_PATH set to an installed PlantDiseaseVR.exe: start it with --crop-scan-id <scan id>.
The pipe name and protocol are the app's own (Assets/_Project/Scripts/Runtime/XR/XrScanLink.cs). Only a validated UUID
is ever sent or passed as an argument, and the routes using this accept only loopback callers with XR_LOCAL_LAUNCH on.
"""

from __future__ import annotations

import getpass
import hashlib
import logging
import os
import subprocess
import threading
import time
import uuid
from collections.abc import Callable
from pathlib import Path

log = logging.getLogger(__name__)

PIPE_PREFIX = "CropScanXR.OpenField."
EDITOR_PIPE_PREFIX = "CropScanXR.Editor."
EDITOR_METHOD = "BTP.Editor.XrEditorBridge.PlayFromCommandLine"


class LaunchError(Exception):
    """The scan could not be handed to CropScan XR; the message is shown to the learner."""


def _user_hash(user: str | None) -> str:
    user = (user or os.environ.get("USERNAME") or getpass.getuser() or "").lower()
    return hashlib.sha256(user.encode("utf-8")).hexdigest()[:16]


def pipe_name(user: str | None = None) -> str:
    """Same as XrScanLink.PipeName: sha256 of the lower-case Windows user name, first 8 bytes in hex."""
    return PIPE_PREFIX + _user_hash(user)


def editor_pipe_name(user: str | None = None) -> str:
    """Same as XrScanLink.EditorPipeName: the Unity Editor's receiver while it is open but not playing."""
    return EDITOR_PIPE_PREFIX + _user_hash(user)


def send(line: str, timeout: float = 2.0, name: str | None = None) -> str | None:
    """Sends one line to a CropScan XR receiver and returns its one-line answer, or None when nothing is listening."""
    if os.name != "nt":
        return None
    path = "\\\\.\\pipe\\" + (name or pipe_name())
    deadline = time.monotonic() + timeout
    while True:
        try:
            with open(path, "r+b", buffering=0) as pipe:
                pipe.write((line + "\n").encode("utf-8"))
                answer = b""
                while not answer.endswith(b"\n") and len(answer) < 256:
                    chunk = pipe.read(1)
                    if not chunk:
                        break
                    answer += chunk
                return answer.decode("utf-8", "replace").strip() or None
        except FileNotFoundError:
            return None          # no CropScan XR receiver on this computer
        except OSError:
            # The receiver serves one connection at a time; it is busy with another click.
            if time.monotonic() >= deadline:
                return None
            time.sleep(0.1)


def receiver_running() -> bool:
    return send("PING", timeout=1.0) == "OK"


def unity_editor_open(project: Path) -> bool:
    """True while a Unity Editor holds this project open (it keeps Temp/UnityLockfile locked)."""
    lock = project / "Temp" / "UnityLockfile"
    if not lock.exists():
        return False
    try:
        with open(lock, "a"):
            return False      # a lockfile nobody holds: left behind by a closed Editor
    except OSError:
        return True


def _start(args: list[str], cwd: Path) -> None:
    flags = 0
    if os.name == "nt":
        flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
    # Direct execution, argument list (no shell); the only variable argument is the validated UUID.
    subprocess.Popen(args, cwd=str(cwd), close_fds=True, creationflags=flags,
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def _editor_args(editor_path: Path, project_path: Path, canonical: str) -> list[str]:
    # The Editor reads --crop-scan-id itself once it has loaded (XrEditorBridge); -executeMethod is a second route.
    return [str(editor_path), "-projectPath", str(project_path), "-executeMethod", EDITOR_METHOD, "--crop-scan-id", canonical]


def _deliver_when_ready(canonical: str, editor_path: Path, project_path: Path, on_ready: Callable[[float], None],
                        timeout: float = 240.0) -> None:
    """Unity holds the project but is not listening yet (starting, compiling, importing or closing). Keeps trying in
    the background: hand the scan over as soon as Unity listens, or start Unity if it closed meanwhile."""

    def run():
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if send("OPEN " + canonical, 0.5) == "OK" or send("OPEN " + canonical, 0.5, name=editor_pipe_name()) == "OK":
                log.info("scan %s handed to Unity once it was ready", canonical)
                on_ready(10.0)
                return
            if not unity_editor_open(project_path):
                _start(_editor_args(editor_path, project_path, canonical), project_path)
                log.info("Unity had closed; started it with scan %s", canonical)
                on_ready(180.0)
                return
            time.sleep(1.0)
        log.warning("Unity did not become ready for scan %s within %.0f s", canonical, timeout)

    threading.Thread(target=run, name="cropscan-xr-deliver", daemon=True).start()


DEMO_ARGUMENT = "--crop-demo"
DEMO_EDITOR_METHOD = "BTP.Editor.XrEditorBridge.PlayDemoFromCommandLine"
# The Editor's bridge watches for this file under the project's Temp folder while it is open but not playing
# (Assets/_Project/Scripts/Editor/XrEditorBridge.cs). It carries no content: its presence is the whole message.
DEMO_REQUEST_FILE = "CropScanDemo.request"


def open_demo(player_path: Path | None, editor_path: Path | None = None, project_path: Path | None = None,
              on_ready: Callable[[float], None] | None = None) -> str:
    """Opens CropScan XR with no scan (See VR Demo), starting it when it is closed, and calls
    on_ready(seconds to wait for its window). Returns how, in the same words as open_scan: "forwarded" (already
    running: it only needs bringing to the front), "started" (installed player started), "unity-play" (Editor open:
    it presses Play), "unity-start" (Unity started on the project; it presses Play when loaded).
    Nothing variable is passed to a process: every argument here is a constant or a configured path."""
    ready = on_ready or (lambda seconds: None)
    if receiver_running():
        ready(10.0)
        return "forwarded"
    if player_path is not None and player_path.is_file():
        _start([str(player_path), "-single-instance"], player_path.parent)
        ready(180.0)
        return "started"
    if editor_path is not None and editor_path.is_file() and project_path is not None and project_path.is_dir():
        request = project_path / "Temp" / DEMO_REQUEST_FILE
        if unity_editor_open(project_path):
            request.parent.mkdir(exist_ok=True)
            request.write_text("demo\n", encoding="utf-8")
            ready(60.0)
            return "unity-play"
        # A request left behind by an earlier run must not fire on top of the command-line one.
        request.unlink(missing_ok=True)
        _start([str(editor_path), "-projectPath", str(project_path), "-executeMethod", DEMO_EDITOR_METHOD, DEMO_ARGUMENT],
               project_path)
        ready(240.0)
        return "unity-start"
    raise LaunchError(
        "CropScan XR is not installed on this computer and no Unity Editor is configured for it. Set XR_PLAYER_PATH "
        "or XR_UNITY_EDITOR_PATH and XR_UNITY_PROJECT_PATH in services/api/.env, then press See VR Demo again."
    )


def open_scan(scan_id: str, player_path: Path | None, editor_path: Path | None = None,
              project_path: Path | None = None, on_ready: Callable[[float], None] | None = None) -> str:
    """Hands a scan to CropScan XR, starting it when needed, and calls on_ready(seconds to wait for its window)
    once it is on its way. Returns how: "forwarded" (game running), "unity-play" (Editor open: it presses Play),
    "started" (installed player started), "unity-start" (Unity started on the project; it presses Play when loaded),
    "unity-wait" (Unity is busy starting, compiling or closing: the scan is delivered as soon as it can be)."""
    ready = on_ready or (lambda seconds: None)
    canonical = str(uuid.UUID(scan_id))
    answer = send("OPEN " + canonical)
    if answer == "OK":
        ready(10.0)
        return "forwarded"
    if answer:
        raise LaunchError(f"CropScan XR refused the scan: {answer}")
    answer = send("OPEN " + canonical, name=editor_pipe_name())
    if answer == "OK":
        ready(10.0)
        return "unity-play"
    if player_path is not None and player_path.is_file():
        _start([str(player_path), "--crop-scan-id", canonical, "-single-instance"], player_path.parent)
        ready(180.0)
        return "started"
    if editor_path is not None and editor_path.is_file() and project_path is not None and project_path.is_dir():
        if unity_editor_open(project_path):
            _deliver_when_ready(canonical, editor_path, project_path, ready)
            return "unity-wait"
        _start(_editor_args(editor_path, project_path, canonical), project_path)
        ready(180.0)
        return "unity-start"
    raise LaunchError(
        "CropScan XR is not running on this computer. In Unity, open the XRBootstrap scene and press Play "
        "(or install the Windows build with Tools/CropScanLauncher/Install-CropScanXR.ps1), then press Enter the field again."
    )
