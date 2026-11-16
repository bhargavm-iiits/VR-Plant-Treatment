# Treatment Lab: implementation notes

Implements `Docs/treatment.md` (master prompt) with content from `Docs/DataBase_Plan.md` and the six concept images in `Docs/VR_Treatment_Concepts`.

## What was built

| Area | Files |
|---|---|
| Content database (SQLite, read-only) | `Assets/_Project/Data/Treatment/treatment_content.sql`: canonical SQL source, imported directly as a SQLite asset (`SQLScriptDatabaseImporter`). 24 cases, 4 environment profiles, 15 tools. |
| Authoring aid | `Tools/treatment_content_gen.py` regenerates the SQL (`python Tools/treatment_content_gen.py`). The `.sql` file is the reviewed source. |
| Progress database (SQLite, writable) | `SqliteProgressRepository` writes to `Application.persistentDataPath/treatment_progress.db`. It is a separate file, so content updates never overwrite progress. |
| Lesson engine | `Runtime/Treatment/TreatmentSession.cs` (actions, prerequisites, routes, hygiene, disposal, outcomes) and `TreatmentCaseValidator.cs` |
| Unity binding | `TreatmentAssetCatalog` maps database asset keys to prefabs and materials only; it holds no advice. |
| Scene | `Assets/Scenes/Treatment.unity` (GUID kept), with `TreatmentController`, panels, case selector, tool rack and a SpawnAnchor |
| Integration | `EnvironmentId.Treatment`, explicit scene mapping, readiness wait before rig placement, save-on-exit, `EnterTreatment` / `ReturnToLab`, shared plant and disease selection (restored on launch), Lab "Enter Treatment Lab" button and status, Farm per-plant status labels, desktop `T` key |
| Editor commands | `BTP/Treatment/1 - Import Content Database`, `2 - Build Treatment Assets`, `3 - Build Treatment Scene`, `4 - Connect Bootstrap, Lab and Farm` |
| Tests | `Assets/_Project/Tests/EditMode/TreatmentTests.cs`: 12 tests against the real SQL. 66/66 EditMode tests pass. |

## Update: indoor treatment room, treatment on the leaf

- **Room:** every case now runs in an indoor treatment room with a tiled floor, walls with window bands, a ceiling, lights and a roof hatch. Each crop gets its own theme, with matching scenery outside the windows and props inside:
  - greenhouse crops for tomato, pepper and strawberry
  - fruit trees and crates for apple, cherry and peach
  - trellised vines for grape
  - corn and potato rows for corn and potato
- **Specimen:** the case's own 3D leaf from `Assets/Leaf_Assets` (via the Lab leaf models), enlarged to 0.7 m on an inspection holder in front of the viewer.
- **Leaf targets:** treatment targets are areas of that leaf. `LeafAnchors` places them by analysing each leaf's diseased texture in the editor:
  - lesion areas are the least green, healthy tissue the greenest
  - underside points sit behind the lesions
  - the petiole is the lowest point
  Cutting removes the lesion area and leaves a visible cut mark. Protection mists the healthy tissue. Pests sit on the underside.
- **Room controls:** growing conditions are corrected with the wall controls: humidity display, roof vent, circulation fan and drip valve.
- **Containment cases:** in virus, late blight and severe Esca cases the leaf is bagged and moved to the containment compartment, then clean stock is planted.
- **Follow-up:** a new healthy leaf "from the treated plant" appears on the comparison stand. Old lesions stay on the treated leaf.
- **Verified:** all 24 leaf lessons complete in Play mode, and all 66 EditMode tests pass. The previous whole-plant version of the content generator was saved outside the project. The content version is now 2, so older saved progress keeps only the step IDs that still exist.
- Captures: `Docs/Treatment_Captures/room_*.png`.

## How interaction works

**Default: buttons only.** The task panel (right of the specimen) shows the current task and one button per action
(`TreatmentButtonPlanner`): every option of a choice, every rack tool for a tool step ("Inspect with magnifier",
"Cut with pruning shears", "Apply spray (training)", ...) used on the next marked target, one button for a
gloved-hand step, and every room control for a control step. A press sends the same `ActionContext` to
`TreatmentSession.TryPerform` that a tool-on-target selection sends, so feedback, scoring, saving and recovery visuals
are identical. Accepted actions animate (`TreatmentActionAnimator`, about 1.7 s, input locked): the tool lifts off the
rack, flies to the target, plays its effect (magnifier hover, shears snip, spray mist, gloved press) and returns.
Refused actions shake the tool on the rack and show the lesson's feedback.

The direct alternative still works with the controller trigger, hand pinch or poke, the seated ray, and a desktop click:

1. Select a tool on the rack. Select the "Hand" slot (or the held tool again) to put it down.
2. Select a target. Cyan rings mark the targets that are valid for the current task.
3. Choices and the simulated "advance" are buttons on the task panel.

Wrong tools, wrong targets and premature actions are refused with case-specific feedback from the database. The scene and progress are not changed.

Removed material is carried until it is placed in Plant waste. Tools used for cuts become contaminated until they are cleaned at the Tool hygiene station.

## Verified in the Editor (desktop mode)

- All 24 cases load, validate and complete in Play mode. Every alternative route was also run: protect or monitor, and the spider mite predator or contact route.
- Outcomes:
  - Managed: 18 cases
  - Pest suppressed: spider mites
  - Replaced: both tomato viruses, tomato and potato late blight, and severe Esca
- Replacement lessons never "cure" the original. It is bagged and removed or contained, and a separate seedling is planted.
- Progress survives an Editor restart. Reset affects only the selected case.
- Lab → Treatment → Lab → Farm keeps the selected case, with exactly one camera, one AudioListener and one EventSystem.
- The Lab and the Farm show the saved result.
- Captures are in `Docs/Treatment_Captures/`.

## Not yet done (and not claimed)

- **Hardware and builds:** no Windows or Android ARM64 IL2CPP build has been made with SQLite yet, and there has been no Quest 2 / 72 FPS test. `Assets/_Project/link.xml` preserves the SQLite row types for IL2CPP.
- **Grip/trigger tool context:** tools are taken and used through selection (trigger, pinch, poke, click). Physically holding a tool on the grip and activating it with the trigger is not implemented.
- **Unused supplied models:** the shears have no articulated moving blades, and the 652k-triangle spray bottle and the water-pipe model are not used. Simple original primitive models stand in for the sprayer, drip emitter and similar props.
- **Simplified specimens:** the life-size Farm models are the visual base. Leaf and part targets are separate 3D leaf models (from the Lab leaf set) and simple props placed around the plant, not detached from the original mesh.
- **Concept captures:** the six concept-matching implementation captures were not produced as a set. The captures listed above cover inspection, completion, virus containment and replacement, and the orchard and vineyard stations.

## Asset licences

| Asset | Source | Author | Licence | Use and changes |
|---|---|---|---|---|
| Gardening Tools (`gardening_tools.glb`) | https://sketchfab.com/3d-models/gardening-tools-eacf394c1cbf496ca2c77373504ffaac | TeenWolf | CC BY 4.0 (attribution required) | Secateurs, rake and garden shovel meshes reused as rack tools, rescaled and recentred. The source file is unchanged. |
| work gloves (`work_gloves.glb`) | https://sketchfab.com/3d-models/work-gloves-991e775b6b5b4fdab682af56d08bb119 | jintrim3 | CC BY 4.0 (attribution required) | Rack display for the empty "Hand" slot, scaled to 0.18 m. Not used as a hand rig. |
| Pruning shears (`pruning_shears.glb`) | https://sketchfab.com/3d-models/pruning-shears-ffe8c4b4865247aa8a80027aabe3a2f8 | Flying Cat | Sketchfab Standard (not CC0) | Not used in the Treatment lab. |
| SQLite-net for Unity 1.3.3 (`com.gilzoide.sqlite-net`) | https://github.com/gilzoide/unity-sqlite-net | Gil Barbosa Reis | MIT (SQLite-net and SQLite3 Multiple Ciphers: MIT) | Package dependency, unmodified. |
| PlantVillage leaf images / leaf models | Existing project assets | Hughes, Salathé et al. | CC BY-SA 3.0 | Leaf targets reuse the Lab leaf models and textures. |

No assets were purchased or downloaded for this work.
## Update: new apple tree

- **Source:** the apple now uses the Blender tree in `Assets/Plant_Asstes/Apple_Tree/Apple_Tree.blend`. `Tools/blender_apple_tree.py` exports it for VR to `Assets/_Project/Models/AppleTree/`; the source `.blend` is not changed.
- **Leaves:** each of the three supplied diseased leaves (black rot, scab, cedar apple rust) is rendered once to a cut-out sprite. All 4,500 leaf instances become two-triangle cards in their exact positions.
- **Size:** two LODs, 50k and 23k triangles. The tree is scaled to 4 m (the Blender tree is 5.7 m) to fit the orchard row spacing.
- **Prefabs:** `BTP/Setup/Replace Apple With Blender Apple Tree` (`AppleTreeBuilder`, also run by "Build Plant Assets") rebuilds `Apple_Farm.prefab` and `Apple_Lab.prefab` in place. The Farm, visiting Lab and orchard treatment room all use the new tree with no scene edits.
- **Farm timeline:** leaf cards use the Farm Recovery shader with a generated healthy sprite (lesion pixels recoloured green), so the illustrative timeline still fades the apple's disease. Bark and apples use plain materials.

## Update: new peach tree and orchard spacing

- **Source:** the peach now uses `Assets/Plant_Asstes/Peach/Peach_Tree.blend`, exported by `Tools/blender_peach_tree.py` to `Assets/_Project/Models/PeachTree/`.
- **Contents:** 800 spotted-leaf cards, 80 simplified peaches and the simplified trunk. Two LODs (47k and 17k triangles), scaled to 3.2 m.
- **Builder:** `BlenderTreeBuilder` (menu: Replace Apple / Replace Peach) builds both trees the same way, including the healthy leaf state for the Farm timeline.
- **Spacing:** `FarmOrchardLayout` (menu: `BTP/Setup/Space Farm Orchard Trees`) re-spaces the Farm orchard:
  - The apple, peach and cherry specimens stand 6 m apart at x = -8 (z = -7, -1, +5).
  - The background trees continue that row from z = 11 and fill a second row at x = -10.8, staggered between the specimens.
  - Selection colliders and rings are refitted to the new tree sizes.
  - `FarmSceneBuilder` uses the same layout.

## Update: collision relaxation for the apple and peach trees

- **What it does:** `Tools/blender_tree_relax.py` runs a physics-style collision solver at export (both tree scripts call it). Branches are the fixed skeleton:
  - each fruit (body and stem) is a sphere, pushed away from other fruit and out of the branches
  - each leaf card is pushed out of the branches and fruit, and apart from overlapping leaves
  - moves are capped so leaves and fruit stay where they grew
- **Results (overlaps before → after):**

  | Overlap | Apple | Peach |
  |---|---|---|
  | Fruit in branches | 64 → 3 | 80 → 0 |
  | Leaves in branches | 173 → 1 | 57 → 1 |
  | Overlapping leaves | 1,126 → 4 | 80 → 0 |
  | Fruit touching fruit | 0 | 0 |

  The 3 apple fruit left are wedged at branch forks.
- **Why it isn't live physics:** real-time rigid bodies for about 5,300 leaves and 316 fruit would cost too much frame time for Quest at 72 FPS. Those parts don't move, so a one-time solve removes the overlaps at no runtime cost.

## Update: pergola vineyard (new grape vine)

- **Source:** the grape now uses `Assets/Plant_Asstes/Grape_Tree/Grape_Tree.blend` (scene "Grape Tree - Full 3D Plant"). `Tools/blender_grape_vine.py` exports it to `Assets/_Project/Models/GrapeVine/`, reshaped into a pergola (bower) vine after the reference photo:
  - a slim trunk to 1.9 m
  - canes laid flat in a 2.0-2.4 m canopy over the wires
  - the supplied bunches hanging below
- **Leaves:** 206 supplied leaves (black rot, esca, leaf blight sprites), each with 3 companions on its shoot, at a real 22 cm card size. That makes 824 leaves forming a ceiling.
- **Collision solve:** the shared solver keeps the canopy flat, separating leaves sideways rather than lifting them. Bunches move as rigid groups. Overlaps after the solve: fruit-fruit 0, fruit-wood 0, leaf-wood 4, leaf-leaf 2.
- **Size:** two LODs, 42k and 15k triangles.
- **Pergola module:** `Grape_Farm.prefab` is one 3.2 m module: galvanised corner posts, an overhead wire grid at 2.12 m, a mulched straw ridge and a drip line. Modules 3.2 m apart join into one canopy. `Grape_Lab.prefab` is the vine alone.
- **Farm vineyard:** `FarmVineyardLayout` (menu: `BTP/Setup/Build Farm Pergola Vineyard`) replaces the old single grape row, which crowded the corn, with a 2 × 3 pergola block behind the corn (x 5.7-12.1, z 14.8-24.4), with an aisle under the canopy. The grape specimen keeps its place as a single aligned module. `FarmSceneBuilder` uses the same layout.
- **Vineyard treatment room:** looks out on pergola rows.
