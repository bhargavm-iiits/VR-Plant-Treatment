# Four-control treatment API

The treatment catalog contains the 58 diseased leaf cases from Unity's `treatment_content.sql`. The backend stores the scenario settings in `Backend/data/treatment_profiles.json`. The 48 cases recognized by the scan model also have an exact `scanDiseaseId`. The remaining 10 use their Unity case IDs.

| Request | Use |
|---|---|
| `GET /api/treatment/cases` | List all cases, outcomes, and available scenarios. An optional `plant_id` filters by Unity plant ID. |
| `GET /api/treatment/cases/{plantId}/{diseaseId}` | Load starting values, control ranges, target bands, and feedback context. |
| `GET /api/diseases/{scanDiseaseId}/treatment` | Find a treatment case using the disease ID returned by a scan. URL-encode the ID. |
| `POST /api/treatment/cases/{plantId}/{diseaseId}/evaluate` | Evaluate the settings applied by the player. Use the same `scenario` and `profileVersion` as the profile. |

`scenario` defaults to `standard`. Use `?scenario=severe` on both the profile and evaluation requests for the severe rice brown spot lesson. An unknown case or scenario returns `404`, a stale version returns `409`, and invalid or missing settings return `422`.

| Order | Control | Subfactors |
|---|---|---|
| 1 | Temperature | Day temperature and night temperature |
| 2 | Fertiliser | Supplemental amount and number of applications |
| 3 | Spray | Training spray amount and percentage covered on the marked target |
| 4 | Soil Moisture | Root-zone moisture and water delivery |

Render the `factors` and `subfactors` arrays in order. **Apply** captures the current values. **Check Health** sends those values to `/evaluate`. Coverage represents only the marked target, so spray placed elsewhere earns no coverage credit. `rootZone` water delivery represents drip or drainage in the greenhouse and simulated root-zone water management in the rice lesson. Leaf wetness is a separate observation in the assessment.

This request earns full credit for the standard `corn/common_rust` lesson.

```json
{
  "profileVersion": 1,
  "values": {
    "temperature": { "day": 25, "night": 17 },
    "fertiliser": { "dose": 0, "applications": 0 },
    "spray": { "dose": 0, "targetCoverage": 0 },
    "soilMoisture": { "rootZone": 59, "irrigation": "hold" }
  }
}
```

The server scores each subfactor against its target. `passed` requires all eight values to satisfy the lesson. Scores are a simulation measure and the doses use training units. `Managed` and `PestSuppressed` mark completed lessons. In `Contained` lessons, the scenario begins after containment and replacement, and `passed` reports care of the healthy replacement. The assessment is stateless. The paired XR recipe and progress routes use their existing contract.
