---
name: btp-quest-controller-interactions
description: Implement or debug Meta Quest Touch controller interactions in this Unity project's Farm, Visiting Lab, and Treatment Lab, including OpenXR bindings, world-space UI, tools, and cross-scene verification.
---

# BTP Quest controller interactions

Read `Docs/VR_Controller_Master_Prompt.md` for the full rollout and scene acceptance checklist. For a targeted request, use the relevant section and keep work within that request's scope.

## Find the active wiring

- Read `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, `ProjectSettings/EditorBuildSettings.asset`, and the OpenXR settings asset. At authoring time the repo uses Unity `6000.5.5f1`, XR Interaction Toolkit `3.5.1`, OpenXR `1.17.1`, and the Input System. Use the versions currently installed in the repo.
- `Assets/Scenes/XRBootstrap.unity` owns the persistent XRI Hands Interaction Demo-derived rig, interaction manager and `XRUIInputModule`. `EnvironmentController` loads the other scenes additively, resolves `SpawnAnchor`, and owns transitions. `SelectionState` carries crop and disease. The Farm filename is `Envirornment.unity`.
- Read `Assets/_Project/Scripts/Editor/BootstrapSceneBuilder.cs`, `XRProjectSetup.cs`, `XRSceneWiring.cs`, `UIFactory.cs`, and the `FarmSceneBuilder`, `LabSceneBuilder`, `TreatmentSceneBuilder` and `ManualTreatmentSceneBuilder` classes under the same directory before altering authoring. Verify actual scene and prefab overrides as well as source builders. Some builders replace scene roots. Use targeted edits where a full rebuild could discard unrelated authored work, and update the builder or reusable prefab when it owns the changed wiring.
- Trace the actual scene handlers. On the Farm, start with `SelectablePlant`, `DiseaseGalleryController`, `FarmTimelineConsole`, and `FarmIrrigationController`. In the Lab, use `LabPlantDisplay`, `LeafDiseaseSelector`, `LeafMouseRotation`, and `RecoveryDemoPanel`. In Treatment, start with `ManualTreatmentController`, `TreatmentController`, `TreatmentSession`, `TreatmentTarget`, `TreatmentToolPickup` and `TreatmentServices`. These live under `Assets/_Project/Scripts/Runtime/`.

## Keep select and activate distinct

- `BootstrapSceneBuilder.SelectWithTrigger` currently copies controller activate action references into `NearFarInteractor` select actions. Preserve trigger for ray selection and world-space UI while introducing a grip-based near-grab path. Use interactor roles or filters and matching XRI interaction layers so an object grab has a separate select binding and an owned trigger activation. Verify both hands in the loaded rig. An interaction-layer mask and a physics-layer mask control different stages of targeting, so check both.
- Use the existing world-space canvases with `TrackedDeviceGraphicRaycaster` and the shared `XRUIInputModule`. Trace button operations to their current callbacks. Use `XRSimpleInteractable` for select-only world objects and XRI grab behavior with sensible attach transforms and colliders for tools that are physically held. Suppress unrelated ray/UI activation when a held tool owns the trigger. Keep locomotion rays limited to valid destinations and cancel pending interactions as input mode or scene changes.
- Prefer existing action references and local bindings over hardcoded device paths. Check Oculus Touch Controller Profile on Android and preserve tracked-hand and desktop routes. Consult the installed package implementation and [Unity XRI grab documentation](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.5/manual/xr-grab-interactable.html), [XR canvas setup](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.5/manual/ui-setup.html), and [OpenXR Oculus Touch profile](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.17/manual/features/oculustouchcontrollerprofile.html) when resolving version-specific behavior.

## Treat SQLite as the source of lesson rules

- Inspect `Assets/_Project/Data/Treatment/treatment_content.sql` with in-memory SQLite. Start with `cases`, `steps`, `case_tools`, `case_targets`, `environment_fixtures`, `step_prereqs`, `step_options`, `methods`, `treatment_controls`, and `control_rules`. The checked-in SQL had 58 cases across 19 crops when this skill was authored. Query the current file for coverage instead of assuming those totals or using the older counts in documentation.
- The scene currently exposes button-based Manual treatment. It hides the step-lesson tools and targets in that mode. `TreatmentToolPickup` and `TreatmentTarget` use `XRSimpleInteractable` selection. The current pass-through pickup presents a held visual, so a visible tool following the controller alone does not prove direct grip and trigger work. For a hands-on build, wire an explicit entry and use `TreatmentSession.TryPerform` and the existing controller's result and persistence path for accepted authored actions. Keep authored prerequisites, option branches and result types intact.
- Resolve the spray target from the active step. The data includes healthy tissue, lesion spots, mite hotspots and neighboring hosts. Treat NPK, liquid feed and compost as nutrient support or soil preparation. Query whether a matching action exists before wiring it, and author a distinct exercise where required. Keep persistence compatible with older saves and make refused interactions explain how to progress.

## Verify from the entry point

Run relevant existing suites in `Assets/_Project/Tests/EditMode/` and exercise the full scene loop through `XRBootstrap`. Check that rig, UI and transitions still work after going around the loop twice. Compare business state and visible feedback, especially for a rejected tool action and for grip, trigger, and UI contention. Verify any scene/prefab references after saving. Test both controllers, tracking loss, standby/resume, and the treatment loop on Quest 2 when available. Label physical-headset checks pending when verification was limited to the Editor or simulator.
