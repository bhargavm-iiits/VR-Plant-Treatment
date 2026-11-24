"""CropScan XR: the Unity VR app's routes, following the checks in the implementation guide.

Ownership (a paired player opens only its web session's scans), eligibility (exact case map + recipe), one-time
pairing, session reopen vs a new scan, launches, treatment results written once per Idempotency-Key against the
pinned recipe and revision, and the device socket's readiness.
"""

import time

import pytest
from sqlalchemy import func, select
from sqlalchemy.orm import sessionmaker

from app import xr
from app.models import LedgerEntry, Scan, XrResultWrite


@pytest.fixture(autouse=True)
def fresh_redeem_limit():
    # The limiter is process-wide (10 attempts a minute per client); every test pairs from the same test client.
    xr.redeem_limiter.attempts.clear()
    yield
    xr.redeem_limiter.attempts.clear()


@pytest.fixture
def recipes(db_session):
    assert xr.seed_recipes(db_session) == 24
    return db_session


def browser(client) -> dict:
    token = client.post("/api/xr/browser-sessions").json()["sessionToken"]
    return {xr.SESSION_HEADER: token}


def pair(client, web: dict, name="Lab PC") -> dict:
    code = client.post("/api/xr/pairings", headers=web).json()["code"]
    body = client.post("/api/xr/pairings/redeem", json={"code": code, "deviceName": name, "platform": "windows-openxr"}).json()
    return {"Authorization": f"Bearer {body['deviceToken']}", "_device": body["deviceId"]}


def auth(device: dict) -> dict:
    return {k: v for k, v in device.items() if not k.startswith("_")}


def owned_scan(db_session, client, web: dict, disease="Tomato___Early_blight", status="ok") -> Scan:
    owner = xr.browser_session_for(db_session, web[xr.SESSION_HEADER])
    scan = Scan(image_key="scans/x/image.jpg", image_sha256="b" * 64, model_version="test",
                disease_id=disease if status == "ok" else None, confidence=0.9 if status == "ok" else 0.2,
                top3=[], status=status, owner_session_id=owner.id)
    db_session.add(scan)
    db_session.commit()
    return scan


def submission(session: dict, manual=7, outcome="Managed", revision=None) -> dict:
    return {
        "xrSessionId": session["xrSessionId"], "recipeId": session["recipeId"], "recipeVersion": session["recipeVersion"],
        "expectedRevision": session["revision"] if revision is None else revision, "manualSessionId": manual,
        "outcome": outcome, "initialSeverity": 0.35, "finalSeverity": 0.1, "initialHealth": 0.7, "finalHealth": 0.82,
        "score": 0.9, "valid": True, "applyCount": 1, "healthChecks": 2, "elapsedSeconds": 41.5,
        "appliedValues": [{"controlId": "sanitation", "value": 7}, {"controlId": "drip", "value": 8}],
    }


# --- eligibility ----------------------------------------------------------------------------------------------------

def test_scan_reports_vr_eligibility_from_the_shared_map(client, db_session, recipes):
    web = browser(client)
    tomato = owned_scan(db_session, client, web)
    potato = owned_scan(db_session, client, web, "Potato___Early_blight")
    healthy = owned_scan(db_session, client, web, "Tomato___healthy")
    unmapped = owned_scan(db_session, client, web, "Orange___Haunglongbing_(Citrus_greening)")
    uncertain = owned_scan(db_session, client, web, status="uncertain")

    assert client.get(f"/api/scans/{tomato.id}").json()["can_enter_vr"] is True
    assert client.get(f"/api/scans/{potato.id}").json()["can_enter_vr"] is True
    for scan, words in ((healthy, "healthy"), (unmapped, "not available"), (uncertain, "not confident")):
        body = client.get(f"/api/scans/{scan.id}").json()
        assert body["can_enter_vr"] is False
        assert words in body["vr_blocked_reason"]


def test_without_recipes_a_mapped_scan_is_not_eligible(client, db_session):
    web = browser(client)
    scan = owned_scan(db_session, client, web)
    body = client.get(f"/api/scans/{scan.id}").json()
    assert body["can_enter_vr"] is False
    assert "lesson" in body["vr_blocked_reason"]


def test_tomato_and_potato_early_blight_open_different_unity_cases(client, db_session, recipes):
    web = browser(client)
    device = pair(client, web)
    tomato = client.post("/api/xr/sessions", json={"scanId": owned_scan(db_session, client, web).id}, headers=auth(device)).json()
    potato = client.post("/api/xr/sessions", json={"scanId": owned_scan(db_session, client, web, "Potato___Early_blight").id},
                         headers=auth(device)).json()
    assert (tomato["plantId"], tomato["diseaseId"], tomato["specimenId"]) == ("tomato", "early_blight", "farm-tomato-early-blight-01")
    assert (potato["plantId"], potato["diseaseId"]) == ("potato", "early_blight")
    assert tomato["recipeId"] != potato["recipeId"]


# --- pairing and ownership -----------------------------------------------------------------------------------------

def test_pairing_code_is_one_time(client):
    web = browser(client)
    code = client.post("/api/xr/pairings", headers=web).json()["code"]
    first = client.post("/api/xr/pairings/redeem", json={"code": code.lower().replace("-", " ")})
    assert first.status_code == 200 and first.json()["deviceToken"]
    assert client.post("/api/xr/pairings/redeem", json={"code": code}).status_code == 400
    assert client.post("/api/xr/pairings/redeem", json={"code": "AAAA-AAAA"}).status_code == 400


def test_pairing_needs_a_browser_session(client):
    assert client.post("/api/xr/pairings").status_code == 401


def test_paired_player_opens_its_own_scan_and_others_get_an_access_error(client, db_session, recipes):
    web, other_web = browser(client), browser(client)
    device = pair(client, web)
    stranger = pair(client, other_web, "Other PC")
    scan = owned_scan(db_session, client, web)

    opened = client.post("/api/xr/sessions", json={"scanId": scan.id}, headers=auth(device))
    assert opened.status_code == 201
    assert opened.json()["scanId"] == scan.id and opened.json()["revision"] == 0 and opened.json()["treatmentResult"] is None

    assert client.post("/api/xr/sessions", json={"scanId": scan.id}, headers=auth(stranger)).status_code == 403
    assert client.post("/api/xr/sessions", json={"scanId": scan.id}).status_code == 401
    assert client.post("/api/xr/sessions", json={"scanId": scan.id},
                       headers={"Authorization": "Bearer not-a-token"}).status_code == 401
    # A foreign player cannot read the session either.
    assert client.get(f"/api/xr/sessions/{opened.json()['xrSessionId']}", headers=auth(stranger)).status_code == 404


def test_scans_without_an_owner_cannot_open(client, db_session, recipes, scan):
    device = pair(client, browser(client))
    response = client.post("/api/xr/sessions", json={"scanId": scan.id}, headers=auth(device))
    assert response.status_code == 403


def test_ineligible_scan_has_a_reason(client, db_session, recipes):
    web = browser(client)
    device = pair(client, web)
    unmapped = owned_scan(db_session, client, web, "Orange___Haunglongbing_(Citrus_greening)")
    response = client.post("/api/xr/sessions", json={"scanId": unmapped.id}, headers=auth(device))
    assert response.status_code == 422
    assert "not available" in response.json()["detail"]


def test_revoked_player_is_refused(client, db_session, recipes):
    web = browser(client)
    device = pair(client, web)
    assert client.delete(f"/api/xr/devices/{device['_device']}", headers=web).status_code == 204
    assert client.post("/api/xr/sessions", json={"scanId": owned_scan(db_session, client, web).id},
                       headers=auth(device)).status_code == 401


# --- sessions -------------------------------------------------------------------------------------------------------

def test_reopening_a_scan_returns_the_same_session_and_a_new_scan_gets_its_own(client, db_session, recipes):
    web = browser(client)
    device = pair(client, web)
    scan = owned_scan(db_session, client, web)
    first = client.post("/api/xr/sessions", json={"scanId": scan.id}, headers=auth(device))
    again = client.post("/api/xr/sessions", json={"scanId": scan.id}, headers=auth(device))
    assert (first.status_code, again.status_code) == (201, 200)
    assert again.json()["xrSessionId"] == first.json()["xrSessionId"]

    second_scan = owned_scan(db_session, client, web)   # same diagnosis, new scan
    second = client.post("/api/xr/sessions", json={"scanId": second_scan.id}, headers=auth(device)).json()
    assert second["xrSessionId"] != first.json()["xrSessionId"]
    # The browser that made the scan sees the same run on the result page.
    assert client.get("/api/xr/sessions", params={"scanId": scan.id}, headers=web).json()["xrSessionId"] == first.json()["xrSessionId"]


def test_recipe_is_pinned_to_the_session(client, db_session, recipes):
    web = browser(client)
    device = pair(client, web)
    session = client.post("/api/xr/sessions", json={"scanId": owned_scan(db_session, client, web).id}, headers=auth(device)).json()
    recipe = client.get(f"/api/xr/sessions/{session['xrSessionId']}/recipe", headers=auth(device)).json()
    assert (recipe["recipeId"], recipe["recipeVersion"]) == (session["recipeId"], session["recipeVersion"])
    assert [c["controlId"] for c in recipe["controls"]] == ["sanitation", "drip", "protect", "prevention"]


# --- launches -------------------------------------------------------------------------------------------------------

def test_launch_is_idempotent_and_its_status_reaches_the_result_page(client, db_session, recipes):
    web = browser(client)
    device = pair(client, web)
    scan = owned_scan(db_session, client, web)
    session = client.post("/api/xr/sessions", json={"scanId": scan.id}, headers=auth(device)).json()
    headers = {**auth(device), "Idempotency-Key": "app-link:1"}
    launch = client.post(f"/api/xr/sessions/{session['xrSessionId']}/launches", json={"scanId": scan.id}, headers=headers).json()
    again = client.post(f"/api/xr/sessions/{session['xrSessionId']}/launches", json={"scanId": scan.id}, headers=headers).json()
    assert again["launchId"] == launch["launchId"] and launch["status"] == "opening"

    status = client.post(f"/api/xr/launches/{launch['launchId']}/status",
                         json={"status": "opening", "detail": "Waiting for Quest Link.", "xrReady": False}, headers=auth(device))
    assert status.json()["detail"] == "Waiting for Quest Link."
    client.post(f"/api/xr/launches/{launch['launchId']}/status", json={"status": "opened", "xrReady": True}, headers=auth(device))
    # A late "opening" never undoes a finished launch.
    client.post(f"/api/xr/launches/{launch['launchId']}/status", json={"status": "opening"}, headers=auth(device))

    page = client.get("/api/xr/sessions", params={"scanId": scan.id}, headers=web).json()
    assert page["latestLaunch"]["status"] == "opened"
    assert client.get(f"/api/xr/launches/{launch['launchId']}", headers=web).json()["status"] == "opened"
    assert client.get("/api/xr/launches/pending", headers=auth(device)).json() == {"items": []}


# --- treatment results ----------------------------------------------------------------------------------------------

def _session(client, db_session):
    web = browser(client)
    device = pair(client, web)
    session = client.post("/api/xr/sessions", json={"scanId": owned_scan(db_session, client, web).id}, headers=auth(device)).json()
    return web, device, session


def test_lost_response_retried_with_the_same_key_writes_one_result(client, db_session, recipes):
    _, device, session = _session(client, db_session)
    url = f"/api/xr/sessions/{session['xrSessionId']}/treatment-result"
    headers = {**auth(device), "Idempotency-Key": "xr:s:m7:r0"}
    first = client.post(url, json=submission(session), headers=headers)
    retry = client.post(url, json=submission(session), headers=headers)
    assert (first.status_code, retry.status_code) == (201, 200)
    assert first.json()["revision"] == retry.json()["revision"] == 1
    assert first.json()["treatmentResult"]["improvement"] == "improved"
    assert db_session.scalar(select(func.count()).select_from(XrResultWrite)) == 1
    ledger_rows = db_session.scalar(select(func.count()).select_from(LedgerEntry).where(LedgerEntry.event_type == "xr.treatment_result"))
    assert ledger_rows == 1


def test_stale_revision_conflicts_with_the_canonical_session(client, db_session, recipes):
    _, device, session = _session(client, db_session)
    url = f"/api/xr/sessions/{session['xrSessionId']}/treatment-result"
    client.post(url, json=submission(session), headers={**auth(device), "Idempotency-Key": "k1"})
    conflict = client.post(url, json=submission(session, manual=8, outcome="PartlyManaged"), headers={**auth(device), "Idempotency-Key": "k2"})
    assert conflict.status_code == 409
    assert conflict.json()["revision"] == 1 and conflict.json()["treatmentResult"]["outcome"] == "Managed"
    saved = client.post(url, json=submission(session, manual=8, outcome="PartlyManaged", revision=1),
                        headers={**auth(device), "Idempotency-Key": "k3"})
    assert saved.status_code == 201 and saved.json()["treatmentResult"]["improvement"] == "partial"


@pytest.mark.parametrize("change, words", [
    ({"recipeVersion": 99}, "pins"),
    ({"outcome": "Cured"}, "not a known outcome"),
    ({"finalHealth": 1.5}, "between 0 and 1"),
    ({"appliedValues": [{"controlId": "magic", "value": 1}]}, "not part of recipe"),
    ({"appliedValues": [{"controlId": "drip", "value": 42}]}, "outside its range"),
])
def test_results_are_validated_against_the_pinned_recipe(client, db_session, recipes, change, words):
    _, device, session = _session(client, db_session)
    body = {**submission(session), **change}
    response = client.post(f"/api/xr/sessions/{session['xrSessionId']}/treatment-result", json=body,
                           headers={**auth(device), "Idempotency-Key": "bad"})
    assert response.status_code == 422
    assert words in response.json()["detail"]


def test_only_the_paired_player_saves_results(client, db_session, recipes):
    web, _, session = _session(client, db_session)
    url = f"/api/xr/sessions/{session['xrSessionId']}/treatment-result"
    assert client.post(url, json=submission(session), headers={**web, "Idempotency-Key": "k"}).status_code == 401
    stranger = pair(client, browser(client), "Other PC")
    assert client.post(url, json=submission(session), headers={**auth(stranger), "Idempotency-Key": "k"}).status_code == 404
    assert client.post(url, json=submission(session), headers=auth(pair(client, web))).status_code == 400   # no key


# --- device socket --------------------------------------------------------------------------------------------------

def test_socket_heartbeat_reports_headset_readiness(client, db_session, monkeypatch):
    # The socket opens its own database sessions; point them at the test database.
    engine = db_session.get_bind()
    monkeypatch.setattr("app.routers.xr.get_sessionmaker", lambda: sessionmaker(bind=engine, expire_on_commit=False))
    web = browser(client)
    device = pair(client, web)
    with client.websocket_connect("/ws/xr", headers=auth(device)) as socket:
        socket.send_json({"type": "device.hello", "xrReady": True, "display": "running", "appVersion": "1.0"})
        socket.send_json({"type": "device.heartbeat", "xrReady": True, "display": "running"})
        # The socket handles messages on its own task; wait for the heartbeat to land.
        deadline = time.monotonic() + 3
        devices = client.get("/api/xr/devices", headers=web).json()
        while not devices[0]["xrReady"] and time.monotonic() < deadline:
            time.sleep(0.05)
            devices = client.get("/api/xr/devices", headers=web).json()
    assert devices[0]["xrReady"] is True and devices[0]["socketConnected"] is True and devices[0]["stale"] is False

    with pytest.raises(Exception):
        with client.websocket_connect("/ws/xr", headers={"Authorization": "Bearer nope"}) as socket:
            socket.receive_json()


# --- local launch (Enter the field on the laptop running the API) ---------------------------------------------------

def test_pipe_name_matches_the_unity_app():
    from app import local_launch
    # XrSessionTests asserts the same value for XrScanLink.PipeName("Alice").
    assert local_launch.pipe_name("Alice") == "CropScanXR.OpenField.2bd806c97f0e00af"


def test_local_launch_is_off_for_remote_callers(client, db_session, recipes):
    web = browser(client)
    scan = owned_scan(db_session, client, web)
    assert client.get("/api/xr/local-launch").json() == {"available": False}
    assert client.post("/api/xr/local-launch", json={"scanId": scan.id}, headers=web).status_code == 404


@pytest.fixture
def local(monkeypatch):
    monkeypatch.setattr("app.routers.xr._local_launch_allowed", lambda request: True)
    # Tests never move windows on the developer's desktop, and never start a real Unity or player.
    monkeypatch.setattr("app.window_focus.focus_app", lambda *a, **k: None)
    monkeypatch.setattr("app.local_launch.subprocess.Popen", lambda *a, **k: pytest.fail("a test tried to start a real process"))
    sent = []

    def fake_send(line, timeout=2.0, name=None):
        sent.append(line)
        is_editor = name is not None and name.startswith("CropScanXR.Editor.")
        return fake_send.editor_answer if is_editor else fake_send.answer

    fake_send.editor_answer = None

    fake_send.answer = "OK"
    monkeypatch.setattr("app.local_launch.send", fake_send)
    return fake_send, sent


def test_local_launch_forwards_the_scan_to_the_running_app(client, db_session, recipes, local):
    fake_send, sent = local
    web = browser(client)
    scan = owned_scan(db_session, client, web)
    response = client.post("/api/xr/local-launch", json={"scanId": scan.id}, headers=web)
    assert response.status_code == 200 and response.json()["status"] == "forwarded"
    assert sent == [f"OPEN {scan.id}"]


def test_local_launch_starts_the_installed_player(client, db_session, recipes, local, monkeypatch, tmp_path):
    fake_send, _ = local
    fake_send.answer = None   # nothing listening
    player = tmp_path / "PlantDiseaseVR.exe"
    player.write_bytes(b"")
    from app.config import get_settings
    monkeypatch.setattr(get_settings(), "xr_player_path", player)
    started = []
    monkeypatch.setattr("app.local_launch.subprocess.Popen", lambda args, **kw: started.append(args))
    web = browser(client)
    scan = owned_scan(db_session, client, web)
    response = client.post("/api/xr/local-launch", json={"scanId": scan.id}, headers=web)
    assert response.json()["status"] == "started"
    assert started == [[str(player), "--crop-scan-id", scan.id, "-single-instance"]]


def test_local_launch_explains_when_nothing_can_open_it(client, db_session, recipes, local, monkeypatch):
    fake_send, _ = local
    fake_send.answer = None
    from app.config import get_settings
    settings = get_settings()
    monkeypatch.setattr(settings, "xr_player_path", None)
    monkeypatch.setattr(settings, "xr_unity_editor_path", None)
    web = browser(client)
    scan = owned_scan(db_session, client, web)
    response = client.post("/api/xr/local-launch", json={"scanId": scan.id}, headers=web)
    assert response.status_code == 409 and "press Play" in response.json()["detail"]


def test_local_launch_checks_ownership_and_eligibility(client, db_session, recipes, local):
    web, other = browser(client), browser(client)
    scan = owned_scan(db_session, client, web)
    assert client.post("/api/xr/local-launch", json={"scanId": scan.id}, headers=other).status_code == 403
    healthy = owned_scan(db_session, client, web, "Tomato___healthy")
    assert client.post("/api/xr/local-launch", json={"scanId": healthy.id}, headers=web).status_code == 422
    assert local[1] == []   # nothing was sent to the app


def test_open_unity_editor_presses_play_with_the_scan(client, db_session, recipes, local):
    fake_send, sent = local
    fake_send.answer, fake_send.editor_answer = None, "OK"   # Editor open, not playing
    web = browser(client)
    scan = owned_scan(db_session, client, web)
    response = client.post("/api/xr/local-launch", json={"scanId": scan.id}, headers=web)
    assert response.json()["status"] == "unity-play"
    assert sent == [f"OPEN {scan.id}", f"OPEN {scan.id}"]


def test_closed_unity_is_started_on_the_project(client, db_session, recipes, local, monkeypatch, tmp_path):
    fake_send, _ = local
    fake_send.answer = fake_send.editor_answer = None
    editor = tmp_path / "Unity.exe"
    editor.write_bytes(b"")
    project = tmp_path / "Project"
    project.mkdir()
    from app.config import get_settings
    settings = get_settings()
    monkeypatch.setattr(settings, "xr_player_path", None)
    monkeypatch.setattr(settings, "xr_unity_editor_path", editor)
    monkeypatch.setattr(settings, "xr_unity_project_path", project)
    started = []
    monkeypatch.setattr("app.local_launch.subprocess.Popen", lambda args, **kw: started.append(args))
    web = browser(client)
    scan = owned_scan(db_session, client, web)
    response = client.post("/api/xr/local-launch", json={"scanId": scan.id}, headers=web)
    assert response.json()["status"] == "unity-start"
    assert started == [[str(editor), "-projectPath", str(project), "-executeMethod",
                        "BTP.Editor.XrEditorBridge.PlayFromCommandLine", "--crop-scan-id", scan.id]]

    # A Unity that holds the project but is not listening yet (starting, compiling, closing) is never started a
    # second time: the scan is delivered in the background as soon as it can be.
    monkeypatch.setattr("app.local_launch.unity_editor_open", lambda path: True)
    waiting = []
    monkeypatch.setattr("app.local_launch._deliver_when_ready", lambda *args, **kw: waiting.append(args[0]))
    again = client.post("/api/xr/local-launch", json={"scanId": scan.id}, headers=web)
    assert again.status_code == 200 and again.json()["status"] == "unity-wait"
    assert waiting == [scan.id] and len(started) == 1


class _Inline:
    """Runs a background thread's target straight away, so the delivery loop can be tested synchronously."""

    def __init__(self, target=None, **kw):
        self.target = target

    def start(self):
        self.target()


def test_busy_unity_gets_the_scan_once_it_listens(monkeypatch, tmp_path):
    from app import local_launch
    answers = iter([None, None, "OK"])   # game pipe, Editor pipe (busy), then the game pipe answers
    monkeypatch.setattr(local_launch, "send", lambda line, timeout=2.0, name=None: next(answers))
    monkeypatch.setattr(local_launch, "unity_editor_open", lambda path: True)
    monkeypatch.setattr(local_launch.threading, "Thread", _Inline)
    monkeypatch.setattr(local_launch.time, "sleep", lambda s: None)
    ready = []
    local_launch._deliver_when_ready("3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47", tmp_path / "Unity.exe", tmp_path, ready.append)
    assert ready == [10.0]


def test_unity_that_closed_while_waiting_is_started(monkeypatch, tmp_path):
    from app import local_launch
    monkeypatch.setattr(local_launch, "send", lambda line, timeout=2.0, name=None: None)
    monkeypatch.setattr(local_launch, "unity_editor_open", lambda path: False)
    monkeypatch.setattr(local_launch.threading, "Thread", _Inline)
    started = []
    monkeypatch.setattr(local_launch, "_start", lambda args, cwd: started.append(args))
    ready = []
    local_launch._deliver_when_ready("3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47", tmp_path / "Unity.exe", tmp_path, ready.append)
    assert ready == [180.0] and started and started[0][-2:] == ["--crop-scan-id", "3f2b8c1e-9d4a-4b7e-8f21-6c0d5e9a1b47"]


def test_editor_pipe_name_matches_the_unity_app():
    from app import local_launch
    assert local_launch.editor_pipe_name("Alice") == "CropScanXR.Editor.2bd806c97f0e00af"


def test_every_farm_disease_opens_at_its_own_row(client, db_session, recipes):
    """Whichever disease the uploaded leaf shows, the session points at that disease's Farm row."""
    web = browser(client)
    device = pair(client, web)
    specimens = set()
    for backend_id, row in xr.case_map().items():
        session = client.post("/api/xr/sessions", json={"scanId": owned_scan(db_session, client, web, backend_id).id},
                              headers=auth(device))
        assert session.status_code == 201, backend_id
        body = session.json()
        assert (body["plantId"], body["diseaseId"], body["specimenId"]) == (row["plantId"], row["diseaseId"], row["specimenId"])
        specimens.add(body["specimenId"])
    assert len(specimens) == 24


def test_local_app_pairs_itself_for_the_scans_owner(client, db_session, recipes, monkeypatch):
    web = browser(client)
    scan = owned_scan(db_session, client, web)
    assert client.post("/api/xr/local-pair", json={"scanId": scan.id}).status_code == 404   # not a local caller
    monkeypatch.setattr("app.routers.xr._local_launch_allowed", lambda request: True)
    first = client.post("/api/xr/local-pair", json={"scanId": scan.id, "deviceName": "Lab PC"}).json()
    device = {"Authorization": f"Bearer {first['deviceToken']}"}
    assert client.post("/api/xr/sessions", json={"scanId": scan.id}, headers=device).status_code == 201
    # Pairing again retires the earlier local credential.
    client.post("/api/xr/local-pair", json={"scanId": scan.id})
    assert client.post("/api/xr/sessions", json={"scanId": scan.id}, headers=device).status_code == 401
    assert client.post("/api/xr/local-pair", json={"scanId": "nope"}).status_code == 404
