# Master Prompt: Database-Driven VR Treatment Lab

**Target document:** `D:\Unity Projects\Plant Disease\BTP_Project\Docs\treatment.md`

**Status:** Master implementation prompt. Creating this document does not implement the database, treatment scene, or changes to existing Unity scenes and scripts.

## 1. Objective and fixed requirements

Work in the existing Unity project at `D:\Unity Projects\Plant Disease\BTP_Project`.

Implement a complete treatment environment in [Treatment.unity](<D:/Unity Projects/Plant Disease/BTP_Project/Assets/Scenes/Treatment.unity>), following the six images in `D:\Unity Projects\Plant Disease\BTP_Project\Docs\VR_Treatment_Concepts`.

Use [DataBase_Plan.md](<D:/Unity Projects/Plant Disease/BTP_Project/Docs/DataBase_Plan.md>) for the disease-management content and realistic outcomes. Preserve the existing nine plant IDs and 24 plant-disease pairs.

The implementation must provide:

- An **Enter Treatment Lab** button in the existing visiting Lab.
- A **Return to Visiting Lab** button in Treatment.
- Plant and disease selection in both labs.
- Crop-specific environments inside the single Treatment scene.
- Life-size orchard and vineyard specimens with accessible treatment targets.
- Guided hands-on tasks, with invalid actions blocked and explained.
- Local progress saving across application restarts.
- Saved treatment results reflected in Treatment, Farm, and visiting Lab.
- Controller, hand-tracking, seated/ray, and desktop interaction paths.
- Free assets and dependencies only.

### Mandatory database rule

**Every treatment case must load its instructions, permitted methods, prerequisites, completion rules, monitoring, outcome, and environment configuration from SQLite.**

Use a Unity catalog to resolve database asset keys into prefabs, materials, textures, and other Unity references. The catalog must not contain a second independently maintained copy of treatment advice.

Do not implement treatment lessons through hardcoded disease-specific instructions, disease-name switches, or the existing shared recovery table.

If database content is missing or invalid:

- Show a clear unavailable-data message.
- Disable starting that lesson.
- Keep navigation and retry available.
- Never substitute another disease or a generic treatment.

## 2. Existing assets and preparation

### Findings from the supplied folder

Asset source folder: `D:\Unity Projects\Plant Disease\BTP_Project\Assets\Lab_Treatment_Assets`.

| Asset | Inspection result | Implementation direction |
|---|---|---|
| `gardening_tools.glb` | 23 meshes; approximately 17,224 triangles total; no animations. Embedded metadata identifies CC BY 4.0. | Separate useful tools into individual prefabs with appropriate scale, grips, and colliders. Preserve attribution. |
| `pruning_shears.glb` | Approximately 35,216 triangles; five meshes/materials; no animations. Metadata identifies Sketchfab Standard licensing. | Reuse the supplied asset, optimise its derivative, and provide a working cutting interaction. Do not describe its licence as CC0. |
| `work_gloves.glb` | Approximately 14,416 triangles; no skin or animation; three embedded 4096x4096 textures. | Optimise for preparation/display props. Do not attach this rigid model as though it were an articulated tracked-hand rig. |
| Water-pipe asset | FBX and PBR textures; several maps have 2K resolution. | Prepare URP materials and appropriate equipment prefabs. Generate flexible emitter geometry where the source cannot provide it. |
| Spray-bottle archive | Nested ZIP contains Blender, FBX, and GLB versions. GLB has approximately 652,158 triangles and eight materials. | Produce a substantially simplified derivative before putting it in a Quest scene. Preserve the original source. |

Create optimised derivatives rather than overwriting supplied models. Use simple colliders and sensible interaction layers. Reduce texture sizes according to viewing distance and measured headset performance.

For the shears, inspect whether the geometry supports separate moving halves. If necessary, create an original articulated derivative with two hinged parts; the visible blades must respond to activation.

### Missing props

Provide the following functional props:

- Magnifier and enlarged-leaf inspection holder.
- Tool rack and sanitation station.
- Disposal container and containment bags.
- Drip emitter, irrigation controls, and plant supports.
- Operable ventilation equipment.
- Insect mesh, whitefly-monitoring card, and predator-release container.
- Clean replacement plants and planting tools.
- Fruit-mummy, trunk, and tuber teaching models.

Reuse existing assets first. Build straightforward original geometry for simple props. Download additional assets only when a meaningful gap remains.

Verified free sources include [Kenney Furniture Kit](https://kenney.nl/assets/furniture-kit) and [Poly Haven's CC0 models](https://polyhaven.com/license), including its [Ceiling Fan](https://polyhaven.com/a/ceiling_fan).

For each downloaded asset, record its original page, author, licence, downloaded filename, and modifications. Preserve supplied attribution requirements. Do not purchase assets, use paid generation credits, or assume that an asset is reusable merely because it can be downloaded.

## 3. Implementation specification

### A. Database and Unity asset catalog

Use the free `com.gilzoide.sqlite-net` package, pinned to version **1.3.3**. Its published package supports Windows and Android and provides read-only SQLite assets for Unity. Verify the native plugin in an Android ARM64 IL2CPP build before expanding the implementation. [Package documentation](https://github.com/gilzoide/unity-sqlite-net/tree/1.3.3)

Create:

1. A versioned SQL authoring source containing the reviewed treatment content.
2. A generated, bundled SQLite content database containing all 24 cases.
3. A `TreatmentAssetCatalog` ScriptableObject that maps asset keys to Unity references.
4. A separate writable SQLite database for progress under `Application.persistentDataPath`.

Use the package's SQLite asset support for the bundled content. Open an actual database connection and execute parameterised queries when selecting a case. A validated in-memory snapshot returned by those queries may drive the current lesson.

The content database must represent:

| Record group | Required information |
|---|---|
| Cases | Composite plant/disease key, display name, problem category, teaching scenario, outcome, content version. |
| Methods | Purpose, limitations, compatible alternatives, and conditions under which each method is appropriate. |
| Steps | Stable ID, instructions, action type, tool key, target tags, prerequisites, completion configuration, and corrective feedback. |
| Environment profiles | Crop-setting key, required modules, specimen configuration, controls, and enabled equipment. |
| Follow-up | Observation checkpoints, expected findings, escalation, and completion criteria. |
| Evidence | Publisher, source URL, review information, and regional limitations. |

Author all treatment text and rules in the database. Code should implement reusable capabilities such as inspection, cutting, disposal, irrigation, and monitoring.

Provide these interfaces:

- `ITreatmentRepository.InitializeAsync()`
- `ITreatmentRepository.GetCase(CaseKey)`
- `TreatmentAssetCatalog.Resolve(assetKey)`
- `TreatmentSession.TryPerform(ActionContext)`
- `IProgressRepository.Load(CaseKey)`
- `IProgressRepository.Save(CaseProgress)`

`CaseKey` must contain both plant ID and disease ID. Never identify a case using the disease ID alone.

Validate database completeness, foreign references, asset keys, prerequisites, and available action handlers before enabling lessons. Preserve types required by SQLite mapping under IL2CPP stripping.

### B. Scene navigation and shared selection

Extend the existing environment system with a third environment: `Treatment`. Preserve existing serialized enum values by appending the new value.

Replace the current two-environment scene-name expression with an explicit mapping for Farm, Lab, and Treatment.

Update:

- Environment entry/return actions.
- Bootstrap scene wiring.
- Build-scene registration.
- Direct Play-mode startup support for Treatment.
- Scene-ready handling and spawn placement.

The current scene-builder registration includes only XRBootstrap, Farm, and Lab; Treatment must be included whenever that registration is regenerated.

Maintain **one persistent XR Origin, headset camera, AudioListener, EventSystem, and interaction manager**. Remove the placeholder Treatment camera from runtime use.

Add persistent plant-disease selection. Entering Treatment must use the case currently selected in the visiting Lab. Changing a case in either lab must update the shared selection.

For a fresh session without a selection, use the database-defined Tomato/Early Blight default. Restore the last selected case on later application launches.

During transitions:

1. Save the current lesson.
2. End tool activation and release or park held tools.
3. Fade out.
4. Load and configure the destination.
5. Wait until its database-backed case and environment are ready.
6. Position the existing XR rig.
7. Fade in.

Keep one scene-level `SpawnAnchor` in Treatment. Copy the selected environment prefab's spawn pose into it before rig placement.

Repeated navigation presses must not create concurrent transitions. A loading or configuration failure must leave the user with visible feedback and a working return path.

### C. Crop-specific environments

Use one Treatment scene with a shared controller and interchangeable environment prefabs. Instantiate only the selected setting and its required disease modules.

| Environment | Plants | Treatment arrangement |
|---|---|---|
| Greenhouse workstation | Tomato, Bell Pepper, Strawberry | Accessible beds or pedestals, inspection holder, tool bench, irrigation, ventilation. |
| Orchard station | Apple, Cherry, Peach | Life-size trees, reachable branches and fruit, safe paths, sanitation and collection points. |
| Vineyard station | Grape | Life-size trellis, accessible canes/clusters, pruning and trunk-inspection equipment. |
| Field-bed station | Corn, Potato | Crop rows, observation positions, irrigation, residue-management and rotation teaching controls. |

Keep the reference images' visual identity: natural daylight, believable plants, white/grey structures, cyan selection feedback, and charcoal world-space panels.

Database configuration must also enable disease-specific modules, including:

- Humidity and ventilation.
- Fallen leaves and fruit mummies.
- Alternate-host inspection for cedar apple rust.
- Pest inspection and biological control.
- Whitefly exclusion and containment.
- Trunk and tuber cutaways.
- Removal and replacement.
- Seasonal renovation and rotation planning.

A selected disease must change the actual specimen, targets, available tools, controls, instructions, and outcome. Changing only the heading is insufficient.

Large plants must remain life-size. Use reachable branches, appropriate viewpoints, ray assistance, and inspection cutaways. Use assisted removal/planting sequences for large specimens rather than requiring learners to lift entire trees.

### D. Plant models and treatment interactions

Retain the existing detached-leaf viewer as a reference. Create treatment-specific whole-plant derivatives with stable, individually identifiable treatment targets.

Where source plants are combined meshes, author detachable leaves, shoots, fruit, and teaching sections. A completed removal must visibly remove the relevant part; disabling a collider alone is insufficient.

Implement these reusable interactions:

| Capability | Required behaviour |
|---|---|
| Inspection | Aim or position the magnifier at valid targets, inspect relevant surfaces, and record observations. |
| Pruning | Require the correct tool and marked cut location; preserve healthy foliage and the main stem. |
| Disposal | Require removed material to enter the correct container. |
| Hygiene | Track relevant contamination and require the database-prescribed cleaning step. |
| Irrigation | Position equipment at the appropriate target and operate the required controls. |
| Ventilation | Operate vents/fans and update the scenario's environmental condition. |
| Protective treatment | Validate method, preparation, permitted targets, and simulated coverage. |
| Biological control | Identify the pest and use the appropriate release method and compatible route. |
| Containment/replacement | Contain the original specimen, complete source/vector management, and establish a distinct replacement. |
| Monitoring | Inspect required sites and record the follow-up findings before completion. |

Use controller **grip to hold tools** and **trigger to activate them** in Treatment. The current project remaps selection to trigger for visiting interactions, so introduce a Treatment input context and restore the visiting context when leaving.

Support hand pinch/poke and a clear alternative for tool activation. Provide seated/ray and desktop equivalents that execute the same validated actions.

Invalid actions must be blocked with specific guidance. They must not damage the plant or advance progress.

Keep physical interaction tolerances and simulation thresholds in the lesson configuration. Treat coverage and interaction feedback as game mechanics, not measured biological effectiveness.

### E. Disease-specific lesson coverage

Seed all 24 cases from the existing database plan, retaining the distinctions below:

| Plant and case | Required lesson distinction |
|---|---|
| Apple - Scab | Fallen-leaf sanitation, infection-risk timing, protection of susceptible growth. |
| Apple - Black rot | Fruit mummies, affected wood, sanitation, fruit protection. |
| Apple - Cedar apple rust | Alternate-host context and seasonal prevention; existing spots remain. |
| Bell Pepper - Bacterial spot | Clean stock, hygiene, dry handling, limited chemical options. |
| Cherry - Powdery mildew | Mildew-specific inspection and compatible protection. |
| Corn - Gray leaf spot | Crop-stage assessment and protection of functional foliage. |
| Corn - Common rust | Default mild case supports monitoring without mandatory spraying. |
| Corn - Northern leaf blight | Upper-canopy protection, resistance, rotation planning. |
| Grape - Black rot | Mummy removal, canopy management, protection of susceptible tissue. |
| Grape - Esca | Trunk inspection; default severe case ends in replacement. |
| Grape - Isariopsis leaf spot | Susceptible-host scenario and protection against premature foliage loss. |
| Peach - Bacterial spot | Cultural care, resistance, treatment limitations, permanent scars. |
| Potato - Early blight | Water/nutrition management, rotation, protection without stripping the canopy. |
| Potato - Late blight | Confirmation, tuber/source containment, healthy-host protection, later clean planting. |
| Strawberry - Leaf scorch | Perennial-bed scenario with correctly timed post-harvest renovation. |
| Tomato - Bacterial spot | Hygiene, dry handling, protection limitations, selective removal. |
| Tomato - Early blight | Inspection, limited pruning, disposal, hygiene, mulch/support, monitoring. |
| Tomato - Late blight | Prompt containment and management of threatened neighbouring hosts. |
| Tomato - Leaf mold | Leaf-underside inspection, irrigation, humidity and ventilation controls. |
| Tomato - Septoria | Lower-leaf inspection, limited removal, splash reduction and monitoring. |
| Tomato - Spider mites | Confirm live pests; offer compatible biological/contact-management routes. |
| Tomato - Target spot | Inner-canopy inspection and disease-specific protection/resistance management. |
| Tomato - Yellow leaf curl virus | Whitefly management, containment, removal and healthy replacement. |
| Tomato - Mosaic virus | Confirmed tobamovirus case, appropriate sanitation and replacement. |

Do not require every method listed in a record. Encode justified alternatives and any seasonal checkpoints explicitly.

The six concept images represent several related teaching cases. Do not combine their numbered captions into one universal six-step treatment prescription.

### F. Progress, outcomes, and cross-scene consistency

Save stable state after every completed action and before switching cases or environments.

Progress must include:

- Plant/disease key and content version.
- Completed step IDs and selected method route.
- Removed target IDs and relevant equipment states.
- Follow-up observations.
- Outcome and replacement state.

Restore the visual state from these records; do not save scene-object instance references. Save related changes atomically.

Reset must affect only the selected case. Database content updates must not overwrite the progress database.

Use realistic outcomes:

- **Managed:** protect functional foliage and show appropriate healthy new growth.
- **Pest suppressed:** reduce active pest signs and subsequent damage.
- **Replaced:** retain the distinction between the infected original and the new specimen.

Preserve old necrosis, scars, and damaged fruit. Do not use the shared six-week table to erase disease or generate invented chlorophyll measurements.

Reflect the selected case's saved result in:

- The Treatment specimen.
- The labelled Farm specimen and its status.
- The visiting Lab's treatment-status/reference presentation.

Decorative Farm rows need not become independently tracked patients. Avoid shared-material changes that accidentally alter every plant of the same species.

Keep the old illustrative timeline separate from treatment state. It must not overwrite saved outcomes, mark tasks complete, or visually "cure" a virus-infected patient.

## 4. Implementation sequence

1. **Prove SQLite first.** Open the bundled content database, query a real case, and save/load progress on Windows and Android ARM64 IL2CPP.
2. **Prepare supplied assets.** Produce optimised tool derivatives, grips, colliders, materials, and required moving parts.
3. **Build scene navigation.** Integrate Treatment with the persistent XRBootstrap and both navigation directions.
4. **Complete one vertical slice.** Implement Tomato/Early Blight through inspection, treatment actions, follow-up, persistence, and cross-scene result display.
5. **Build the four environment profiles.** Verify a greenhouse crop, orchard tree, grapevine, and field crop at their intended scales.
6. **Add the specialist modules.** Cover humidity, biological control, trunk/tuber inspection, seasonal tasks, and virus containment/replacement.
7. **Complete all 24 database records and lessons.**
8. **Validate performance, input paths, content accuracy, and visual quality.**

Provide repeatable editor setup/build commands. Generated-scene tooling must preserve the Treatment scene's existing GUID and unrelated project work.

## 5. Acceptance and handover

The work is complete only when:

- The existing Lab button opens the selected case in Treatment.
- Treatment returns to the visiting Lab without losing selection or progress.
- Every canonical plant-disease pair loads its own SQLite record.
- Editing a database instruction or supported rule changes the lesson without editing disease-specific C#.
- Removing a required database row or asset binding blocks that lesson with visible feedback.
- Environment, specimen, targets, and tools change appropriately across cases.
- Physical actions validate their targets and prerequisites.
- Alternate valid methods work; unnecessary spraying is never required.
- Wrong actions are explained and blocked.
- Progress survives case changes, scene changes, and application restart.
- Reset affects only the selected case.
- Farm and visiting Lab show the saved selected-case result.
- Legacy timeline controls cannot change treatment progress.
- Virus and severe-Esca cases cannot finish by greening the infected original.
- Corn lessons do not reward foliage stripping.
- Hand, controller, seated/ray, and desktop paths are usable.
- Direct Play in Treatment starts through the existing bootstrap pattern.
- Only one persistent XR rig and UI/input system exist.
- Windows and Android ARM64 IL2CPP builds succeed with functioning SQLite.
- Quest 2 reaches the existing **72 FPS target** under representative treatment workloads.
- All new downloaded assets are free and have recorded licences.
- Six actual implementation captures demonstrate inspection, pruning/sanitation, environment controls, protection, follow-up, and containment/replacement.

Deliver the scene, populated database and its authoring source, Unity asset catalog, optimised prefabs, runtime/editor integration, meaningful tests, licence records, and implementation screenshots. Report any hardware testing that could not be performed instead of claiming it passed.
