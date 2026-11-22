"""Versioned treatment lessons for the 58 Unity leaf specimens."""

from functools import lru_cache
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field

from app.config import get_settings
from app.content import disease_info


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, allow_inf_nan=False)


Band = Annotated[list[float], Field(min_length=2, max_length=2)]
Celsius = Annotated[float, Field(ge=5, le=40)]
Dose = Annotated[float, Field(ge=0, le=10)]
Percent = Annotated[float, Field(ge=0, le=100)]
Applications = Annotated[int, Field(ge=0, le=3)]
Irrigation = Literal["hold", "rootZone", "overhead"]
Outcome = Literal["Managed", "PestSuppressed", "Contained"]


class TemperatureValues(StrictModel):
    day: Celsius
    night: Celsius


class FertiliserValues(StrictModel):
    dose: Dose
    applications: Applications


class SprayValues(StrictModel):
    dose: Dose
    targetCoverage: Percent


class SoilValues(StrictModel):
    rootZone: Percent
    irrigation: Irrigation


class SettingsValues(StrictModel):
    temperature: TemperatureValues
    fertiliser: FertiliserValues
    spray: SprayValues
    soilMoisture: SoilValues


class Evaluation(StrictModel):
    profileVersion: Annotated[int, Field(ge=1)]
    values: SettingsValues


class CropTargets(StrictModel):
    day: Band
    night: Band
    rootZone: Band
    irrigationLabel: str


class Deficiency(StrictModel):
    nutrient: str
    evidence: str
    dose: Band
    applications: Applications


class Spray(StrictModel):
    target: str
    dose: Band
    coverageMin: Percent


class Scenario(StrictModel):
    description: str
    spray: Spray


class Case(StrictModel):
    plantId: str
    diseaseId: str
    scanDiseaseId: str | None
    plant: str
    disease: str
    description: str
    outcome: Outcome
    outcomeFeedback: str
    startingTemperature: TemperatureValues
    startingRootZone: Percent
    startingLeafWetness: Percent
    spray: Spray | None
    deficiency: Deficiency | None = None
    dayTarget: Band | None = None
    nightTarget: Band | None = None
    rootZoneTarget: Band | None = None
    scenarios: dict[str, Scenario] = Field(default_factory=dict)


class Catalog(StrictModel):
    version: Annotated[int, Field(ge=1)]
    crops: dict[str, CropTargets]
    cases: list[Case]


OUTCOME_LABELS = {
    "Managed": "Managed",
    "PestSuppressed": "Pest suppressed",
    "Contained": "Contained and replaced",
}
REPLACEMENT_SETUP = "The infected specimen has been contained. Use the controls to establish the healthy replacement."


@lru_cache(maxsize=1)
def load_catalog() -> tuple[int, dict[tuple[str, str], Case], dict[str, CropTargets], dict[str, Case]]:
    path = get_settings().data_dir / "treatment_profiles.json"
    catalog = Catalog.model_validate_json(path.read_bytes())
    cases = {(case.plantId, case.diseaseId): case for case in catalog.cases}
    by_scan = {case.scanDiseaseId: case for case in cases.values() if case.scanDiseaseId}
    scan_ids = {key for key, info in disease_info().items() if not info["is_healthy"]}
    if len(cases) != len(catalog.cases) or len(by_scan) != sum(bool(c.scanDiseaseId) for c in cases.values()):
        raise ValueError(f"{path}: duplicate treatment case or scan disease ID")
    if by_scan.keys() != scan_ids:
        raise ValueError(f"{path}: scan disease IDs differ from the diseased scan catalog")
    for case in cases.values():
        for scenario in ("standard", *case.scenarios):
            profile = build_profile(catalog.version, catalog.crops[case.plantId], case, scenario)
            for factor in profile["factors"]:
                for sub in factor["subfactors"]:
                    if "min" in sub and not sub["min"] <= sub["targetMin"] <= sub["targetMax"] <= sub["max"]:
                        raise ValueError(f"{path}: invalid {sub['id']} band for {case.plantId}/{case.diseaseId}")
    return catalog.version, cases, catalog.crops, by_scan


def summary(case: Case) -> dict:
    return {
        "caseId": f"{case.plantId}/{case.diseaseId}",
        "plantId": case.plantId, "diseaseId": case.diseaseId, "scanDiseaseId": case.scanDiseaseId,
        "plant": case.plant, "disease": case.disease, "scenarios": ["standard", *case.scenarios],
        "outcome": case.outcome, "outcomeLabel": OUTCOME_LABELS[case.outcome],
    }


def numeric(id: str, label: str, unit: str, minimum: float, maximum: float, step: float,
            start: float, band: list[float]) -> dict:
    return {
        "id": id, "label": label, "unit": unit,
        "min": minimum, "max": maximum, "step": step, "start": start,
        "targetMin": band[0], "targetMax": band[1],
    }


def build_profile(version: int, crop: CropTargets, case: Case, scenario: str) -> dict:
    variant = case.scenarios.get(scenario)
    spray = variant.spray if variant else case.spray
    deficiency = case.deficiency
    replacing = case.outcome == "Contained"
    soil_band = case.rootZoneTarget or crop.rootZone
    may_hold = soil_band[0] <= case.startingRootZone <= soil_band[1]
    return {
        **summary(case),
        "profileVersion": version, "scenario": scenario,
        "description": variant.description if variant else case.description,
        "trainingNote": "Settings and doses are simulation values for this lesson.",
        "outcomeFeedback": case.outcomeFeedback,
        "careTarget": "healthyReplacement" if replacing else "affectedPlant",
        "setup": REPLACEMENT_SETUP if replacing else None,
        "leafWetnessStart": case.startingLeafWetness,
        "factors": [
            {"id": "temperature", "label": "Temperature", "subfactors": [
                numeric("day", "Day temperature", "°C", 5, 40, 0.5, case.startingTemperature.day, case.dayTarget or crop.day),
                numeric("night", "Night temperature", "°C", 5, 40, 0.5, case.startingTemperature.night, case.nightTarget or crop.night),
            ]},
            {"id": "fertiliser", "label": "Fertiliser",
             "nutrient": deficiency.nutrient if deficiency else None,
             "evidence": deficiency.evidence if deficiency else "The lesson has no confirmed nutrient deficiency. Keep the baseline nutrition and add no supplement.",
             "subfactors": [
                numeric("dose", "Supplement amount", "training units", 0, 10, 1, 0, deficiency.dose if deficiency else [0, 0]),
                numeric("applications", "Supplement applications", "applications", 0, 3, 1, 0, [deficiency.applications] * 2 if deficiency else [0, 0]),
            ]},
            {"id": "spray", "label": "Spray", "indicated": spray is not None,
             "target": spray.target if spray else None, "subfactors": [
                numeric("dose", "Training spray amount", "training units", 0, 10, 1, 0, spray.dose if spray else [0, 0]),
                numeric("targetCoverage", "Coverage on the indicated target", "%", 0, 100, 5, 0, [spray.coverageMin, 100] if spray else [0, 0]),
            ]},
            {"id": "soilMoisture", "label": "Soil Moisture", "subfactors": [
                numeric("rootZone", "Root-zone moisture", "%", 0, 100, 1, case.startingRootZone, soil_band),
                {"id": "irrigation", "label": "Water delivery", "start": "hold", "options": [
                    {"value": "hold", "label": "Hold steady"},
                    {"value": "rootZone", "label": crop.irrigationLabel},
                    {"value": "overhead", "label": "Overhead watering"},
                ], "targets": ["hold", "rootZone"] if may_hold else ["rootZone"]},
            ]},
        ],
    }


def evaluate(profile: dict, values: SettingsValues) -> dict:
    applied = values.model_dump()
    soil = applied["soilMoisture"]
    root_start = profile["factors"][3]["subfactors"][0]["start"]
    held_change = soil["irrigation"] == "hold" and soil["rootZone"] != root_start
    measured_root = root_start if soil["irrigation"] == "hold" else soil["rootZone"]
    wetness_delta = 30 if soil["irrigation"] == "overhead" else -20
    wetness = min(100, max(0, profile["leafWetnessStart"] + wetness_delta))

    factors = []
    for factor in profile["factors"]:
        factor_id = factor["id"]
        results = []
        for sub in factor["subfactors"]:
            id = sub["id"]
            value = applied[factor_id][id]
            if id == "irrigation":
                score = float(value in sub["targets"] and not held_change)
                feedback = ("Holding steady leaves root-zone moisture at its starting value." if held_change else
                            "Overhead watering adds water to the leaves." if value == "overhead" else
                            "The water delivery suits the case." if score else
                            "Adjust the root zone directly to reach the target without wetting the leaves.")
            else:
                observed = measured_root if id == "rootZone" else value
                score, feedback = numeric_score(sub, observed)
                if id == "targetCoverage" and not applied["spray"]["dose"] and value:
                    score, feedback = 0, "Coverage requires an applied training spray."
            results.append({"id": id, "value": value, "score": round(score, 3), "onTarget": score == 1, "feedback": feedback})
        factors.append({"id": factor_id, "score": round(sum(row["score"] for row in results) / len(results), 3), "subfactors": results})

    passed = all(row["onTarget"] for factor in factors for row in factor["subfactors"])
    replacement = profile["careTarget"] == "healthyReplacement"
    outcome = profile["outcome"] if passed or replacement else None
    return {
        "caseId": profile["caseId"], "profileVersion": profile["profileVersion"], "scenario": profile["scenario"],
        "score": round(sum(f["score"] for f in factors) / len(factors), 3), "passed": passed,
        "outcome": outcome, "outcomeLabel": OUTCOME_LABELS.get(outcome), "targetOutcome": profile["outcome"],
        "careTarget": profile["careTarget"], "rootZoneMoisture": measured_root, "leafWetness": wetness,
        "feedback": profile["outcomeFeedback"] if passed else "Review the settings marked outside the target.",
        "factors": factors,
    }


def numeric_score(sub: dict, value: float) -> tuple[float, str]:
    low, high = sub["targetMin"], sub["targetMax"]
    if low <= value <= high:
        return 1, f"{sub['label']} is on target for this scenario."
    if low == high == 0:
        message = f"This scenario calls for zero {sub['label'].lower()}."
    else:
        direction = "below" if value < low else "above"
        target = f"{low:g}" if low == high else f"{low:g}–{high:g}"
        message = f"{sub['label']} is {direction} this scenario's target of {target} {sub['unit']}."
    distance = max(low - value, value - high)
    span = low - sub["min"] if value < low else sub["max"] - high
    return 1 - distance / span, message
