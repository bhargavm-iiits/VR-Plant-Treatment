"""The study's activity log, and See VR Demo opening Unity with no scan."""

import uuid

import pytest
from sqlalchemy import func, select

from app import local_launch, xr
from app.config import get_settings
from app.models import ActivityEvent


def batch(visit=None, events=None, **extra) -> dict:
    return {"visitId": visit or str(uuid.uuid4()), "source": "web", "lang": "en", "viewport": "1586x992",
            "events": events if events is not None else [
                {"seq": 0, "kind": "page_view", "ts": 1_700_000_000_000, "path": "/"},
                {"seq": 1, "kind": "click", "ts": 1_700_000_001_000, "path": "/", "target": "Detect Disease",
                 "data": {"role": "link"}},
            ], **extra}


def count(db) -> int:
    return db.scalar(select(func.count()).select_from(ActivityEvent))


def test_a_batch_is_stored_with_its_session(client, db_session):
    token = client.post("/api/xr/browser-sessions").json()["sessionToken"]
    body = batch()
    answer = client.post("/api/activity", json=body, headers={xr.SESSION_HEADER: token})
    assert answer.status_code == 202 and answer.json() == {"accepted": 2, "skipped": 0}

    rows = list(db_session.scalars(select(ActivityEvent).order_by(ActivityEvent.seq)))
    assert [r.kind for r in rows] == ["page_view", "click"]
    assert rows[1].target == "Detect Disease" and rows[1].data == {"role": "link"}
    assert rows[0].browser_session_id == xr.browser_session_for(db_session, token).id
    assert rows[0].client_ts is not None and rows[0].viewport == "1586x992"


def test_a_retried_batch_is_not_counted_twice(client, db_session):
    body = batch()
    client.post("/api/activity", json=body)
    again = client.post("/api/activity", json=body)
    assert again.json() == {"accepted": 0, "skipped": 2}
    assert count(db_session) == 2


def test_the_unity_app_logs_to_the_same_table(client, db_session):
    body = batch(events=[{"seq": 0, "kind": "scene_loaded", "target": "Farm"}], source="unity")
    assert client.post("/api/activity", json=body).status_code == 202
    assert db_session.scalar(select(ActivityEvent.source)) == "unity"


@pytest.mark.parametrize("change, status", [
    ({"visitId": "not-a-uuid"}, 422),
    ({"source": "elsewhere"}, 422),
    ({"events": "nope"}, 422),
    ({"events": [{"seq": i, "kind": "x"} for i in range(201)]}, 413),
])
def test_a_malformed_batch_is_refused(client, db_session, change, status):
    assert client.post("/api/activity", json={**batch(), **change}).status_code == status
    assert count(db_session) == 0


def test_bad_events_are_skipped_and_large_data_is_dropped(client, db_session):
    events = [
        {"seq": 0, "kind": "ok"},
        {"seq": -1, "kind": "negative"},
        {"seq": "2", "kind": "text seq"},
        {"seq": 3},
        "not an event",
        {"seq": 4, "kind": "big", "data": {"blob": "x" * 5000}},
        {"seq": 5, "kind": "k" * 200, "target": "t" * 500},
    ]
    assert client.post("/api/activity", json=batch(events=events)).json() == {"accepted": 3, "skipped": 4}
    rows = {r.seq: r for r in db_session.scalars(select(ActivityEvent))}
    assert set(rows) == {0, 4, 5}
    assert rows[4].data == {"dropped": "data too large"}
    assert len(rows[5].kind) == 48 and len(rows[5].target) == 160


def test_the_log_is_only_readable_from_this_computer(client, db_session, monkeypatch):
    visit = str(uuid.uuid4())
    client.post("/api/activity", json=batch(visit=visit))
    # The test client is not a loopback caller, so this is the network's view of the log.
    assert client.get("/api/activity").status_code == 404
    assert client.get("/api/activity.csv").status_code == 404

    monkeypatch.setattr("app.routers.activity._local_only", lambda request: None)
    listing = client.get("/api/activity", params={"visitId": visit, "kind": "click"}).json()
    assert listing["count"] == 1 and listing["events"][0]["target"] == "Detect Disease"


def test_the_csv_is_a_timeline_and_neutralises_formulas(client, db_session, monkeypatch):
    monkeypatch.setattr("app.routers.activity._local_only", lambda request: None)
    client.post("/api/activity", json=batch(events=[
        {"seq": 0, "kind": "page_view", "path": "/"},
        {"seq": 1, "kind": "click", "target": "=HYPERLINK(1)"},
    ]))
    lines = client.get("/api/activity.csv").text.strip().splitlines()
    assert lines[0].startswith("createdAt,clientTs,source,visitId,seq")
    assert ",page_view," in lines[1] and "'=HYPERLINK(1)" in lines[2]


# --- See VR Demo: open the Unity app with no scan ---------------------------------------------------------------------

@pytest.fixture
def local(monkeypatch):
    monkeypatch.setattr("app.routers.xr._local_launch_allowed", lambda request: True)
    monkeypatch.setattr("app.window_focus.focus_app", lambda *a, **k: None)
    monkeypatch.setattr("app.local_launch.subprocess.Popen", lambda *a, **k: pytest.fail("a test tried to start a real process"))
    monkeypatch.setattr("app.local_launch.receiver_running", lambda: False)


def test_demo_is_refused_away_from_this_computer(client):
    assert client.post("/api/xr/local-demo").status_code == 404


def test_demo_brings_a_running_app_to_the_front(client, local, monkeypatch):
    monkeypatch.setattr("app.local_launch.receiver_running", lambda: True)
    assert client.post("/api/xr/local-demo").json() == {"status": "forwarded"}


def test_demo_starts_the_installed_player(client, local, monkeypatch, tmp_path):
    player = tmp_path / "PlantDiseaseVR.exe"
    player.write_bytes(b"")
    monkeypatch.setattr(get_settings(), "xr_player_path", player)
    started = []
    monkeypatch.setattr("app.local_launch.subprocess.Popen", lambda args, **kw: started.append(args))
    assert client.post("/api/xr/local-demo").json() == {"status": "started"}
    assert started == [[str(player), "-single-instance"]]


def unity(monkeypatch, tmp_path):
    editor = tmp_path / "Unity.exe"
    editor.write_bytes(b"")
    project = tmp_path / "Project"
    project.mkdir()
    monkeypatch.setattr(get_settings(), "xr_player_path", None)
    monkeypatch.setattr(get_settings(), "xr_unity_editor_path", editor)
    monkeypatch.setattr(get_settings(), "xr_unity_project_path", project)
    return editor, project


def test_demo_starts_a_closed_unity_and_asks_it_to_play(client, local, monkeypatch, tmp_path):
    editor, project = unity(monkeypatch, tmp_path)
    stale = project / "Temp" / local_launch.DEMO_REQUEST_FILE
    stale.parent.mkdir()
    stale.write_text("old")
    started = []
    monkeypatch.setattr("app.local_launch.subprocess.Popen", lambda args, **kw: started.append(args))

    assert client.post("/api/xr/local-demo").json() == {"status": "unity-start"}
    assert started == [[str(editor), "-projectPath", str(project), "-executeMethod",
                        local_launch.DEMO_EDITOR_METHOD, local_launch.DEMO_ARGUMENT]]
    assert not stale.exists()      # an old request must not fire on top of the command-line one


def test_demo_asks_an_open_unity_to_play(client, local, monkeypatch, tmp_path):
    _, project = unity(monkeypatch, tmp_path)
    monkeypatch.setattr("app.local_launch.unity_editor_open", lambda path: True)
    assert client.post("/api/xr/local-demo").json() == {"status": "unity-play"}
    assert (project / "Temp" / local_launch.DEMO_REQUEST_FILE).is_file()


def test_demo_explains_itself_when_nothing_can_be_started(client, local, monkeypatch):
    for name in ("xr_player_path", "xr_unity_editor_path", "xr_unity_project_path"):
        monkeypatch.setattr(get_settings(), name, None)
    answer = client.post("/api/xr/local-demo")
    assert answer.status_code == 409 and "XR_UNITY_EDITOR_PATH" in answer.json()["detail"]
