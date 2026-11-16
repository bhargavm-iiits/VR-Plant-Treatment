# Work Done So Far: VR Plant Disease & Treatment System (BTP)

**Report date:** 22 October 2026
**Project folder:** `D:\Unity Projects\Plant Disease\BTP_Project`
**Engine:** Unity 6000.5.5f1 (Unity 6.5), Universal Render Pipeline (URP)
**Repository:** git branch `main`, 124 commits from 22 Sep 2026 to 6 Oct 2026, plus uncommitted work (see section 10)

This report covers what has been built, how it is put together, what has been verified, and what is still open. Every figure comes from a file in the project folder or from the live Unity Editor; the source is named where it matters. Screenshots are in [`Docs/Screenshots`](Screenshots/), and section 9 shows all of them.

---

## Contents

1. [Project overview and goals](#1-project-overview-and-goals)
2. [Tech stack and packages](#2-tech-stack-and-packages)
3. [Development timeline](#3-development-timeline)
4. [Feature inventory (what works in each scene)](#4-feature-inventory-what-works-in-each-scene)
5. [Architecture](#5-architecture)
6. [Asset pipeline](#6-asset-pipeline)
7. [Testing and verification](#7-testing-and-verification)
8. [Builds and deliverables](#8-builds-and-deliverables)
9. [Screenshot gallery](#9-screenshot-gallery)
10. [Current status, limitations and next steps](#10-current-status-limitations-and-next-steps)

---

## 1. Project overview and goals

The project is an educational VR prototype about plant diseases. The user starts in a **Farm**, picks one of nine plant species, and goes to a **Lab** to look at that plant's diseased leaf up close and step through an illustrative six-week recovery. The original specification is in `Docs/PLAN.md`. The current, updated specification is `Docs/Master_Prompt.md`. The implementation plan and the first folder analysis are in `abc.md` at the repository root.

**User flow** (from `Docs/Master_Prompt.md`, section 4):

```
XRBootstrap (persistent)  --loads-->  Farm (Envirornment.unity, default)
                                        |  "Enter Lab"
                                        v
                                       Lab
                                        |  "Return to Farm"
                                        v
                                       Farm   (plant selection kept)
```

**Plants (nine):** Apple, Bell Pepper, Cherry, Corn (Maize), Grape, Peach, Potato, Strawberry, Tomato. The authoritative list is `Docs/References/plant_types.png`. The first plan hid Grape and Peach because their models were missing. Both were supplied and added on 5 Oct 2026.

**Target platforms** (from `Docs/Master_Prompt.md` section 2 and `XRProjectSetup.cs`):

| Target | Settings found in the project |
|---|---|
| Meta Quest 2 / Quest 3 (Android, standalone) | OpenXR with the Meta Quest feature, Vulkan only, IL2CPP, ARM64, minimum API level 29 (`ProjectSettings.asset`: `AndroidMinSdkVersion: 29`), OpenXR render mode Single Pass Instanced, package `com.btp.plantdiseasevr` |
| Windows PC VR (Link, SteamVR or any OpenXR runtime) | OpenXR, x86_64, Direct3D 11; controller profiles for Oculus Touch, Meta Quest Touch Plus/Pro, Valve Index, HTC Vive and Khronos Simple |

**Input:**
- **Controllers:** ray interaction, teleportation and 30-degree snap turn. Smooth movement and smooth turn stay off (`BootstrapSceneBuilder.cs`).
- **Hands (Quest):** hand ray with pinch, and poke on buttons, through XR Hands and the OpenXR Hand Interaction profile. Controllers take over when hand tracking is lost (`XRInputModalityManager`).
- **Desktop demo mode** (`DesktopDemoControls.cs`, added for demos without a headset): the mouse cursor acts as the controller ray, and left click selects plants or presses buttons. Hold the right mouse button to look around. WASD or the arrow keys walk, Shift speeds up, and Q/E turn. L enters the Lab, F returns to the Farm, R recenters and H toggles help. The mode turns on by itself when no real headset is active (`Docs/oldcnv.md`).

**Scope boundary:** the recovery demo is labelled **"Illustrative prototype. Not diagnostic or treatment advice."** The weekly numbers are fixed example values, not measurements. No disease classifier is implemented. The `com.unity.ai.inference` package is installed, but no project script uses it.

---

## 2. Tech stack and packages

These versions are read from `Packages/manifest.json`:

| Purpose | Package | Version |
|---|---|---|
| Rendering | `com.unity.render-pipelines.universal` (URP) | 17.5.0 |
| XR interaction | `com.unity.xr.interaction.toolkit` (XRI; Starter Assets and Hands Interaction Demo samples imported under `Assets/Samples`) | 3.5.1 |
| Hand tracking | `com.unity.xr.hands` | 1.8.0 |
| XR runtime | `com.unity.xr.openxr` | 1.17.1 |
| XR loader management | `com.unity.xr.management` | 4.5.4 |
| Input | `com.unity.inputsystem` | 1.19.0 |
| glTF/GLB import (Corn model) | `com.unity.cloud.gltfast` | 6.14.1 |
| UI, including TextMesh Pro (part of uGUI in Unity 6; TMP Essential Resources are under `Assets/TextMesh Pro`) | `com.unity.ugui` | 2.5.0 |
| Editor automation used to drive the live Editor from the command line | `com.unity.pipeline` | 0.7.0-exp.1 |
| Tests | `com.unity.test-framework` | 1.7.0 |
| Video capture (the `Recordings/` folder) | `com.unity.recorder` | 5.1.7 |
| Also installed, not used by project code | `com.unity.ai.inference` 2.6.1, `com.unity.ai.assistant` 2.20.0-pre.1, `com.unity.ai.navigation` 2.0.14, `com.unity.timeline` 1.8.12, `com.unity.visualscripting` 1.9.11, `com.unity.multiplayer.center` 1.0.1, `com.unity.collab-proxy` 2.13.3, IDE packages (Rider 3.0.38, Visual Studio 2.0.26) | — |

**Outside Unity:**
- **Blender 5.2** (`C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`; the leaf logs report Blender 5.2.2 LTS) runs headless for decimation, inspection, masks and leaf preparation (`Tools/*.py`, `Tools/decimate_all.ps1`).
- **Python** with NumPy and Pillow runs the leaf texture baking (`Tools/prepare_leaf_surface.py`), contact sheets and the GIF preview.
- **The `unity` CLI** runs C# in the open Editor (`Tools/eval/*.cs`, `*.ps1`).

**Quality settings** (`ProjectSettings/QualitySettings.asset`): there are two levels, **Mobile** (the Quest 2 baseline) and **PC**. The plan proposed separate Quest 2 and Quest 3 levels. Instead, `DeviceQualityProfile.cs` raises the URP render scale to 1.2 at run time on Quest 3 and Quest Pro. `QualityTuning.cs` records the tuned values: Mobile uses LOD bias 1, 4x MSAA and one 1024 shadow cascade over 20 m; PC uses LOD bias 3, forced anisotropy and 4x MSAA.

---

## 3. Development timeline

The dates below are commit dates from `git log`. One caution: the Quest APK, the Windows build and many preview images have file dates of **24–25 Sep 2026**, before some of the commits that describe the same work (for example the XR commits dated 30 Sep). The history appears to have been committed in stages after the work was done. Read the commit dates as the order of work, not as exact dates.

| Phase | Dates (commits) | What was done | Representative commits |
|---|---|---|---|
| **1. Repository and project setup** | 22 Sep | gitattributes/gitignore, Unity version and packages, URP graphics settings, quality presets, tags, XR plug-in management and OpenXR settings, input actions, reference images | `47a7862` init, `e2a128b` packages, `dd06d73` XR plug-in management, `1c72f28` reference images |
| **2. Core runtime** | 22–23 Sep | Runtime assembly, `EnvironmentId`, `PlantDefinition`/`PlantCatalog`, `EnvironmentController` (scene switching), `FadeOverlay` shader and controller, TextMesh Pro resources, first EditMode tests | `2f75f15`, `95c30d9`, `d23d9e5`, `2f96636` |
| **3. Blender tooling** | 24 Sep | Headless decimation script, loose-parts analyser, alpha packing, batch decimation driver, mesh inspection | `902e6b3`, `e35c9e6`, `6fde102` |
| **4. Unity asset pipeline** | 24–25 Sep | `ProjectPaths`, `TextureSettings`, `MaterialFactory`, procedural meshes and textures, `PlantAssetPipeline`, `BillboardBaker`, `PlantTint` | `e9dbacf`, `96916af` |
| **5. Plant models and prefabs** | 25 Sep | Apple, Bell Pepper, Cherry (alpha-cutout leaves), Corn (GLB through glTFast), Potato, Strawberry and Tomato imported and optimized; catalog with 7 species; farm and lab prefabs | `70f2b03`, `d2442b5`, `edaed05`, `1832211`, `857b6e5`, `55d8c5e` |
| **6. Device quality** | 26 Sep | `DeviceQualityProfile` (Quest 2 baseline, Quest 3 render scale) | `ae4ac7f` |
| **7. Farm scene** | 27 Sep | `FarmSceneBuilder`, `EnvironmentKit`, terrain/soil/fence/mountain textures, `PlantSpawner`, `SelectablePlant`, `SelectionRing`, `PlantNameCard`, `UIPalette`, `UIFactory`, `FaceCamera`, `CanvasEventCamera`, billboard atlases, Farm hierarchy and lighting | `1ad8eaa`, `23a3fd6`, `60344af`, `a63a7f2` |
| **8. Lab scene, shaders and UI** | 28 Sep | `LeafDisease` URP shader (input and forward-pass HLSL), `UnlitGlow`, Blender tomato disease mask, `LabPlantDisplay`, `LeafDiseaseVisual`, `Turntable`, `PlantButton`, `PlantInfoPanel`, `MetricBar`, `SelectedStateVisual`, `SelectionReadout`, glasshouse and pedestal | `5e3c837`, `710a175`, `ff1334d`, `90dac89` |
| **9. Recovery demo** | 29 Sep | `TomatoRecoveryData` (six weeks), `RecoveryTimeline`, `RecoveryDemoPanel`, `RecoveryDemoTests`, ring meshes | `8f3dd63`, `ca5f8c7`, `a79a247` |
| **10. XR rig and bootstrap** | 30 Sep | XRI Starter Assets, controller and poke interactors, XR Hands samples, `XRProjectSetup`, `XRSceneWiring`, `BootstrapSceneBuilder`, `RecenterControl`, `XRBootstrap.unity` | `d174072`, `f3672dd`, `1077135` |
| **11. Evaluation and automation tooling** | 28–30 Sep | `Tools/eval` scripts: capture, play-state probes, the `journey.ps1` end-to-end runner, `step.ps1` setup runner, shader/GLB checks, build-scene helpers | `1a77a66`, `0875abb`, `ffec026`, `1295df2` |
| **12. Release** | 30 Sep | Build scene list fixed (XRBootstrap, Envirornment, Lab), project settings finalised, Beamer slides, **release commit** | `d8d398a`, `70f1968`, `aafa20b` "release: finalize VR Plant Disease & Treatment System delivery" (30 Sep 2026, 22:40) |
| **13. Grape and Peach** | 5 Oct | Grape and Peach models, prefabs and catalog entries (catalog now has 9 species) | `16145db` |
| **14. Recovery demo for all nine plants** | 5 Oct | Per-plant Lab disease masks; every plant gets the six-week demo with a default PlantVillage example disease | `696694d` |
| **15. PlantVillage disease gallery** | 5 Oct | Ring of disease cards around a selected Farm specimen (24 classes) | `4744fca` |
| **16. Scene refresh and docs** | 6 Oct | Farm and Lab rebuilt with nine-plant layouts and baked billboards; spec, slides and quality tuning updated | `9d27e5f`, `1fffc5e` (last commit) |
| **17. Lab leaf-inspection display (uncommitted)** | about 13–14 Oct (leaf logs and previews dated 14 Oct; `Docs/Leaf_Display.md` verification dated 14 Oct) | The Lab pedestal now shows 24 disease-specific 3D leaves instead of the whole plant, with a new recovery shader, mouse rotation, viewer-facing controls and Week 6 = 100% | Not committed; see section 10 |

---

## 4. Feature inventory (what works in each scene)

The three scenes in the build, in order (`EditorBuildSettings`, confirmed in the Editor): `Assets/Scenes/XRBootstrap.unity`, `Assets/Scenes/Envirornment.unity` (the Farm; the misspelt name is the original template scene, renamed), and `Assets/Scenes/Lab.unity`.

### 4.1 XRBootstrap (persistent)
Hierarchy, inspected in the Editor on 22 Oct 2026:
- **XR Origin (XR Rig)** with `XROrigin`, `CharacterController`, `InputActionManager`, `XRInputModalityManager`, `XRGazeAssistance`, `RecenterControl` and `DesktopDemoControls`. Under it are the Main Camera (with `TrackedPoseDriver` and `AudioListener`), a gaze interactor, left and right controllers, left and right hands with poke and system-gesture detectors, a hand visualizer, and a `Locomotion` group (snap turn, teleportation and the other XRI providers).
- **XR Interaction Manager** and an **EventSystem** with `XRUIInputModule`.
- **BTP Systems**, which holds `SelectionState`, `EnvironmentController` and `DeviceQualityProfile`.

This scene has no visible geometry. On start it loads the Farm additively. The scene keeps one XR rig for the whole session. A play-mode check on 22 Oct 2026 reported `cameras=1 listeners=1 eventSystems=1 xrOrigins=1` in both the Farm and the Lab.

### 4.2 Farm (`Envirornment.unity`)
- A life-sized field (`FarmSceneBuilder.cs`) with a central dirt aisle and two cross paths (all teleport areas), ten raised soil beds with drip lines, a timber fence (24 m by 37 m), a water tower, boulders, a procedural mountain ring, 22 background trees and warm daylight with fog.
- **Crops:** rows of tomato (staked), bell pepper, strawberry, potato and corn, grape vines along the east fence, and an orchard edge of apple, cherry and peach trees. Farm albedos carry baked, illustrative "unhealthy foliage" on the leaves only (`FarmDiseaseTextures.cs`).
- **Nine labelled specimens**, one per species, each with a wooden name sign (common and scientific name) and a cyan selection ring. Selecting a specimen by controller ray, hand pinch or mouse click makes it the shared selection, lights its ring and opens a floating name card ("Selected. Press Enter Lab on the notice board.").
- **PlantVillage disease gallery:** when the viewer is within 4 m, selecting a plant also pops up a ring of cards, one per PlantVillage disease class for that plant (24 classes in total), each with a sample leaf photo and the disease name. The ring animates with an ease-out-back pop, a slow spin and a bob. It closes when the plant is selected again, when another plant or empty space is clicked, on Esc/H/F1, or when the viewer is more than 6 m away. From farther than 4 m the name card shows "Move closer to see diseases". Verified in Play mode on 22 Oct 2026 for Tomato (9 cards), Apple, Corn and Grape (3 cards each); see screenshots 08–11.
- **Notice board** facing the spawn point: the title "Plant Disease Lab", three-step instructions, a live readout of the selected plant, a large **Enter Lab** button, **Recenter view**, and the PlantVillage credit line (CC BY-SA 3.0). If no plant is selected, the Lab opens with Tomato.
- **Teleport viewpoints:** Notice Board, Orchard, Corn and Field End. The spawn anchor is at (0, 0, -9.5).
- Distant plants switch to baked billboards (an LODGroup with a near mesh and a 4-triangle billboard).

### 4.3 Lab (`Lab.unity`)
- A glasshouse with steel frames, glass panes, a tiled floor, back planters holding farm plants, a mountain backdrop and cool lighting (`LabSceneBuilder.cs`).
- **Central pedestal:** in the current, uncommitted version it shows **one enlarged 3D leaf** for the selected plant and disease (24 leaf prefabs). An earlier committed version showed the whole Lab plant. A cyan divider splits the leaf: the left half keeps the supplied diseased texture (Week 1) and the right half shows the selected week. Labels read "Diseased · Week 1" and "Current · Week N".
- **Leaf condition selector** above the leaf: one button for each of the plant's disease classes (for example, nine for Tomato and three for Apple).
- **Plant panel** (left): nine species buttons with scientific names. The current species is highlighted in cyan.
- **Recovery Demo panel** (right): plant and disease subtitle, the permanent prototype disclaimer, "Week N of 6", three metric bars (leaf health, chlorophyll, disease severity), a PlantVillage sample photo of the disease, and previous/next disease controls.
- **Six-week timeline** below the leaf: previous, Week 1–6, next and Reset. "Demo treatment applied" appears at Week 3, and Week 6 reads "Complete recovery (illustrative)".
- **Plant Information panel:** family, plant type, a short description and a small rotating 3D model of the whole plant on its own column.
- **Navigation panel:** **Return to Farm** and **Recenter view**.
- **Leaf interaction (desktop):** drag with the left mouse button on the leaf to turn it, or drag vertically to tilt it (limited to 65 degrees each way). There is no idle rotation. The frame and controls turn to face the tracked viewer as they move (`LeafViewerFacing.cs`).
- **Recovery animation:** changing week animates over 1.6 s. At Week 6 the whole leaf turns healthy, the divider and labels hide, and the metrics end at 100% / 100% / 0% (`Docs/Leaf_Display.md`). All of these states were captured in Play mode on 22 Oct 2026 (screenshots 12–21).

**Example metrics** shared by every plant (`TomatoRecoveryData.asset`, working copy):

| Week | Leaf health | Chlorophyll | Disease severity | Caption |
|---|---:|---:|---:|---|
| 1 | 25% | 30% | 85% | |
| 2 | 40% | 45% | 70% | |
| 3 | 65% | 70% | 25% | Demo treatment applied |
| 4 | 75% | 80% | 18% | |
| 5 | 88% | 90% | 8% | |
| 6 | 100% | 100% | 0% | Complete recovery (illustrative) |

Week 6 was 96 / 96 / 2 in the last commit. The change to 100 / 100 / 0 is uncommitted.

**Default example disease for each plant** (`DiseaseClasses.DefaultDiseaseId`): Tomato Early Blight, Apple Apple Scab, Bell Pepper Bacterial Spot, Cherry Powdery Mildew, Corn Common Rust, Grape Black Rot, Peach Bacterial Spot, Potato Early Blight, Strawberry Leaf Scorch.

---

## 5. Architecture

### 5.1 Runtime scripts (`Assets/_Project/Scripts/Runtime`, assembly `BTP.Runtime`)

| Folder / namespace | Script | Purpose |
|---|---|---|
| Core (`BTP.Core`) | `EnvironmentController` | Keeps exactly one environment (Farm or Lab) loaded next to XRBootstrap. A switch fades out, unloads, loads, moves the rig to the `SpawnAnchor` and fades in. Repeated presses are ignored during a switch. |
| | `EnvironmentId` | Enum: Farm, Lab. |
| | `EnvironmentButton` | Connects an environment-scene UI button (Enter Lab, Return to Farm, Recenter) to the persistent controller. |
| | `SelectionState` / `PlantSelection` | Shared plant selection. `PlantSelection` is plain C# so it can be unit tested. Defaults to Tomato. |
| | `FadeOverlay` | Camera-attached fade used during scene transitions. |
| | `SpawnAnchor` | Where the user stands after loading or recentering. |
| | `RecenterControl` | Recenters on the spawn point from the left controller's menu button, or R on the keyboard. |
| | `BootstrapGuard` | Lets Farm or Lab be played on its own in the Editor by loading XRBootstrap next to it. |
| | `DesktopDemoControls` | Keyboard and mouse mode when no headset is active (see section 1). |
| | `DeviceQualityProfile` | Quest 2 baseline; render scale 1.2 on Quest 3 and Quest Pro. |
| | `ScenePreviewCamera` | Edit-time preview camera for the Farm and Lab. It switches itself off at run time. |
| Farm (`BTP.Farm`) | `SelectablePlant` | Specimen selection (ray, pinch or mouse), ring and name-card state, and the gallery trigger. |
| | `SelectionRing` | Idle, hover and selected glow states of the ground ring. |
| | `PlantNameCard` | Floating card for the selected plant. |
| | `DiseaseGalleryController` | Pooled ring of PlantVillage disease cards, with open, close and idle animation. |
| | `DiseaseRingLayout` | Pure maths for the ring: angles, radius clamp, ease-out-back curve, stagger and ground-distance check. |
| Lab (`BTP.Lab`) | `LabPlantDisplay` | Instantiates the leaf prefab for the current plant and disease, fits it to the display size and adds mouse rotation. A missing entry shows a visible setup-error marker. |
| | `LeafDiseaseSelector` *(new)* | Disease-condition buttons above the leaf. |
| | `LeafRecoveryVisual` *(new)* | Drives the `BTP/Leaf Recovery` shader: recovery amount, baseline half, 1.6 s transition and the split plane. |
| | `LeafMouseRotation` *(new)* | Drag to turn or tilt around the specimen centre; ignores UI clicks; no inertia. |
| | `LeafViewerFacing` *(new)* | Keeps the inspection frame and controls facing the tracked viewer. |
| | `LeafDiseaseVisual` | Earlier whole-plant driver for the `BTP/Leaf Disease` shader (diseased/current split). |
| | `RecoveryDemoPanel` | Recovery Demo panel and week timeline: metric bars, captions, disease cycling and comparison labels. |
| | `RecoveryTimeline` | Pure week logic: step, clamp to 1–6 and reset. |
| | `TomatoRecoveryData` / `RecoveryWeek` | The six-week example table, shared by all nine plants (the original Tomato name is kept). |
| | `PlantButton` | Plant-panel button that calls `SelectPlant`. |
| | `PlantInfoPanel` | Species identity text and a rotating 3D view of the plant model. |
| Plants (`BTP.Plants`) | `PlantDefinition` | ID, display and scientific name, family, type, summary, farm and lab prefabs, and the recovery-demo flag. |
| | `PlantCatalog` | List of 9 definitions, `TryGet`, `Validate()`; default is Tomato. |
| | `PlantSpawner` | Instantiates plant models; a missing model gives a visible setup error. |
| | `DiseaseClasses` | The 24 PlantVillage classes (healthy classes excluded), default disease for each plant, cycling helpers and the credit text. |
| | `DiseaseGalleryCatalog` | Plant ID to sample leaf images and names for the gallery. |
| | `LeafCatalog` *(new)* | Plant ID and disease ID to leaf display prefab (24 entries). |
| UI (`BTP.UI`) | `UIPalette` | Charcoal, white, cyan and red/green tokens from the lab reference. |
| | `MetricBar` | Animated percentage bar. |
| | `SelectedStateVisual` | Persistent cyan "selected" look on buttons. |
| | `SelectionReadout` | Notice-board readout of the current selection. |
| | `FaceCamera` | Billboards a canvas towards the viewer. |
| | `CanvasEventCamera` | Assigns the XR camera as the event camera of world-space canvases. |
| | `RingMesh` | Procedural ring geometry. |
| | `Turntable` | Constant rotation for the info-panel model. |

### 5.2 Editor tooling (`Assets/_Project/Scripts/Editor`, assembly `BTP.Editor`)

The **BTP > Setup** menu rebuilds the project from its sources, in order:

| Menu item | Code | What it does |
|---|---|---|
| 1 - Configure XR Project | `XRProjectSetup.Configure` | OpenXR for Quest and Windows, controller and hand profiles, Quest player settings (Vulkan, IL2CPP, ARM64, API 29), Single Pass Instanced, Editor XR simulator |
| 2 - Build Plant Assets | `PlantAssetPipeline` (+ `BillboardBaker`, `FarmDiseaseTextures`, `LabDiseaseMasks`, `DiseaseGalleryBuilder`, `TextureSettings`, `MaterialFactory`) | Import settings, URP materials, Lab prefabs, Farm prefabs (near mesh plus baked billboard LOD), plant catalog, recovery data, per-plant disease masks and the disease gallery catalog |
| 3 - Build XR Bootstrap Scene | `BootstrapSceneBuilder` | Persistent rig, 30-degree snap turn, fade, XR UI EventSystem, selection and environment systems, desktop controls |
| 4 - Build Farm Scene | `FarmSceneBuilder` (+ `EnvironmentKit`, `ProceduralMeshes`, `ProceduralTextures`, `UIFactory`, `XRSceneWiring`) | The whole Farm (see 4.2) |
| 5 - Build Lab Scene | `LabSceneBuilder` | The whole Lab, now including the leaf layout |
| 6 - Add Preview Cameras To Environment Scenes | `PreviewCameraInstaller` | Adds edit-time preview cameras without rebuilding the scenes |
| 6 - Prepare Leaf Display Assets *(new)* | `LeafAssetPipeline.Build` | Leaf materials, 24 display prefabs, texture copies (2048 px on PC, 1024 on Android) and `LeafCatalog.asset` |
| 7 - Update Lab Leaf Display *(new)* | `LabSceneBuilder.UpdateLeafDisplay` | Applies the leaf layout to the existing Lab scene |

Note that two menu items are both numbered "6".

Other editor code:
- `Automation.cs` writes START, DONE and FAILED lines to `Tools/logs/automation.log`, so command-line runs can be followed.
- `QualityTuning.cs` applies the tuned quality values.
- `PlantTint.cs` makes yellowed decorative material variants.
- `ProjectPaths.cs` holds the shared asset paths.

### 5.3 Data assets (`Assets/_Project/Data`)
- `PlantCatalog.asset` plus `Plants/<id>.asset`: nine plant definitions.
- `DiseaseGalleryCatalog.asset`: 24 disease classes with PlantVillage sample images from `Textures/DiseaseLeaves/<plant>/<disease>.jpg`.
- `LeafCatalog.asset` *(new, uncommitted)*: 24 leaf prefabs.
- `TomatoRecoveryData.asset`: the six-week example table.
- `Lighting/`: lighting settings for the Farm and Lab.

### 5.4 Shaders (`Assets/_Project/Shaders`)
| Shader | Use |
|---|---|
| `BTP/Leaf Disease` (`LeafDisease.shader` + `LeafDiseaseInput.hlsl` + `LeafDiseaseForwardPass.hlsl`) | URP Simple Lit with an illustrative lesion and chlorosis overlay driven by a leaf mask. Only leaf texels change; fruit, stems and soil keep their colour. Has a diseased/current split plane. Used by the whole-plant recovery demo. |
| `BTP/Leaf Recovery` *(new)* | Blends the supplied diseased leaf texture towards a reconstructed healthy texture, using a per-texel recovery-delay map, vein normal maps, roughness and a small thin-leaf transmission term. Split plane for the comparison. |
| `BTP/Unlit Glow` | Transparent, double-sided highlight colour for selection rings, teleport pads and the cyan divider. |
| `BTP/Fade Overlay` | Full-view fade for scene transitions (ZTest Always). |

### 5.5 Procedural content
- **Meshes** (`Models/Generated`): selection rings at several radii, the pedestal ring, the teleport-pad ring, the water-tower roof cone, the mountain rings (`FarmMountains`, `FarmMountainsTall`) and the combined corn mesh.
- **Textures** (`ProceduralTextures.cs`): grass, soil, dirt path, wood and mountain swatches, plus rounded 9-slice UI sprites.
- **Baked:** billboard atlases (`Textures/Billboards`), farm "unhealthy foliage" albedos, per-plant Lab disease masks (R = leaf, G = lesion field, B = chlorosis field), and healthy and recovery maps for each leaf.

---

## 6. Asset pipeline

### 6.1 Sources
- `Assets/Plant_Asstes` (about 2.8 GB; the folder name's typo is kept on purpose) holds the supplied plant models. Most are Meshy AI exports, plus a SpeedTree-style cherry, a potato collection and `maize_corn_plant.glb`. Unused sources (`sci-fi_lab.glb`, the Spa circuit model, `bermuda+grass.fbx`, `Apple/`, `Strawberry/`, `Tomato_1/`, `Tree-Fruit_3d/`) stay in place but are not referenced, so they do not go into builds.
- `Assets/Leaf_Assets` (about 2.4 GB, uncommitted) holds 24 supplied diseased-leaf models in folders named `<Plant>/<Disease>/` (Meshy exports dated 13 Oct). The originals are treated as read-only inputs.

### 6.2 Plant decimation (Blender, headless)
`Tools/decimate_all.ps1` calls `Tools/blender_decimate.py` for each plant. The script re-pivots the model to the ground, scales it to a real height and decimates it to fixed budgets: first `<Plant>_Lab.fbx` from the source, then `<Plant>_Farm.fbx` from the Lab mesh. The source and Lab triangle counts below come from `Tools/logs/decimate.log`. The current prefab counts were measured in the Editor on 22 Oct 2026.

| Plant | Source triangles | Lab prefab triangles | Farm near-mesh triangles | Height (m) |
|---|---:|---:|---:|---:|
| Apple | 7,648,230 | 80,000 | 50,000 | 3.5 |
| Bell Pepper | 4,299,082 | 60,000 | 18,000 | 0.7 |
| Cherry | 6,100 | 6,100 | 6,100 | 3.5 |
| Corn | (GLB through glTFast) | 3,108 | 3,108 | 2.2 |
| Grape | 4,923,968 | 70,000 | 30,180 | 1.6 |
| Peach | about 8.0 M (`Master_Prompt.md`) | 80,000 | 30,000 | 2.5 |
| Potato | 12,620 | 12,620 | 7,999 | 0.5 |
| Strawberry | 565,360 | 50,000 | 8,000 | 0.3 |
| Tomato | 1,620,650 (farm source); Lab uses the single-stem model, 5,017,024 | 69,998 | 10,000 | 1.0 |

- **Billboards:** every Farm prefab is an `LODGroup` with two levels: the near mesh, then a 4-triangle crossed-quad billboard baked by `BillboardBaker.cs` (front and side views, exact alpha). LOD transition heights range from 0.03 (strawberry) to 0.23 (apple).
- **Budgets:** the plan targeted roughly 50–80k triangles for Lab models and 5–10k for Farm models. Apple (50k), Bell Pepper (18k), Grape (30k) and Peach (30k) Farm meshes are above 10k. The comments in `decimate_all.ps1` explain that Bell Pepper and Apple "cannot collapse below ~14k / ~40k triangles without shredding" and that only the nearest plants use the near mesh.
- **Textures:** the cap is 2048 for Lab and 1024 for Farm, with ASTC on Android (`abc.md` Phase 2, `TextureSettings.cs`). Cherry uses alpha-clipped leaves from `Cherry1_Opacity.tga`.
- **Tomato disease mask:** `Tools/blender_tomato_disease.py` derives a leaf/stem/fruit mask for the original Tomato demo. The other plants get masks generated in Unity by `LabDiseaseMasks.cs`.

### 6.3 Leaf pipeline (new, uncommitted)
1. `python Tools/prepare_leaf_assets.py` runs Blender (`Tools/blender_leaf.py`) on all 24 leaves. It writes upright, front-facing, decimated copies to `Assets/_Project/Models/Leaves` and the map to `Tools/leaf_manifest.json`. The per-leaf logs in `Tools/logs/leaves` show sources of 74k to 1.19 M triangles reduced to about 12,000.
2. `python Tools/prepare_leaf_surface.py` bakes a reconstructed **healthy** colour atlas from the green regions of each supplied texture, together with recovery-delay and roughness maps. It also applies orientation corrections from `Tools/leaf_orientations.json`.
3. **BTP > Setup > 6 - Prepare Leaf Display Assets** builds the materials, 24 prefabs and `LeafCatalog.asset`. **7 - Update Lab Leaf Display** updates the Lab scene.

Measured in the Editor on 22 Oct 2026: 23 leaf prefabs have about 12,000 triangles, and **Grape Leaf Blight has 79,997**. `Docs/Leaf_Display.md` explains this as welding coincident geometry to keep its folded surface; the Blender log for this leaf shows 22,264. Screenshot 24 (an earlier automated preview) shows all 24 leaves.

---

## 7. Testing and verification

### 7.1 EditMode tests (`Assets/_Project/Tests/EditMode`, assembly `BTP.Tests.EditMode`)
| Suite | Test methods | Test cases | Covers |
|---|---:|---:|---|
| `PlantCatalogTests.cs` | 10 | 10 | The 9-plant catalog is valid; Grape and Peach are accepted; a wrong count, missing models and missing demos are reported; selection starts empty, defaults to Tomato, keeps an explicit choice, raises one change event and rejects plants without models |
| `RecoveryDemoTests.cs` | 7 (one with 6 `[TestCase]`s) | 12 | Weeks match the table; one shared table for all plants; default example disease for each plant; disease cycling wraps; only Week 3 is "treatment"; timeline steps, clamps and resets |
| `DiseaseGalleryTests.cs` | 11 | 11 | Class counts per plant, names and IDs, unknown plants, leaf images for every disease, ring layout maths (angles, circle offsets, radius clamp, near check, ease-out-back, stagger) |
| `LeafDisplayTests.cs` *(new)* | 9 | 9 | Mouse drag rotates around an offset centre and stops on release; ignores buttons and empty space; tilt limit; the leaf catalog covers every condition; week interpolation and reversal; frame-rate-independent animation; Week 6 fully recovers both halves; front and right are consistent from every side; facing; Week 1 keeps the supplied texture |
| **Total** | **37** | **42** | |

**Recorded results.** No test run was made for this report.
- **42 of 42 passed**, according to `Docs/Leaf_Display.md` ("Verification (14 October 2026)"). The 42 matches the test-case count above.
- **16 of 16 passed** is the earlier figure (before Grape/Peach, the gallery and the leaf display), according to `Docs/Presentation_Raw_Slides.md` and `Docs/Presentation_Beamer.tex`. Both cite `Logs/Editor-prev.log`, which has since been overwritten and no longer contains a "Run finished" line.

### 7.2 Automation and integration checks (`Tools/eval`, 49 files)
- **Capture:** `capture_views.cs`, `capture_lab.cs`, `capture_leaf_display.cs` and `capture_leaf_front_back.cs` render previews with a temporary camera to `Tools/out/previews`.
- **Journey:** `play_action.cs` and `play_state.cs` drive and report Play mode. `journey.ps1` runs Farm, select, Enter Lab, weeks, Reset, change plant and Return to Farm. `desktop_test.cs` simulates mouse clicks.
- **Setup steps:** `step.ps1` with `step_*.cs` runs the BTP > Setup steps through `Automation.Run` and waits for DONE or FAILED in `Tools/logs/automation.log`.
- **Checks:** `verify_xr.cs`, `verify_plants.cs`, `check_shaders.cs`, `check_glb.cs`, `inspect_rig.cs`, `inspect_crops.cs` and `console_errors.cs`.
- **Leaf display (new):** `verify_leaf_display.cs`, `verify_leaf_animation.cs`, `verify_leaf_rotation.cs`, `verify_leaf_views.cs`, `verify_leaf_startup.cs`, `audit_leaf_orientation.cs` and `audit_grape_faces.cs`.

Recorded outcomes, as written in the project files:
- `Tools/out/previews/leaf_rotation/result.txt`: "PASS: 24 conditions; mouse drag turns and tilts, release stops, fixed specimen centre/controls, comparison plane tracks rotation, whole-leaf Week 6 recovery. Idle movement: 0 degrees."
- `Tools/out/previews/recovery_motion/result.json`: `"passed": true`, with the recovery value sampled over time during a week change. `recovery.gif` is the looping animation.
- `Docs/Leaf_Display.md` (14 Oct 2026): scripts compiled with zero errors and warnings. Play-mode integration covered all 24 conditions, orientation corrections, model fitting, disease selection, Week 6 and Reset. 96 extra endpoint and facing checks passed (24 conditions from four viewpoints).

### 7.3 Checks made while writing this report (22 Oct 2026)
- The Editor entered Play mode through XRBootstrap and loaded the Farm.
- Selecting Tomato, Apple, Corn and Grape opened their disease rings.
- Enter Lab loaded the Lab with Tomato / Early Blight.
- Plant, disease and week changes updated the panel text and the leaf. Week 3 showed "Demo treatment applied", and Week 6 showed 100% / 100% / 0% and "Complete recovery (illustrative)".
- One camera, one audio listener, one EventSystem and one XR Origin were present.
- Play mode was then exited.

### 7.4 Not yet verified
`Docs/Leaf_Display.md` and `Docs/Presentation_Raw_Slides.md` both state that no headset testing (Quest 2, Quest 3 or PC VR), frame-rate measurement or user study has been recorded. The acceptance checklist in `Docs/Master_Prompt.md` section 10 is still unticked.

---

## 8. Builds and deliverables

| Item | Details |
|---|---|
| **Quest APK** | `Builds/Quest/PlantDiseaseVR.apk`, 99,168,135 bytes (about 94.6 MiB), file date 25 Sep 2026 02:25. A Burst debug folder and an IL2CPP backup folder are next to it. |
| **Windows PC VR build** | `Builds/Windows/PlantDiseaseVR.exe` with `PlantDiseaseVR_Data`, `UnityPlayer.dll` (37.1 MB), D3D12 and DirectStorage DLLs, file date 25 Sep 2026 01:54–01:55. |
| **Recordings** | `Recordings/Movie_002.mp4` (616.9 MB, 25 Sep 2026 12:17) and `Recordings/Movie_003.mp4` (324.9 MB, 25 Sep 2026 12:20), presumably made with Unity Recorder. Their durations were not checked. |
| **Presentation** | `Docs/Presentation_Raw_Slides.md` (a 12-slide, 10-minute script with speaker cues, marked as a draft for review) and `Docs/Presentation_Beamer.tex` (Metropolis-theme Beamer slides). |
| **Specification and plans** | `Docs/PLAN.md` (original spec), `Docs/Master_Prompt.md` (current spec), `abc.md` (implementation plan and folder analysis), `Docs/oldcnv.md` (summary of the desktop-mode and controller fix session), `Docs/Leaf_Display.md` (leaf display, uncommitted). |
| **Reference images** | `Docs/References/plant_types.png`, `farming_land.png` and `lab.png`. |

Both builds date from 25 Sep 2026, so they **predate** Grape and Peach, the disease gallery, the all-plant recovery demo and the leaf display. `Docs/oldcnv.md` also notes that the Windows exe predates desktop demo mode. The editor's active build target is Android, according to the Editor title bar.

---

## 9. Screenshot gallery

All images are in `Docs/Screenshots`. Images 01–21 and 23 were rendered from the live Unity Editor on 22 Oct 2026 with a temporary camera at 1600x900 (the 23 contact sheet is 1560x1560). Images 08–21 were taken in Play mode through the normal XRBootstrap flow, from the viewer's head position, so the UI faces the camera as it would for a user. Image 22 is a capture of the Editor window. Images 24–25 are **earlier automated previews** copied from `Tools/out/previews` (14 Oct 2026).

### Farm

**01. Spawn view.** The central aisle, raised beds with labelled specimens and selection rings, the notice board on the left, and the orchard, corn and water tower behind.
![Farm spawn view](Screenshots/01_farm_spawn_view.png)

**02. Aerial overview.** The fenced field, central aisle and cross paths, beds, corn block, orchard edge, water tower and background trees.
![Farm aerial overview](Screenshots/02_farm_aerial_overview.png)

**03. Specimen row.** Tomato, Bell Pepper, Strawberry and Potato specimens with name signs and cyan rings at the start of the aisle.
![Farm specimen row](Screenshots/03_farm_specimen_row.png)

**04. Notice board.** Title, instructions, the **Enter Lab** and **Recenter view** buttons, and the PlantVillage credit line.
![Notice board with Enter Lab](Screenshots/04_farm_notice_board_enter_lab.png)

**05. Orchard edge.** Apple and Peach specimens with selection rings and name signs.
![Orchard edge](Screenshots/05_farm_orchard_edge.png)

**06. Corn block and grape specimen.** The east side of the farm, with the water tower behind.
![Corn and grape](Screenshots/06_farm_corn_and_grape.png)

**07. High oblique view.** The whole farm layout. A gap in the procedural mountain ring is visible at the top left.
![High oblique farm view](Screenshots/07_farm_high_oblique_water_tower.png)

**08. Tomato selected, with its disease gallery.** Nine PlantVillage disease cards around the specimen (Play mode).
![Tomato disease gallery](Screenshots/08_farm_tomato_selected_disease_gallery.png)

**09. Apple disease gallery.** Apple Scab, Black Rot and Cedar Apple Rust cards around the selected Apple specimen (Play mode).
![Apple disease gallery](Screenshots/09_farm_apple_disease_gallery.png)

**10. Corn disease gallery.** Common Rust and Gray Leaf Spot (Cercospora) cards; the third card (Northern Leaf Blight) is hidden behind the plant from this angle. The "Corn (Maize)" name card is above (Play mode).
![Corn disease gallery](Screenshots/10_farm_corn_disease_gallery.png)

**11. Grape disease gallery.** Leaf Blight, Black Rot and Esca cards around the Grape specimen (Play mode).
![Grape disease gallery](Screenshots/11_farm_grape_disease_gallery.png)

### Lab

**12. Lab overview.** The glasshouse, Plant Information panel, Plant panel, leaf condition selector, Recovery Demo panel, Navigation panel, timeline, and the Tomato Early Blight leaf on the pedestal at Week 1. The whole-plant model on the left column is the Plant Information 3D view.
![Lab overview](Screenshots/12_lab_overview_tomato_week1.png)

**13. Leaf close-up, Tomato Early Blight, Week 1.** The cyan divider and the "Diseased · Week 1" / "Current · Week 1" labels.
![Leaf close-up week 1](Screenshots/13_lab_leaf_closeup_tomato_early_blight_week1.png)

**14. Tomato, Week 3, "Demo treatment applied".** The right half of the leaf has partly recovered; the metrics read 65% / 70% / 25%.
![Tomato week 3](Screenshots/14_lab_tomato_week3_treatment_applied.png)

**15. Tomato, Week 6, "Complete recovery (illustrative)".** The whole leaf is green, the divider and labels are hidden, and the metrics read 100% / 100% / 0%.
![Tomato week 6](Screenshots/15_lab_tomato_week6_recovered.png)

**16. Apple, Apple Scab, Week 1.** The condition selector shows the three apple diseases.
![Apple scab week 1](Screenshots/16_lab_apple_scab_week1.png)

**17. Strawberry, Leaf Scorch, Week 4.** Metrics 75% / 80% / 18%.
![Strawberry leaf scorch week 4](Screenshots/17_lab_strawberry_leaf_scorch_week4.png)

**18. Grape, Esca (Black Measles), Week 2.** Diseased left half and partly recovering right half.
![Grape esca week 2](Screenshots/18_lab_grape_esca_week2.png)

**19. Potato, Late Blight, Week 5.** Metrics 88% / 90% / 8%.
![Potato late blight week 5](Screenshots/19_lab_potato_late_blight_week5.png)

**20. Plant Information panel (Peach).** Family Rosaceae, deciduous fruit tree, a short description and the rotating 3D model.
![Plant information panel](Screenshots/20_lab_plant_information_panel_peach.png)

**21. Navigation panel.** **Return to Farm** and **Recenter view**, next to the Recovery Demo panel (Peach, Bacterial Spot, Week 3).
![Navigation panel](Screenshots/21_lab_navigation_panel_return_to_farm.png)

### Editor and assets

**22. Unity Editor window with the Lab scene open (edit mode).** The Game view shows the Lab through its edit-time preview camera. In edit mode the panels have not initialised, so the 0% bars and the empty condition buttons are placeholders, not runtime values. The **BTP** menu is visible in the menu bar.
![Unity Editor](Screenshots/22_unity_editor_lab_scene.png)

**23. Current Lab prefabs of all nine species**, rendered in an isolated preview scene. Top row: Apple, Bell Pepper, Cherry. Middle row: Corn, Grape, Peach. Bottom row: Potato, Strawberry, Tomato.
![Nine Lab prefabs](Screenshots/23_plant_lab_prefabs_9_species.png)

**24. Earlier automated preview (14 Oct 2026): orientation contact sheet of the 24 leaf models** (`Tools/out/previews/leaf_orientation_sheet.png`).
![Leaf orientation sheet](Screenshots/24_earlier_preview_leaf_orientation_sheet.png)

**25. Earlier automated preview (14 Oct 2026): Lab seen from behind the pedestal at Week 6 (Apple Scab).** It shows that the controls stay readable from the rear viewpoint (`Tools/out/previews/leaf_front_back/rear_week6.png`).
![Rear view week 6](Screenshots/25_earlier_preview_lab_rear_view_week6.png)

The design references the scenes were built from are in `Docs/References`: [plant types](References/plant_types.png), [farm](References/farming_land.png) and [lab](References/lab.png).

---

## 10. Current status, limitations and next steps

### 10.1 Done and committed (up to `1fffc5e`, 6 Oct 2026)
- Unity 6 URP project configured for OpenXR on Quest (Android) and Windows PC VR.
- A persistent XR bootstrap with one rig (controllers, hands, teleport, 30-degree snap turn, recenter), fade transitions and a shared plant selection.
- The Farm scene with nine selectable, labelled specimens, a notice board with Enter Lab, teleport viewpoints, billboard LODs and the PlantVillage disease gallery (24 classes).
- The Lab scene with Plant, Plant Information, Recovery Demo and Navigation panels, a six-week timeline, and a diseased/current split comparison for all nine plants.
- Keyboard and mouse demo mode for showing the project without a headset.
- Blender and Unity pipelines that rebuild every asset and scene from menu steps.
- EditMode tests and Editor automation scripts.
- A Quest APK and a Windows build (both 25 Sep), screen recordings, and presentation material.

### 10.2 In progress / not yet committed (working copy as of 22 Oct 2026)
The **Lab leaf-inspection display** is implemented and verified in the Editor (`Docs/Leaf_Display.md`, 14 Oct 2026) but not committed:
- **New (untracked):** `Assets/Leaf_Assets/` (24 source leaves); `Assets/_Project/Models|Materials|Prefabs|Textures/Leaves/`; `Data/LeafCatalog.asset`; scripts `LeafCatalog.cs`, `LeafRecoveryVisual.cs`, `LeafMouseRotation.cs`, `LeafViewerFacing.cs`, `LeafDiseaseSelector.cs` and `LeafAssetPipeline.cs`; `Shaders/LeafRecovery.shader`; `Tests/EditMode/LeafDisplayTests.cs`; `Docs/Leaf_Display.md`; `Tools/blender_leaf.py`, `prepare_leaf_assets.py`, `prepare_leaf_surface.py`, `leaf_contact_sheet.py`, `recovery_preview_gif.py` and `leaf_*.json`; 16 new `Tools/eval` leaf scripts.
- **Modified:** `Assets/Scenes/Lab.unity` (large change); `LabSceneBuilder.cs`, `LabPlantDisplay.cs`, `RecoveryDemoPanel.cs`, `DesktopDemoControls.cs` and `TomatoRecoveryData.cs` with its `.asset` (Week 6 changed from 96/96/2 to 100/100/0); `RecoveryDemoTests.cs` (updated to match); `Docs/Master_Prompt.md` (Week 6 row).
- Committing this work needs care because of the size of `Assets/Leaf_Assets`, about 2.4 GB. Git LFS, or keeping the raw sources out of the repository, may be needed.

### 10.3 Known limitations
- **Not scientific data.** The six-week values are fixed examples, and the Week 6 healthy leaf is a *visual reconstruction* from the green areas of each diseased texture. It is not a measured healthy leaf or a real treatment response (`Docs/Leaf_Display.md`, `prepare_leaf_surface.py`). The prototype disclaimer stays on screen. The disease names come from PlantVillage classes and are examples, not diagnoses.
- **No headset or performance validation.** Nothing in the folder records Quest 2, Quest 3 or PC VR testing, the 72 FPS target, or hand-tracking usability.
- **The builds are out of date.** Both builds date from 25 Sep 2026 and lack Grape and Peach, the gallery, the all-plant demo and the leaf display. The Windows build also lacks desktop mode.
- **Grape and Peach.** They were added late, on 5 Oct. Their Farm near meshes (30k triangles each) are above the original 5–10k Farm target, and the Grape Leaf Blight leaf is about 80k triangles against about 12k for the others. Grape leaves needed orientation corrections to stand upright (`Leaf_Display.md`).
- **Some budgets are over target.** The Apple Farm mesh is 50k triangles. Budgets were raised deliberately, as documented in `decimate_all.ps1`, but their effect on Quest 2 has not been measured.
- **Quality levels differ from the plan.** Only "Mobile" and "PC" levels exist. Quest 3 is handled at run time with a render-scale change.
- **Naming leftovers.** The shared recovery table is still called `TomatoRecoveryData`. The Farm scene file is `Envirornment.unity` (misspelt). The source folder is `Plant_Asstes`. Two setup menu items are numbered "6".
- **Visual issue observed on 22 Oct.** A gap in the procedural mountain ring is visible from some high angles (screenshot 07).
- **Slides are out of date.** The slides still report "16/16 tests" and, in slide 7, list seven plants "used now". The cited log line has since been overwritten.
- **Interaction parity.** Leaf rotation is implemented for mouse drag. `Leaf_Display.md` does not describe a controller or hand equivalent for rotating the leaf.

### 10.4 Suggested next steps
1. **Commit the leaf display work** in logical commits: data and assets, scripts and shader, tests, docs. Decide how to store `Assets/Leaf_Assets` (Git LFS or external storage).
2. **Re-run all EditMode tests** after committing and record the result and date in the docs. 42 test cases are expected.
3. **Rebuild the Quest APK and the Windows player** from the current state, and record the size, date and any build warnings.
4. **Test on a headset** (Quest 2 first, then Quest 3 and PC VR): controller ray, teleport, snap turn, hand pinch and poke, scene transitions, text legibility and the frame rate in both environments, using OVR Metrics or the Profiler. Tick the boxes in `Master_Prompt.md` section 10.
5. **Add controller and hand rotation** for the Lab leaf (for example, grab-to-rotate), so the leaf inspection does not depend on a mouse.
6. **Profile triangle and draw-call budgets** on Quest 2, especially the Apple, Grape and Peach near meshes and the 80k-triangle Grape Leaf Blight leaf. Lower them if the 72 FPS target is missed.
7. **Update the presentation** (`Presentation_Raw_Slides.md`, `Presentation_Beamer.tex`) with nine plants, 42 tests, the disease gallery, the leaf display and the screenshots in `Docs/Screenshots`.
8. **Small clean-ups:** fix the mountain-ring gap, renumber the duplicate "6" menu item, and consider renaming `TomatoRecoveryData`.
9. **Future scope** (from the slides): a real disease classifier (the `com.unity.ai.inference` package is already installed), interactive treatment choices reviewed with agricultural experts, and a small learner usability study.
