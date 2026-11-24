import sqlite3
from copy import deepcopy
from pathlib import Path
from urllib.parse import quote

import pytest

from app.content import disease_info

CASES = "/api/treatment/cases"
EXPECTED_CONTROLS = [
    ("temperature", ["day", "night"]),
    ("fertiliser", ["dose", "applications"]),
    ("spray", ["dose", "targetCoverage"]),
    ("soilMoisture", ["rootZone", "irrigation"]),
]


def get_profile(client, case_id, scenario="standard"):
    response = client.get(f"{CASES}/{case_id}", params={"scenario": scenario})
    assert response.status_code == 200, response.text
    return response.json()


def settings(profile, numeric_key=None):
    return {
        factor["id"]: {
            sub["id"]: sub.get(numeric_key, sub["start"]) for sub in factor["subfactors"]
        } for factor in profile["factors"]
    }


def target_settings(profile):
    values = settings(profile, "targetMin")
    values["soilMoisture"]["irrigation"] = "rootZone"
    return values


def submit(client, profile, values, version=None):
    return client.post(f"{CASES}/{profile['caseId']}/evaluate", params={"scenario": profile["scenario"]},
                       json={"profileVersion": version or profile["profileVersion"], "values": values})


def test_catalog_matches_the_unity_cases_and_the_existing_scan_catalog(client):
    rows = client.get(CASES).json()
    assert len(rows) == 58
    assert {row["scanDiseaseId"] for row in rows if row["scanDiseaseId"]} == {
        key for key, info in disease_info().items() if not info["is_healthy"]
    }
    assert sum(row["scanDiseaseId"] is None for row in rows) == 10
    assert len(client.get(CASES, params={"plant_id": "soybean"}).json()) == 9

    sql = Path(__file__).resolve().parents[4] / "Assets/_Project/Data/Treatment/treatment_content.sql"
    if sql.is_file():
        with sqlite3.connect(":memory:") as db:
            db.executescript(sql.read_text(encoding="utf-8"))
            authored = {f"{plant}/{disease}": "Contained" if outcome == "Replaced" else outcome
                        for plant, disease, outcome in db.execute("SELECT plant_id, disease_id, outcome FROM cases")}
        assert {row["caseId"]: row["outcome"] for row in rows} == authored


def test_all_cases_use_the_same_controls_and_reward_targets_over_maxing_out(client):
    for case in client.get(CASES).json():
        for scenario in case["scenarios"]:
            profile = get_profile(client, case["caseId"], scenario)
            assert [(f["id"], [s["id"] for s in f["subfactors"]]) for f in profile["factors"]] == EXPECTED_CONTROLS
            target = submit(client, profile, target_settings(profile))
            assert target.status_code == 200, (case["caseId"], target.text)
            assert target.json()["passed"] and target.json()["score"] == 1
            assert target.json()["outcome"] == case["outcome"]

            maximums = settings(profile, "max")
            maximums["soilMoisture"]["irrigation"] = "overhead"
            excess = submit(client, profile, maximums).json()
            assert not excess["passed"] and excess["score"] < target.json()["score"], case["caseId"]


def test_mild_corn_rust_rewards_holding_steady_and_zero_spray(client):
    profile = get_profile(client, "corn/common_rust")
    values = settings(profile)
    result = submit(client, profile, values).json()
    assert result["score"] == 1 and result["outcome"] == "Managed"
    assert result["factors"][2]["score"] == 1

    values["spray"] = {"dose": 3, "targetCoverage": 80}
    sprayed = submit(client, profile, values).json()
    assert not sprayed["passed"] and sprayed["factors"][2]["score"] < 1


def test_rice_brown_spot_uses_confirmed_nutrition_and_severe_spray_branch(client):
    case = "rice/brown_spot"
    standard = get_profile(client, case)
    severe = get_profile(client, case, "severe")
    assert standard["factors"][1]["nutrient"] == "Potassium"
    assert "soil test" in standard["factors"][1]["evidence"]
    assert not standard["factors"][2]["indicated"] and severe["factors"][2]["indicated"]
    assert severe["factors"][2]["target"] == "Marked affected rice canopy"
    assert submit(client, severe, target_settings(severe)).json()["score"] == 1
    assert not submit(client, severe, target_settings(standard)).json()["passed"]
    assert not submit(client, standard, target_settings(severe)).json()["passed"]

    no_coverage = target_settings(severe)
    no_coverage["spray"]["targetCoverage"] = 0
    assert not submit(client, severe, no_coverage).json()["factors"][2]["subfactors"][1]["onTarget"]


def test_soil_moisture_is_independent_from_leaf_wetness(client):
    profile = get_profile(client, "tomato/septoria_leaf_spot")
    values = target_settings(profile)
    drip = submit(client, profile, values).json()
    values["soilMoisture"]["irrigation"] = "overhead"
    overhead = submit(client, profile, values).json()
    assert drip["rootZoneMoisture"] == overhead["rootZoneMoisture"]
    assert drip["leafWetness"] < overhead["leafWetness"]
    assert drip["score"] > overhead["score"]

    values["soilMoisture"]["irrigation"] = "hold"
    held = submit(client, profile, values).json()
    assert held["rootZoneMoisture"] != drip["rootZoneMoisture"] and not held["passed"]


def test_virus_result_keeps_containment_separate_from_replacement_health(client):
    profile = get_profile(client, "tomato/yellow_leaf_curl_virus")
    assert profile["careTarget"] == "healthyReplacement"
    assert profile["outcomeLabel"] == "Contained and replaced"
    assert profile["factors"][1]["nutrient"] is None
    result = submit(client, profile, settings(profile)).json()
    assert not result["passed"] and result["outcome"] == "Contained"
    assert submit(client, profile, target_settings(profile)).json()["passed"]


def test_profiles_can_be_opened_from_scan_ids_and_unmapped_unity_ids(client):
    scan_id = quote("Tomato___Spider_mites Two-spotted_spider_mite", safe="")
    assert client.get(f"/api/diseases/{scan_id}/treatment").json() == get_profile(client, "tomato/spider_mites")
    assert get_profile(client, "soybean/frogeye_leaf_spot")["scanDiseaseId"] is None
    assert client.get("/api/diseases/Tomato___healthy/treatment").status_code == 404
    assert client.get(f"{CASES}/no_such/leaf").status_code == 404
    assert client.get(f"{CASES}/corn/common_rust", params={"scenario": "severe"}).status_code == 404


@pytest.mark.parametrize(("factor", "setting", "bad_value"), [
    ("temperature", "day", 41), ("temperature", "night", True), ("fertiliser", "applications", 1.5),
    ("spray", "tank", 8), ("soilMoisture", "irrigation", "unknown"),
])
def test_evaluation_rejects_extra_or_invalid_settings(client, factor, setting, bad_value):
    profile = get_profile(client, "corn/common_rust")
    values = target_settings(profile)
    values[factor][setting] = bad_value
    assert submit(client, profile, values).status_code == 422


def test_evaluation_requires_all_eight_subfactors_and_current_version(client):
    profile = get_profile(client, "corn/common_rust")
    values = target_settings(profile)
    assert submit(client, profile, values, version=99).status_code == 409
    missing = deepcopy(values)
    del missing["temperature"]["night"]
    assert submit(client, profile, missing).status_code == 422
