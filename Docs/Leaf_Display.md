# Lab leaf inspection

The central lab display now shows individual supplied leaves from `Assets/Leaf_Assets`, rather than the full plant. The Farm and the smaller species-information preview retain their plant models.

## Interaction

1. Select a plant on the left. Its default disease leaf appears on the pedestal.
2. Select one of that plant's disease buttons above the leaf. The actual 3D model and reference image change together. The right panel's previous/next disease controls do the same.
3. Choose Weeks 1–6 below the leaf. Weeks 1–5 preserve the supplied diseased texture on the left and animate the current state on the right. At Week 6 the **whole leaf** animates to a healthy appearance; the divider and comparison labels disappear, and the metrics finish at **100% leaf health, 100% chlorophyll and 0% severity**. Recovery masks fade affected patches at different rates while restoring pigmentation. Each week change takes about 3 seconds: a healing front sweeps out from the divider (near the midrib) to the leaf edge, with a soft green glow on the moving front while healing (none when going back, none once settled). The right-hand label reads "Healing… → Week N" during the animation, and the metric bars count in step. Reset returns to Week 1 in about 1.5 seconds. Tune `transitionSeconds`, `resetSeconds` and `healingGlow` on `LeafRecoveryVisual`. The selected week remains shared across plants and scene transitions, as before.

The inspection frame, labels and lab controls face the tracked viewer as the viewer changes position. Hold the left mouse button on the leaf and drag horizontally to turn it or vertically to tilt it (limited to 65 degrees each way). Releasing the button stops rotation immediately; there is no automatic rotation or inertia. Dragging must start on the specimen, and UI clicks do not rotate it. Right-drag retains the existing camera look controls. The controls stay readable and the comparison remains aligned with the viewer while the leaf turns. A newly selected specimen starts from its corrected front orientation. Plant selection stays on the viewer's left. Leaf fitting uses inspection-space bounds so moving behind or beside the pedestal does not reverse the comparison or change model scale.

There are nine plant species and 24 condition-specific leaf prefabs. Supplied source meshes and textures are preserved. Most display meshes are approximately 12,000 triangles; Grape Leaf Blight uses about 80,000 after welding coincident geometry, to retain its folded surface and UV detail. Display texture copies are capped at 2048 pixels on PC and 1024 on Android. Per-model orientation corrections keep reversed single leaves stem-down and turn sideways grape leaves upright.

The assets provide diseased leaves only. A healthy colour atlas is reconstructed from green regions of each supplied texture, with restrained local detail preservation. The shader uses the supplied vein normal maps, roughness maps, physically based lighting and a small thin-leaf transmission term. Geometry is preserved during recovery. The endpoint remains a visual reconstruction, not a measured healthy leaf or scientific treatment response. The existing shared six-week example values and prototype disclaimer remain in use.

## Recreate the assets and layout

- Run `python Tools/prepare_leaf_assets.py` to optimize the supplied FBX meshes with the installed Blender 5.2. Outputs go to `Assets/_Project/Models/Leaves`; the source-to-condition map is `Tools/leaf_manifest.json`. Run `python Tools/prepare_leaf_surface.py` to bake the surface maps and apply the orientation map from `Tools/leaf_orientations.json`.
- In Unity, run **BTP > Setup > 6 - Prepare Leaf Display Assets** to create the materials, display prefabs, texture copies and `LeafCatalog.asset`.
- Run **BTP > Setup > 7 - Update Lab Leaf Display** to update the existing Lab scene. The normal Lab scene generator also applies the leaf layout.

## Verification (14 October 2026)

- Scripts compiled with zero reported errors or warnings.
- All 42 EditMode tests passed. Mouse rotation tests cover offset model centres, stopping on release, no idle rotation, UI/empty-space clicks and tilt limits, alongside the existing leaf-catalog and recovery checks.
- The drag/release integration probe passed for all 24 conditions, including fixed centres/controls, comparison-plane tracking and healthy Week 6. `Tools/eval/verify_leaf_rotation.cs` also records a live idle measurement in `Tools/out/previews/leaf_rotation/result.txt`.
- Play-mode integration checked all 24 conditions, orientation corrections, visible disease choices, model fitting, direct disease selection, side navigation, Week 6 and Reset. A real-time forward/reverse animation check passed; measurements and a looping GIF are in `Tools/out/previews/recovery_motion/`.
- The latest endpoint/facing update passed 96 additional checks (24 conditions from four viewpoints). Rendered front/rear Week 1 and Week 6 comparisons are in `Tools/out/previews/leaf_front_back/`. These confirm readable controls and a healthy whole leaf at Week 6.
- Inspected a contact sheet of all 24 corrected leaf orientations and rendered comparisons for Tomato, Apple, Corn and Grape. Preview files are under `Tools/out/previews/leaves/`; the orientation sheet is `Tools/out/previews/leaf_orientation_sheet.png`.
- No Quest APK or Windows player build was created. Headset interaction and device performance have not been tested in this change.
