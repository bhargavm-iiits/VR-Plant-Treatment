"""Build data/xr_recipes.json, the VR treatment recipes, from the Unity project's treatment content.

The Unity app ships its lessons as Assets/_Project/Data/Treatment/treatment_content.sql. For every case in
data/xr_case_map.json this exports the structure the server validates a treatment result against (control ranges,
recommended bands, weights, rules, starting state) and the asset keys the build needs -- the same fields as Unity's
XrRecipeAdapter.FromCase, so a recipe the server pins is one the headset accepts. Learner-facing text stays in Unity.

    python tools/build_xr_recipes.py "<Unity project>/Assets/_Project/Data/Treatment/treatment_content.sql"

A changed recipe needs a new RECIPE_VERSION, so sessions already pinned to the old version keep it.
"""

from __future__ import annotations

import json
import sqlite3
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
MAP = REPO / "data" / "xr_case_map.json"
OUT = REPO / "data" / "xr_recipes.json"
RECIPE_VERSION = 1


def main(sql_path: str) -> int:
    db = sqlite3.connect(":memory:")
    db.executescript(Path(sql_path).read_text(encoding="utf-8"))
    case_map = json.loads(MAP.read_text(encoding="utf-8"))
    recipes = []
    for row in case_map["rows"]:
        p, d = row["plantId"], row["diseaseId"]
        case = db.execute("select content_version, specimen_asset from cases where plant_id=? and disease_id=?", (p, d)).fetchone()
        profile = db.execute(
            "select initial_severity, initial_health, max_severity_drop, max_health_gain, max_worsen "
            "from case_profile where plant_id=? and disease_id=?", (p, d)).fetchone()
        if case is None or profile is None:
            print(f"error: no case or manual profile for {p}/{d}", file=sys.stderr)
            return 1
        controls = [
            {"controlId": c[0], "order": c[1], "min": c[2], "max": c[3], "step": c[4], "start": c[5],
             "recommendedMin": c[6], "recommendedMax": c[7], "weight": c[8], "required": bool(c[9])}
            for c in db.execute(
                "select control_id, ord, min_value, max_value, step_value, start_value, rec_min, rec_max, weight, required "
                "from treatment_controls where plant_id=? and disease_id=? order by ord", (p, d))
        ]
        rules = [
            {"ruleId": r[0], "controlA": r[1], "aMin": r[2], "aMax": r[3], "controlB": r[4] or "",
             "bMin": r[5] or 0.0, "bMax": r[6] or 0.0, "penalty": r[7], "blocks": bool(r[8])}
            for r in db.execute(
                "select rule_id, control_a, a_min, a_max, control_b, b_min, b_max, penalty, blocks "
                "from control_rules where plant_id=? and disease_id=? order by rowid", (p, d))
        ]
        assets = {case[1]}
        for asset, material in db.execute("select asset_key, material_key from case_targets where plant_id=? and disease_id=?", (p, d)):
            assets.add(asset)
            if material:
                assets.add(material)
        recipes.append({
            "recipeId": row["recipeId"], "recipeVersion": RECIPE_VERSION, "plantId": p, "diseaseId": d,
            "contentVersion": case[0], "source": "backend",
            "initialSeverity": profile[0], "initialHealth": profile[1], "maxSeverityDrop": profile[2],
            "maxHealthGain": profile[3], "maxWorsen": profile[4],
            "controls": controls, "rules": rules, "assetKeys": sorted(a for a in assets if a),
        })

    OUT.write_text(json.dumps({"mapVersion": case_map["mapVersion"], "recipes": recipes}, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {len(recipes)} recipe(s) to {OUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
