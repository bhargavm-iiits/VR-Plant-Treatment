from fastapi import APIRouter, HTTPException, Query

from app.treatment import Case, Evaluation, build_profile, evaluate, load_catalog, summary

router = APIRouter(prefix="/api/treatment/cases", tags=["treatment"])


def case_for(plant_id: str, disease_id: str) -> Case:
    _, cases, _, _ = load_catalog()
    case = cases.get((plant_id, disease_id))
    if case is None:
        raise HTTPException(404, "No such treatment case.")
    return case


def profile_for(case: Case, scenario: str) -> dict:
    if scenario != "standard" and scenario not in case.scenarios:
        raise HTTPException(404, "No such scenario for this treatment case.")
    version, _, crops, _ = load_catalog()
    return build_profile(version, crops[case.plantId], case, scenario)


@router.get("")
def list_cases(plant_id: str | None = None) -> list[dict]:
    """Browse the full Treatment scene catalog by its Unity case IDs."""
    _, cases, _, _ = load_catalog()
    return [summary(case) for case in cases.values() if plant_id is None or case.plantId == plant_id]


@router.get("/{plant_id}/{disease_id}")
def get_case(plant_id: str, disease_id: str, scenario: str = Query("standard")) -> dict:
    """Load all four factors, starting values, and case targets in scene order."""
    return profile_for(case_for(plant_id, disease_id), scenario)


@router.post("/{plant_id}/{disease_id}/evaluate")
def check_health(plant_id: str, disease_id: str, assessment: Evaluation, scenario: str = Query("standard")) -> dict:
    """Evaluate the applied settings for one lesson without changing learner progress."""
    profile = profile_for(case_for(plant_id, disease_id), scenario)
    if assessment.profileVersion != profile["profileVersion"]:
        raise HTTPException(409, "The treatment profile changed. Reload the case before checking health.")
    return evaluate(profile, assessment.values)
