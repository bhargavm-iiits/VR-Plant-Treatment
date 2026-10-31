# Step-by-step implementation plan: BTP plant disease VR experience

## 1. Reference images and source rules

Copy these three images into `Docs/References` and embed them in `Docs/Master_Prompt.md`. Use them for the roles below.

**Plant list — authoritative for requested species.** The screenshot names Apple, Bell Pepper, Cherry, Corn (Maize), Grape, Peach, Potato, Strawberry, and Tomato.

![Plant types reference](<C:/Users/Bhargav/OneDrive/Pictures/Screenshots/Screenshot 2026-09-23 220836.png>)

**Farm — visual reference for the starting environment.** Recreate its sunny field, raised rows, central walking path, fence, trees, mountains, water tower, and visible signs of unhealthy foliage. The existing source file is `C:\Users\Bhargav\Downloads\farming_land.png`; the previously stated `farming\_land.png` path does not exist.

![Farm environment reference](C:/Users/Bhargav/Downloads/farming_land.png)

**Lab — visual reference for the second environment and interface.** Translate its central plant display, diseased/healthy comparison, dark translucent panels, cyan highlights, timeline, and metrics into spatial VR controls. Chili, Cucumber, treatment text, and percentages in this image are examples, not additional plant or scientific data requirements.

![Lab interface reference](C:/Users/Bhargav/Downloads/lab.png)

## 2. Establish the Unity and XR foundation

1. Keep `SampleScene.unity` intact and create `XRBootstrap.unity`, `Farm.unity`, and `Lab.unity`. Make the bootstrap scene first in Build Settings; it loads the farm by default.
2. Add Unity-compatible XR Plug-in Management, OpenXR, XR Interaction Toolkit, XR Hands, and glTFast packages. The project currently has none of the XR packages, and its `.glb` files currently use Unity’s default importer. [Unity Quest setup](https://docs.unity3d.com/6000.5/Documentation/Manual/xr-meta-quest-build-profile.html) and [glTFast editor import](https://github.com/Unity-Technologies/com.unity.cloud.gltfast/blob/main/Packages/com.unity.cloud.gltfast/Documentation~/ImportEditor.md) are the setup references.
3. Configure OpenXR for Quest Android and Windows PC VR. Use the Quest build profile with Vulkan, IL2CPP, ARM64, stereo instancing, and its required Android API level; the project’s existing generic Android minimum is API 26 and needs Quest-specific configuration.
4. Put one persistent XR Origin, input manager, XR UI input module, fade overlay, selection state, and environment controller in the bootstrap scene. Use controller rays, teleportation, and 30° snap turning. Add Quest hand ray/pinch and near-poke interaction, with controllers available whenever tracking is lost. PC hand controls activate only when its OpenXR runtime supplies hands; controller interaction remains fully functional.
5. Disable smooth movement by default. Provide teleport points and a recenter control so users can navigate while seated or standing.

## 3. Prepare the supplied plant assets

Create optimized, correctly scaled URP prefabs from these existing asset locations under the exact folder name `Assets/Plant_Asstes`:

| Plant | Source |
|---|---|
| Apple | `Apple_new/Meshy_AI_apple_tree_3d_0923203849_image-to-3d-texture_fbx/` |
| Bell Pepper | `Bell Pepper/Meshy_AI_Colorful_Bell_Pepper__0923184552_texture_fbx/` |
| Cherry | `Cherry/fbx/Cherry1.fbx` |
| Corn | `maize_corn_plant.glb` |
| Potato | `potato-plants-collection/source/Potato_plants_Collection.fbx` |
| Strawberry | `Strawberry_1/Meshy_AI_strawberry_plant_0923200556_texture_fbx/` |
| Tomato | `Tomato/Meshy_AI_Tomato_Plant_0923194145_texture_fbx/` |

For each prefab, check scale, pivot, textures, transparency, colliders, and URP materials; create lower-detail versions for repeated farm plants. Use `Tomato_asset`’s single-stem model for the lab close-up if its imported geometry is cleaner than the farm model. Import Corn through glTFast and confirm it becomes a usable Unity prefab.

Show these **seven** plants in the selector. Record Grape and Peach as missing assets in the documentation and hide them from the live selector until suitable models are supplied. Build lightweight farm and lab structures with Unity geometry and materials. Use the 468 MB `sci-fi_lab.glb` only as a source for a substantially optimized, visually suitable derivative; do not place the raw model in the Quest scenes.

## 4. Build the farm as the default scene

1. Create a compact, life-sized field with a central teleportable aisle and raised soil beds. Place low-growing crops in rows; position Apple and Cherry trees along an orchard edge and Corn where its height will not block the route.
2. Match the reference’s lighting and landmarks: blue sky, warm daylight, distant terrain, timber fence, notice board, and water tower. Keep the central path and entry controls immediately visible at spawn.
3. Give each of the seven plant types a labeled, selectable specimen. Controller ray or hand pinch selects it, adds a clear highlight, and opens a small world-space name card. Save its plant ID as the current selection.
4. Put a prominent **Enter Lab** button on the entry notice board and keep it reachable with both controller and hand input. If the user enters without selecting a plant, select Tomato by default.
5. Add teleport surfaces only on safe paths and viewing points; prevent landing inside plant beds or scenery.

## 5. Build the lab and its VR interface

1. Create an airy glasshouse/lab with a central plant pedestal, mountain or garden backdrop, clean structural frames, and cool lighting. Use the reference palette: charcoal translucent panels, white text, cyan selection states, and restrained red/green health cues.
2. Display the selected plant at the pedestal. Arrange world-space controls around it within comfortable viewing and reach distance. Avoid a face-locked, full-screen copy of the reference UI.
3. Provide **Plant**, **Recovery Demo**, and **Plant Information** panels. The first switches among the seven available species; the second provides the Tomato demo; the third shows concise species identity and an asset-backed 3D view.
4. Add a **Return to Farm** control in a fixed, easy-to-find location. The selected plant persists when switching environments.
5. Make controls usable by controller ray and Quest hand ray/pinch or near poke. Give buttons hover, press, and selected feedback. Make panel text legible at normal headset viewing distance.

## 6. Implement one explicit Tomato recovery demo

Create a local, clearly labeled **illustrative prototype** for Tomato/Early Blight. It demonstrates the interaction and visual progression; it does not claim to diagnose plants or recommend treatment.

- Use a six-week timeline with Previous, Next, direct week selection, and Reset controls. Mark Week 3 as “Demo treatment applied.”
- Show a diseased/healthy comparison around the central pedestal, separated by a cyan visual marker. Update leaf appearance across weeks using leaf-specific material variants or a derived leaf mask; keep fruit, stems, and soil unaffected.
- Use fixed example metrics so UI behavior is reproducible:

| Week | Leaf health | Chlorophyll | Disease severity |
|---|---:|---:|---:|
| 1 | 25% | 30% | 85% |
| 2 | 40% | 45% | 70% |
| 3 | 65% | 70% | 25% |
| 4 | 75% | 80% | 18% |
| 5 | 88% | 90% | 8% |
| 6 | 96% | 96% | 2% |

For the six other selectable plants, show their model and information panel. Replace the recovery metrics with a clear “Tomato demo only” state and a **View Tomato Demo** button; do not fabricate disease histories for them.

## 7. Connect scenes and state

1. Define a seven-item plant catalog containing stable plant ID, display name, farm prefab, lab prefab, and whether the Tomato recovery demo is available.
2. Let `SelectPlant(plantId)` update the shared selection and both scenes’ UI. Let `SwitchEnvironment(Farm|Lab)` fade out, unload the old environment, load the target environment, place the XR Origin at its spawn anchor, and fade in.
3. Keep one XR Origin throughout the session to avoid duplicate cameras or input modules. Reset only the Tomato demo when **Reset** is pressed; returning to the farm keeps the plant selection.
4. Handle missing model references with a visible setup error during development rather than silently showing the wrong species.

## 8. Optimize and verify

- Profile on **Quest 2 first**; use its result as the baseline for foliage density, texture sizes, shadows, draw calls, and memory. Allow higher quality profiles for Quest 3 and PC VR. Maintain a stable 72 FPS on Quest 2 in both environments.
- Test the complete journey: farm launch → select each available plant → enter lab → view selected model → run and reset Tomato weeks → return to farm. Confirm Grape and Peach never appear as finished choices.
- Test controller interaction, teleport, snap turn, and scene transitions on Quest 2, Quest 3, and Windows PC VR. Test hand selection and UI on Quest standalone; use controller fallback on PC because [Quest hand tracking over Link on Windows is documented for Unity Editor development](https://developers.meta.com/horizon/documentation/unity/unity-handtracking-overview/).
- Confirm both builds compile, all models and materials render, the Corn GLB imports correctly, world-space text is readable, and no transition causes a camera jump or duplicate XR rig.

## 9. Deliver the project brief

Save this implementation specification as `D:\Unity Projects\Plant Disease\BTP_Project\Docs\Master_Prompt.md`, with relative embeds for the three copied images in `Docs/References`. Include the asset map, scene flow, interaction rules, demo-data label, build targets, and acceptance checks so a Unity developer can implement it without choosing scope or inventing missing assets.
