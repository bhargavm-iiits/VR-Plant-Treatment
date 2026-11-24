# CropScan XR repository audit

Reviewed for Bhargav M’s Third BTP evaluation on 24 November 2026. The audit uses the current working tree, including local changes, and dated build/test records. The repository's earlier presentation draft describes a smaller Farm-and-Lab prototype and predates the Treatment scene and the current web classifier.

Bhargav M's individual contribution is the Unity VR experience and its Quest demonstration. A teammate developed the backend. The classifier measurements below are team project results.

## How the pieces fit

| Area | Where to look | What the folder shows |
|---|---|---|
| Unity runtime | [Build settings](../ProjectSettings/EditorBuildSettings.asset), [Core](../Assets/_Project/Scripts/Runtime/Core), [Farm](../Assets/_Project/Scripts/Runtime/Farm), [Lab](../Assets/_Project/Scripts/Runtime/Lab) | Four enabled scenes. A persistent XR rig loads the Farm, Lab and Treatment environments. Shared selection drives specimen focus, leaf display and recovery previews. |
| Plants and assets | [Plant catalog](../Assets/_Project/Data/PlantCatalog.asset), [prefabs](../Assets/_Project/Prefabs/Plants), [art tooling](../Tools), [Unity captures](../Docs/Screenshots) | The serialized plant catalog has nine primary entries and ten in its second collection. Plant scenes use imported models, disease and healthy textures, and lighter representations for distance. Review the actual scene and prefab evidence when describing placement or coverage. |
| Treatment | [Treatment scripts](../Assets/_Project/Scripts/Runtime/Treatment), [authored SQLite source](../Assets/_Project/Data/Treatment/treatment_content.sql), [API contract](../Backend/docs/treatment_api.md) | Local SQL contains 58 cases across 19 crops. The chamber captures four simulated settings and assesses an applied scenario. Local progress and connected assessment have separate storage paths. |
| Web and inference | [Frontend source](../Backend/frontend/src), [Scan routes](../Backend/services/api/app/routers/scans.py), [inference](../Backend/services/api/app/inference.py), [model metadata](../Backend/services/api/model/meta.json) | The React UI submits a leaf image to FastAPI. The ONNX runtime returns logits and class activation maps. The service can abstain using non-leaf, image detail, confidence and entropy checks. The v6 artifact has 69 outputs across 20 crops including one non-leaf class. |
| Scan-to-specimen integration | [Case map](../Backend/data/xr_case_map.json), [Unity XR coordinator](../Assets/_Project/Scripts/Runtime/XR/XrSessionCoordinator.cs), [XR routes](../Backend/services/api/app/routers/xr.py), [Windows launcher](../Tools/CropScanLauncher) | The connected map and recipe catalog contain 24 entries each. Pairing, launch and assessment use scan and session identifiers. A session pins a recipe version. Assessment writes handle retries and revision conflicts. |
| Tests and packaging | [Unity EditMode tests](../Assets/_Project/Tests/EditMode), [API tests](../Backend/services/api/tests), [Quest build records](../Builds/Quest), [Unity test records](../Tools/logs) | Unity recorded a targeted 167/167 pass in the grip and leaf run. A Quest Android build log reports success. The launcher executable in the tools folder is a separate component from the Unity Windows player. |

## Benchmark interpretation

The [baseline experiment summary](../Backend/experiments/results/summary.md) reports 97.07% on its PlantVillage lab split and 14.83% on its PlantDoc field split. The [intermediate B0 metrics](../Backend/experiments/results/deploy5-field_metrics.json) report 97.85% lab and 58.90% field. The [BioCLIP v6 metadata](../Backend/services/api/model/meta.json) reports 99.35% lab and 77.12% field with field macro-F1 of 0.7695. The lab and field benchmarks contain 4,333 and 236 images, respectively, in the project records.

The 62.29 percentage-point PlantDoc gain compares successive models and training setups. A separate EfficientNet-B0 research ablation helps explain the training data effect, with 20.76% for the lab-only run and 71.61% for the mixed-data run with field augmentation. Earlier saved error analysis records confusion between visually similar diseases and label inconsistencies. Treat those as examples from the earlier experiment. The package contains no independent new-farm validation or v6 per-class error audit that reproduces those particular examples.

## Verification boundaries

- The 167 Unity results are from a filtered EditMode run. The recorded backend treatment API run reported 160 passed with 7 deselected. Neither count represents a new full-suite run made for this evaluation package.
- A fresh frontend unit run reported 34 passes and 4 failures. Telugu, Hindi, Tamil and Kannada lack the English key for the Quest installation label.
- The presenter confirmed a successful Quest demonstration of the workflow, including treatment and weekly recovery. An earlier Quest capture records an API configuration issue. A separate headset recording shows the Treatment Lab interaction. Repeatable device timing and comfort measurements remain future work.
- The simulated recovery timeline and assessment represent authored training states. The project has yet to measure learner improvement or real-world treatment outcomes.
- The report follows the current working copy and the local model records. The public GitHub project is the source-code link for the portal. Full data revisions and test logs are identified above where they exist locally.
