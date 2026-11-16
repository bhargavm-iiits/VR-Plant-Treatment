# Master prompt for building Quest controller interactions

Copy the prompt below into a Codex session opened at the Unity project root. Give that session the [project skill](skills/btp-quest-controller-interactions/SKILL.md) as well.

---

Implement and verify end-to-end Meta Quest Touch controller interaction for this Unity project. Use `Docs/skills/btp-quest-controller-interactions/SKILL.md` as the project-specific implementation guide. Use Meta Quest 2 as the standalone Android hardware baseline and make both left and right controllers usable. The three gameplay scenes are `Assets/Scenes/Envirornment.unity` (Farm), `Assets/Scenes/Lab.unity` (Visiting Lab), and `Assets/Scenes/Treatment.unity` (Treatment Lab). Preserve the exact scene paths and GUIDs. Start through `Assets/Scenes/XRBootstrap.unity` and use the existing additive environment transitions. Keep the current scene art, plant assets, desktop input, Quest hand tracking, and saved learner progress functional.

## Start with the actual project

Check the working tree, Unity version and installed package versions. Inventory the actual buttons, interactables, canvases, prefabs, builder scripts, binding assets and data-driven actions for each scene. For each interaction, record its game object or prefab, input, existing handler, and required result. Fix existing wiring before creating replacement components. Keep the inventory as verification evidence. Implement and test the changes in the same session.

## Shared controller behavior

| Input | Player action |
|---|---|
| Point and index trigger | Click a world-space button, choose a plant or lesion marker, or select an unheld inspection target. |
| Grip in reach | Grab or release a physical tool or specimen handle. |
| Index trigger while gripping a tool | Operate it. Squeeze to close shears, hold to spray and release to stop, or confirm a tool action that needs an activation. |
| Grip and move or tilt | Rotate a specimen, place a carried object, turn a physical control, or pour at the designated soil target. |
| Existing thumbstick locomotion | Teleport on supported surfaces and snap-turn. Keep the project's 30-degree snap-turn default. |

Keep menu, recenter and return controls reachable from the scene's normal player position. Route each action to one handler. Held-tool activation, UI click, world selection and teleport must arbitrate competing candidates so the same press only does one thing. Provide a valid/invalid affordance and feedback for UI, selection and tool use. Add brief haptic feedback for accepted interactions where the controller supports it. Release transient interaction state on tracking loss, scene exit, or case changes. Preserve the project’s mouse and tracked-hand paths where they already work.

## Farm — `Assets/Scenes/Envirornment.unity`

- Teleport to valid paths or viewing points. Check blocked destinations, snap turn, and recenter.
- Ray-select a plant or disease specimen. Verify its selection ring, name card, disease gallery and case identity. Exercise the available selectors and clear stale selections when switching specimens.
- Operate the timeline console, including its authored week and playback controls. Verify that the selected specimen and console reflect the same timeline state.
- Operate irrigation through the existing control. Confirm that a single press updates the button, indicators and irrigation state once. If adding a direct grip interaction for the valve, route it through the same state controller.
- Enter the Lab using the normal scene control. Carry the selected crop and disease into the Lab.

## Visiting Lab — `Assets/Scenes/Lab.unity`

- Use controllers to choose crops and disease variants, inspect the specimen and read its information. Verify that database/catalog identity and the displayed leaf agree after every change.
- Add or repair a graspable, bounded rotation handle for inspecting the front and underside of the enlarged leaf. Grip rotates the display; release ends the manipulation. Keep existing viewer alignment and desktop drag working, and keep the selected leaf stable when its material or variant updates.
- Operate recovery weeks, comparison and reset controls where present. Verify the displayed timepoint, visual updates, and metrics.
- Use recenter, return to Farm, and enter Treatment. Pass the active crop and disease to Treatment through the shared selection state.

## Treatment Lab — `Assets/Scenes/Treatment.unity`

Make the existing button-based case picker, learner picker, diagnosis, adjustments, apply, monitoring, final assessment, results, recenter, and return flow fully usable by controllers. Add a clearly named **Hands-on treatment** entry for direct tool use. Give the player an explicit way back to the existing button-based flow. Keep the two exercises' interaction state isolated so a physical action cannot also submit a button-based assessment.

- Read cases, tools, actions, targets, prerequisites, branches and outcomes from `Assets/_Project/Data/Treatment/treatment_content.sql` through the existing treatment services and lesson engine. Audit all authored action types, including inspection, choices, observation, advance, collection, sanitation, containment and replacement. Use controller-accessible prompts or world actions according to each step. Provide a route to finish and review the result for every valid case.
- **Pruning shears.** Grip to pick up, align the blades with the marked cut area, and pull trigger. Submit the cut only when the authored target and prerequisites are satisfied. Preserve healthy tissue and the petiole in cases whose cut step specifies that. Carry or select the removed material for disposal and run tool cleaning when required.
- **Hand sprayer.** Grip the bottle, aim from its nozzle, and hold trigger to spray. Use **Apply targeted spray** as the shared action label and **Training spray** on the prop. The active step decides what is highlighted and eligible. Test healthy tissue, a mildew patch, a mite hotspot, and healthy neighboring foliage where each is authored. Stop spray immediately on release, loss of the tool, tracking interruption, or transition. Prevent double-counting coverage and give feedback for a wrong target.
- **Other lesson tools and fixtures.** Wire the database-authored magnifier, water wand, biological-control release, vents and irrigation, plant waste and sanitation, containment, mesh, monitoring cards and clean transplants. Show only the tools and targets eligible for the current case or branch. Validate target type and prerequisites before changing visuals or recording progress.
- **Requested nutrient activities.** Provide controller-driven granular NPK scooping, liquid fertilizer pouring and compost mixing as educational soil activities. Gate NPK and liquid fertilizer behind an applicable nutrient-support case or explicit authored exercise. Add an explicit pre-plant exercise for compost if the database has no matching action. Identify these new interactions in the content layer, use soil targets, and keep their completion separate from disease treatment. Preserve existing case IDs, outcomes and saved records. Use illustrative training units and no real product doses.
- Separate preview feedback from completion. Reuse `TreatmentSession` validation and existing save/result paths for authored steps. Give actionable feedback for missing prerequisites, an unavailable case or an unsuitable target. On case change, reset held tools, highlights and transient effects. On resume, restore the correct case, step and accepted progress.

## Verification and handback

1. Compile with the project's Unity version. Run existing Edit Mode suites covering treatment, Farm timeline and irrigation, leaf display, and shared state. Add focused regression coverage when existing tests miss a new input or state invariant. Update assertions that intentionally describe the previous mode-only design and retain their unrelated coverage.
2. Audit every authored case with the lesson validator and verify that its offered actions, tool and target keys resolve. Exercise representative complete lessons for targeted spraying, cutting and sanitation, a pest route, containment and replacement, and each new nutrient exercise. Test a wrong target, missing prerequisite, interrupted spray, repeated press, pause/resume, and active tool during exit.
3. Run from `XRBootstrap` through Farm → Lab → Treatment → Lab → Farm, then repeat. Confirm one XR rig, one XR interaction manager and one active XR UI event system, with selection and learner progress intact. Test both input hands, UI/world overlap, multiple device inputs and scene-transition cleanup. Use the project's simulator for wiring where available.
4. Build and test Android on a Quest 2 when the headset is available. Verify tracking, grip and trigger separation, thumbsticks, readability and reach, standby/resume and a full treatment loop. Report simulator, automated, build and physical-headset results separately. List any device checks still pending.
5. Deliver a brief per-scene control map, the file paths changed, the tests run and their results, and concise steps to reproduce the end-to-end flow.
