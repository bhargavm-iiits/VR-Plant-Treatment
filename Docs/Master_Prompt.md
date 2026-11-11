# Master Prompt: BTP Plant Disease VR Experience

This is the implementation specification for the BTP plant disease VR project (Unity 6000.5.5f1, URP). It fixes scope, assets, scene flow, interaction rules, demo data, build targets, and acceptance checks, so a Unity developer can build it without choosing scope or inventing missing assets.

---

## 1. Reference images

| Image | Role |
|---|---|
| `References/plant_types.png` | **Authoritative plant list** (requested species) |
| `References/farming_land.png` | **Visual reference for the Farm** (starting environment) |
| `References/lab.png` | **Visual reference for the Lab** (second environment and interface) |

### Plant list (authoritative)
Apple, Bell Pepper, Cherry, Corn (Maize), Grape, Peach, Potato, Strawberry, Tomato.

![Plant types reference](References/plant_types.png)

### Farm (starting environment)
Recreate its sunny field, raised soil rows, central walking path, timber fence, trees, distant mountains, water tower, notice board, and visible signs of unhealthy foliage.

![Farm environment reference](References/farming_land.png)

### Lab (second environment and interface)
Translate its central plant pedestal, diseased/healthy comparison, dark translucent panels, cyan highlights, week timeline, and health metrics into **spatial VR controls**. Chili, Cucumber, the treatment text, and the percentages in this image are **examples only**. They are not extra plant or scientific data requirements.

![Lab interface reference](References/lab.png)

---

## 2. Build targets

| Target | Profile | Settings |
|---|---|---|
| Meta Quest 2 / Quest 3 (standalone) | Meta Quest build profile (Android) | OpenXR, Vulkan only, IL2CPP, ARM64, Linear color, Single Pass Instanced, min API level required by the Meta Quest build profile |
| Windows PC VR (Link / SteamVR / any OpenXR runtime) | Windows build profile | OpenXR, x86_64, DX11 |

- Quality levels: **Quest2** (baseline: `Mobile_RPAsset`), **Quest3** (higher render scale and texture/LOD budget), **PC** (`PC_RPAsset`).
- **Performance target: stable 72 FPS on Quest 2 in both environments.** Quest 2 is the profiling baseline for foliage density, texture sizes, shadows, draw calls, and memory.

## 3. Packages

`com.unity.xr.management`, `com.unity.xr.openxr`, `com.unity.xr.interaction.toolkit` (3.x, with the Starter Assets and Hands Interaction Demo samples), `com.unity.xr.hands`, `com.unity.cloud.gltfast`. `com.unity.pipeline` is used only as an editor automation tool.

---

## 4. Scene flow

```
XRBootstrap (persistent)  ──loads──▶  Farm (default)
                                       │  "Enter Lab"
                                       ▼
                                      Lab
                                       │  "Return to Farm"
                                       ▼
                                      Farm   (plant selection kept)
```

| Scene | Contents |
|---|---|
| `Assets/Scenes/XRBootstrap.unity` | **First in Build Settings.** The only XR Origin, input action manager, XR input modality manager, EventSystem with XR UI input module, fade overlay, `SelectionState`, `EnvironmentController`. Loads the Farm on start. |
| `Assets/Scenes/Envirornment.unity` | **The Farm** (default environment), loaded additively. Has a `SpawnAnchor`. This is the project's renamed template scene; pressing Play in it loads XRBootstrap automatically. |
| `Assets/Scenes/Lab.unity` | Lab environment and UI, loaded additively. Has a `SpawnAnchor`. |

### State and transitions
- **Plant catalog**: exactly **nine** entries. Each has a stable plant ID, display name, farm prefab, lab prefab, info text, and and that it has the illustrative recovery demo (all nine plants).
- `SelectPlant(plantId)` updates the shared selection, and both scenes' UI respond to the change.
- `SwitchEnvironment(Farm|Lab)`: fade out → unload the old environment → load the target → place the XR Origin at the target's spawn anchor → fade in. Ignore repeated presses while a switch is running.
- Keep **one XR Origin** for the whole session. There must never be duplicate cameras, audio listeners, EventSystems, or input modules.
- If the user enters the Lab without selecting a plant, **Tomato** is selected.
- Returning to the Farm keeps the selection. **Reset** resets only the Tomato demo week.
- A missing model reference shows a **visible setup error** placeholder (in the Editor or development builds). It never silently shows the wrong species.

---

## 5. Asset map

All sources are under `Assets/Plant_Asstes/` (the exact folder name, typo included).

| Plant ID | Display name | Source model | Notes |
|---|---|---|---|
| `apple` | Apple | `Apple_new/Meshy_AI_apple_tree_3d_0923203849_image-to-3d-texture_fbx/` | Very heavy Meshy export (≈356 MB). Decimate. Orchard edge. |
| `bell_pepper` | Bell Pepper | `Bell Pepper/Meshy_AI_Colorful_Bell_Pepper__0923184552_texture_fbx/` | Heavy Meshy export (≈187 MB). Decimate. |
| `cherry` | Cherry | `Cherry/fbx/Cherry1.fbx` | SpeedTree-style export (≈6k triangles). Alpha-clipped leaves use `Cherry1_Opacity.tga`. Orchard edge. |
| `corn` | Corn (Maize) | `maize_corn_plant.glb` | Import through **glTFast**. Tall, so place it where it doesn't block the route. |
| `grape` | Grape | `Grapes/Meshy_AI_Mature_Indian_Grapevi_0924170001_image-to-3d-texture_fbx/` | Very heavy Meshy export (≈4.9M triangles). Decimate. Real height about 1.6 m. East fence. |
| `peach` | Peach | `Peach/Meshy_AI_Mature_Peach_Tree_0924191204_image-to-3d-texture_fbx/` | Very heavy Meshy export (≈8.0M triangles). Decimate. Real height about 2.5 m. Orchard edge. |
| `potato` | Potato | `potato-plants-collection/source/Potato_plants_Collection.fbx` | Collection file. Textures in `potato-plants-collection/textures/`. |
| `strawberry` | Strawberry | `Strawberry_1/Meshy_AI_strawberry_plant_0923200556_texture_fbx/` | Low-growing crop. |
| `tomato` | Tomato | Farm: `Tomato/Meshy_AI_Tomato_Plant_0923194145_texture_fbx/`. Lab: `Tomato_asset/` single-stem model if its geometry is cleaner after optimization | Recovery demo: default example disease Early Blight (Tomato is the original demo; every plant now has one). |

**Grape and Peach** models have been supplied (single textured Meshy meshes with a 4096 albedo and normal map). Both are in the plant catalog and selectable in the Farm and the Lab. Peach stands on the orchard edge with Apple and Cherry; Grape stands to the right of the entrance with a row of vines along the east fence.

**Environment assets:** Build the Farm and Lab structures from Unity geometry and materials. `sci-fi_lab.glb` (≈468 MB) may only be used as a source for a substantially optimized derivative; the raw model must never be placed in Quest scenes. `circuit-de-spa-francorchamps-2022-layout/`, `bermuda+grass.fbx`, `Meshy_AI_apple_tree_split_*.fbx`, `Apple/`, `Strawberry/`, `Tomato_1/`, and `Tree-Fruit_3d/` are not used.

### Prefab requirements (every plant)
- Correct real-world scale, pivot at ground level, URP materials, correct texture import, alpha clipping where needed, and a selection collider.
- **Lab prefab**: detailed close-up model (roughly 50–80k triangles, ≤ 2048 textures).
- **Farm prefab**: lower-detail LOD version for repeated planting (roughly 5–10k triangles, ≤ 1024 textures, GPU instancing).
- Rough target heights: fruit trees 3–4 m, corn about 2 m, tomato/pepper 0.8–1 m, potato/strawberry 0.3–0.5 m.

---

## 6. Interaction rules

- **Controllers**: ray interaction, teleportation, **30° snap turn**. Smooth movement is **disabled by default**.
- **Quest hands**: hand ray + pinch select, and near **poke** on buttons. Controllers take over automatically whenever hand tracking is lost.
- **PC VR**: controller interaction must be fully functional. Hand controls activate only when the OpenXR runtime supplies hand tracking.
- **Teleport** only onto safe paths and designated viewing points. Never land inside plant beds or scenery.
- **Recenter** control so seated and standing users can realign to the current spawn point.
- **Buttons** give hover, press, and selected feedback. Every control is reachable by controller ray and by hand pinch or poke.
- **Text** is legible at normal headset viewing distance: world-space panels about 1.2–1.5 m away, no face-locked or full-screen UI.
- **Palette**: charcoal translucent panels, white text, cyan (`#22D3EE`) selection states, restrained red/green health cues.

---

## 7. Farm (default scene)

1. A compact, life-sized field with a **central teleportable aisle** and raised soil beds. Low crops are in rows. Apple and Cherry trees stand along an orchard edge, and Corn is where its height won't block the route.
2. Blue sky, warm daylight, distant terrain/mountains, timber fence, notice board, and water tower. The central path and entry controls are visible at spawn.
3. Each of the nine plants has **one labeled, selectable specimen**. Selecting it (controller ray or hand pinch) highlights it, opens a small world-space name card, and saves its plant ID as the current selection.
4. A prominent **Enter Lab** button on the entry notice board, reachable with controller and hand input.

### Disease gallery (Farm only)
- **Behaviour:** within about 4 m of a specimen, selecting it (controller ray, hand pinch or desktop mouse click) also pops up a ring of cards around the plant: one per PlantVillage disease class, each with a sample leaf image, the disease name and a "PlantVillage sample" caption. Cards fly out from the plant centre with a staggered ease-out-back pop, face the viewer, and idle with a slow rotation and a small bob. Only one ring is open at a time. It closes (reverse animation) when the plant is selected again, when another plant or empty space is clicked, on Esc, H or F1, or when the viewer is more than 6 m away. Selecting a plant from farther than 4 m only shows "Move closer to see diseases" on its name card. Selecting still works as before (selection ring, name card, Enter Lab).
- **Illustration only:** the cards show what the dataset contains. They do not diagnose the plants in the scene.
- **Data:** `DiseaseClasses` (class list), `DiseaseGalleryCatalog.asset` (built by Build Plant Assets from `Assets/_Project/Textures/DiseaseLeaves/<plant>/<disease>.jpg`). Healthy classes are not shown. 24 classes: Apple 3 (Apple Scab, Black Rot, Cedar Apple Rust), Bell Pepper 1 (Bacterial Spot), Cherry 1 (Powdery Mildew), Corn 3 (Gray Leaf Spot, Common Rust, Northern Leaf Blight), Grape 3 (Black Rot, Esca, Leaf Blight), Peach 1 (Bacterial Spot), Potato 2 (Early Blight, Late Blight), Strawberry 1 (Leaf Scorch), Tomato 9 (Bacterial Spot, Early Blight, Late Blight, Leaf Mold, Septoria Leaf Spot, Two-Spotted Spider Mite, Target Spot, Yellow Leaf Curl Virus, Mosaic Virus).
- **Attribution:** leaf images are samples from the PlantVillage dataset, S. P. Mohanty, D. P. Hughes and M. Salathé, "Using deep learning for image-based plant disease detection", Frontiers in Plant Science 7:1419 (2016), https://github.com/spMohanty/PlantVillage-Dataset, licensed CC BY-SA 3.0 (per the dataset's Hugging Face card, https://huggingface.co/datasets/mohanty/PlantVillage; the GitHub repository itself carries no licence file). A credit line is shown on the Farm notice board. Share-alike applies to redistributed copies of the images.

## 8. Lab and VR interface

1. An airy glasshouse/lab: central plant pedestal, mountain or garden backdrop, clean structural frames, cool lighting.
2. The selected plant is displayed on the pedestal. World-space panels are arranged around it within comfortable viewing and reach distance.
3. Panels:
   - **Plant**: switches among the nine available species.
   - **Recovery Demo**: the Tomato demo (section 9).
   - **Plant Information**: concise species identity and an asset-backed 3D view.
4. **Return to Farm** control in a fixed, easy-to-find location.

---

## 9. Recovery demo for every plant (illustrative prototype)

> **Label (always visible): "Illustrative prototype. Not diagnostic or treatment advice."**
> This demo shows the interaction and visual progression for each of the nine plants, with an example disease taken from that plant's PlantVillage classes (defaults: Tomato Early Blight, Apple Apple Scab, Bell Pepper Bacterial Spot, Cherry Powdery Mildew, Corn Common Rust, Grape Black Rot, Peach Bacterial Spot, Potato Early Blight, Strawberry Leaf Scorch; a small control on the panel cycles through the plant's other classes). It is an illustrative prototype for every plant: it does not diagnose plants or recommend treatment, and the disease histories are examples, not data. (This knowingly replaces the earlier rule that only Tomato had a demo.)

- **Six-week timeline** with Previous, Next, direct week selection (Weeks 1–6), and **Reset** (back to Week 1).
- **Week 3** is marked **"Demo treatment applied."**
- **Diseased/healthy comparison** around the central pedestal, separated by a cyan visual marker.
- Leaf appearance changes across weeks through a **leaf mask** per plant (Tomato: the original derived mask; the other plants: a mask generated from the healthy Lab albedo, with seed-grown lesion and chlorosis threshold fields), all on the one BTP/Leaf Disease shader. Lesions spread as severity rises and shrink as it falls. **Fruit, stems, and soil are unaffected.** The left half of the plant shows Week 1 and the right half the selected week.
- Fixed example metrics (reproducible):

| Week | Leaf health | Chlorophyll | Disease severity |
|---|---:|---:|---:|
| 1 | 25% | 30% | 85% |
| 2 | 40% | 45% | 70% |
| 3 | 65% | 70% | 25% |
| 4 | 75% | 80% | 18% |
| 5 | 88% | 90% | 8% |
| 6 | 96% | 96% | 2% |

- **All nine plants** use the same fixed example table above. There is no "Tomato demo only" state any more. Selecting another plant in the Plant panel switches the demo to that plant without leaving the Lab.

---

## 10. Acceptance checks

**Journey**
- [ ] Launch opens the Farm through XRBootstrap.
- [ ] Each of the nine plants can be selected (highlight + name card).
- [ ] Enter Lab shows the selected plant's model on the pedestal (Tomato if nothing was selected).
- [ ] Tomato demo: Previous/Next, direct week jump, and Reset all update the leaves and metrics exactly as in the table. Week 3 shows "Demo treatment applied".
- [ ] Every plant shows the six-week timeline, the metrics, and the Week 1 / selected-week comparison, with its example disease named in the panel subtitle.
- [ ] Return to Farm keeps the selection.
- [ ] Grape and Peach appear as selectable choices in the Farm and the Lab (and, like every plant, have the illustrative recovery demo).

**XR**
- [ ] Controller ray, teleport, and 30° snap turn work on Quest 2, Quest 3, and Windows PC VR.
- [ ] Hand ray/pinch and poke work on Quest standalone. Controllers take over when tracking is lost. PC uses controller fallback.
- [ ] Scene transitions fade, with no camera jump and no duplicate XR rig, camera, audio listener, or EventSystem.
- [ ] Teleport never lands inside beds or scenery.

**Build and quality**
- [ ] The Quest (Android) and Windows builds both compile.
- [ ] All models and materials render (no pink materials). Corn imports through glTFast.
- [ ] World-space text is readable in the headset.
- [ ] Stable 72 FPS on Quest 2 in the Farm and the Lab.
