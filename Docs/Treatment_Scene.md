# Treatment Scene and Database

How the **Treatment Lab** (`Assets/Scenes/Treatment.unity`) works: what the scene contains, how a lesson runs, and the two SQLite databases behind it. Its UI is the button-only **Manual treatment** mode (section 10). The older step-lesson floating windows (TaskPanel, CaseNotesPanel, the mode switch and "Reset this case") have been **removed from the scene**. The step-lesson engine, its 24 lessons in SQL, its saved progress and its logic tests remain, and sections 1-9 describe that engine.

> **Related documents:**
> - `Docs/treatment.md` is the original master prompt (the requirements).
> - `Docs/Treatment_Implementation.md` is the build log.
> - `Docs/DataBase_Plan.md` holds the disease-management research.

## 1. Overview

- 24 lessons, one per plant/disease pair (9 plants), in 4 themed rooms: greenhouse, orchard, vineyard, field.
- Database-driven: every instruction, rule, choice, feedback message and outcome comes from SQLite. The C# code contains no disease-specific advice.
- Outcomes: Managed (18), Pest suppressed (1: tomato spider mites), Replaced (5: tomato late blight, potato late blight, both tomato viruses, grape Esca). Dead tissue never turns green; replacement cases never cure the original.
- Progress is saved after every accepted action and survives restarts. The Farm and visiting Lab show the saved result.
- Entry: Lab "Enter Treatment Lab" button, or the desktop T key. Exit: "Return to Visiting Lab". The selected case is shared via SelectionState; the last case is restored from settings.last_case. A fresh install starts on tomato/early_blight.

## 2. Editor menu

| Menu | Purpose |
|---|---|
| BTP/Treatment/1 - Import Content Database | Imports treatment_content.sql as a SQLiteAsset |
| BTP/Treatment/2 - Build Treatment Assets | Builds the room, fixture, leaf, target and tool prefabs; fills TreatmentAssetCatalog.asset |
| BTP/Treatment/3 - Build Treatment Scene | Rebuilds Treatment.unity |
| BTP/Treatment/4 - Connect Bootstrap, Lab and Farm | Adds the Lab button and the Farm/Lab status labels |
| BTP/Treatment/5 - Bake Leaf Vein Paths (Manual Treatment) | Adds vein paths to the existing leaf specimen prefabs (for the manual mode's particles); step 2 also bakes them |

## 3. Scene

- Hierarchy: Lighting, Scenery, Treatment (TreatmentController, ManualTreatmentController, ManualTreatmentView, LeafRecoveryController, LeafParticleController), EnvironmentRoot (room prefab at runtime), Interface (NavigationPanel with Return / Recenter view, CaseSelectorPanel, and the manual panels of 10.4), ToolRack (hidden), Navigation/SpawnAnchor, BootstrapGuard. The old TaskPanel and CaseNotesPanel are no longer built; TreatmentController runs without a task panel.
- Rooms (cases.environment_key):
  - greenhouse: tomato x9, bell pepper, strawberry
  - orchard: apple x3, cherry, peach
  - vineyard: grape x3
  - field: corn x3, potato x2
- 13 fixtures per room. A fixture is shown when its module_key is null or appears in the case's case_modules:
  - always: disposal_bin (fx.disposal_bin), sanitation_station (fx.sanitation)
  - irrigation: irrigation_valve (fx.valve)
  - ventilation: vent_control (fx.vent), fan_control (fx.fan), humidity_display (fx.display)
  - risk: risk_card (fx.risk_card)
  - planning: planning_board (fx.board)
  - confirmation: lab_result (fx.lab_result)
  - neighbours: neighbour_host (fx.neighbour)
  - containment: containment_area (fx.containment)
  - replacement: clean_pot (fx.clean_pot)
  - mesh: mesh_frame (fx.mesh_frame)
- Specimen: the case's 3D leaf, about 0.7 m, on an inspection holder. LeafAnchors places targets by texture analysis: lesion_spot (least green), healthy_tissue (greenest), leaf_underside, petiole. TreatmentTarget State_<state> children show the saved state. show_after_step / hide_after_step time targets, for example the new_leaf after the simulated wait.
- Tools (15):
  - used by lessons: magnifier, shears (contaminates), sprayer (category only; no product or dose), water_wand, predator_release, containment_bag, transplant, insect_mesh, sticky_card
  - defined but on no case rack: drip_emitter, stake, mulch, rake, trellis_tie, spade

## 4. Lesson flow

- Phases: Inspect (99 steps), Prepare (53), Treat (87), Recheck (68).
- Interaction: buttons by default (TreatmentButtonPlanner builds them; TreatmentActionAnimator plays a 1.7 s tool flight). The direct alternative is tool then target via trigger, pinch, poke, ray or click, with cyan rings on valid targets. Both paths call TreatmentSession.TryPerform(ActionContext).
- Rules:
  - wrong tool: steps.wrong_tool_feedback
  - wrong target: steps.wrong_target_feedback
  - too early: steps.prerequisite_feedback
  - wrong choice: step_options.feedback
  - a contaminated tool must be cleaned before a requires_clean_tool step
  - dispose needs carried material
  - each target counts once per step
  - required_count shows "n of m done"
- Routes: a correct option may set Progress.Route; steps with that route apply only on it (for example, early blight protect/monitor, spider mites predator/contact).
- Example tomato/early_blight (13 steps, 12 per learner): inspect lesions x2 (magnifier) > underside > confirm (choose) > cut x2 (shears, contaminates) > dispose > clean tools > drip valve > assess (protect | monitor) > spray healthy x2 [protect] or mark monitoring [monitor] > advance > observe new leaf > observe old lesions. Outcome: Managed.

## 5. The 24 cases (steps / targets / methods)

| Case | Steps | Targets | Methods | Outcome |
|---|---|---|---|---|
| tomato/early_blight | 13 | 9 | 4 | Managed |
| tomato/bacterial_spot | 13 | 9 | 3 | Managed |
| tomato/late_blight | 16 | 7 | 3 | Replaced |
| tomato/leaf_mold | 16 | 9 | 3 | Managed |
| tomato/septoria_leaf_spot | 13 | 9 | 3 | Managed |
| tomato/spider_mites | 11 | 10 | 3 | PestSuppressed |
| tomato/target_spot | 14 | 9 | 3 | Managed |
| tomato/yellow_leaf_curl_virus | 11 | 8 | 3 | Replaced |
| tomato/mosaic_virus | 12 | 8 | 3 | Replaced |
| apple/apple_scab | 16 | 9 | 4 | Managed |
| apple/black_rot | 14 | 11 | 2 | Managed |
| apple/cedar_apple_rust | 12 | 12 | 3 | Managed |
| cherry/powdery_mildew | 12 | 9 | 2 | Managed |
| peach/bacterial_spot | 10 | 10 | 3 | Managed |
| bell_pepper/bacterial_spot | 15 | 10 | 3 | Managed |
| strawberry/leaf_scorch | 13 | 9 | 3 | Managed |
| corn/gray_leaf_spot | 10 | 9 | 3 | Managed |
| corn/common_rust | 11 | 9 | 3 | Managed |
| corn/northern_leaf_blight | 10 | 9 | 3 | Managed |
| potato/early_blight | 14 | 9 | 3 | Managed |
| potato/late_blight | 13 | 8 | 3 | Replaced |
| grape/black_rot | 16 | 11 | 3 | Managed |
| grape/esca | 10 | 8 | 2 | Replaced |
| grape/leaf_blight | 12 | 9 | 2 | Managed |

## 6. Databases (SQLite via com.gilzoide.sqlite-net 1.3.3; no server, works offline)

- Content: Assets/_Project/Data/Treatment/treatment_content.sql, imported as a read-only SQLiteAsset (SQLScriptDatabaseImporter) and referenced by TreatmentAssetCatalog. Read by SqliteTreatmentRepository (ITreatmentRepository).
- Progress: Application.persistentDataPath/treatment_progress.db, read/write, via SqliteProgressRepository (IProgressRepository). It is a separate file, so content updates never wipe progress.
- Both are owned by the TreatmentServices singleton on XRBootstrap, which caches cases and provides DescribeProgress().

### Content schema (content_version 2; per-case tables keyed by plant_id, disease_id)

| Table | Rows | Columns |
|---|---|---|
| content_info | 2 | content_version=2, default_case=tomato/early_blight |
| environments | 4 | environment_key, display_name, prefab_key, description |
| environment_fixtures | 52 | environment_key, target_id, target_tag, asset_key, slot, label, module_key |
| tools | 15 | tool_key, name, asset_key, description |
| cases | 24 | plant_name, disease_name, category, scenario, outcome CHECK(Managed, PestSuppressed, Replaced), environment_key, specimen_asset, specimen_scale, summary, result_text, content_version |
| case_modules | 51 | module_key |
| case_tools | 103 | tool_key |
| case_targets | 220 | target_id, target_tag, asset_key, material_key, slot, label, show_after_step, hide_after_step |
| methods | 70 | method_id, name, purpose, limitations |
| steps | 307 | step_id, ord, phase, title, instruction (pipe-separated bullets), action, tool_key, target_tag, required_count, route, method_id, checkpoint_id, contaminates, requires_clean_tool, success_text, wrong_tool_feedback, wrong_target_feedback, prerequisite_feedback |
| step_prereqs | 14 | step_id, requires_step_id |
| step_options | 117 | option_id, ord, label, correct, route, feedback |
| followups | 43 | checkpoint_id, prompt, finding, escalation |
| evidence | 27 | publisher, url, reviewed, region, limitations |

Actions by step count: inspect 84, choose 52, observe 43, advance 25, control 23, spray 19, clean_tool 14, dispose 13, cut 12, contain 5, remove_plant 5, plant 5, collect 4, place 2, release 1, irrigate 0 (handler only).

Evidence: University of Minnesota Extension 14, UC IPM 7, and one each from Clemson, Embrapa, Iowa State, NC State, Penn State and UF/IFAS.

### Progress schema (schema_version 1; version 2 adds the manual-mode tables, see 10.6)

| Table | Stores |
|---|---|
| settings | key/value: schema_version, last_case |
| case_progress | (plant_id, disease_id) PK: content_version, route, outcome, completed, updated_utc ticks |
| progress_steps | completed step_id |
| progress_step_targets | step_id, target_id counted |
| progress_targets | target_id, state |
| progress_equipment | equipment_key, state (tool:shears=contaminated, carried=ids, control tag=on) |
| progress_observations | checkpoint_id, finding |

- Save deletes and re-inserts the case's rows in one transaction, after every action, on case change, on pause or quit, and on exit.
- Reset deletes one case. On a content_version mismatch, only step IDs that still exist are kept.
- LatestForPlant() feeds the Farm labels.
- Only stable IDs are stored; the scene is rebuilt on load.

### Asset keys

The TreatmentAssetCatalog maps these keys to prefabs and materials only (no advice): env.*, fx.*, tool.*, leafspecimen.<plant>.<disease>, target.*, leafmat.*.

### Load and validate

TreatmentController.Load:
1. Save the previous case, drop the held tool, clear the room.
2. GetCase (cached, parameterised queries).
3. TreatmentCaseValidator checks:
   - names, outcome, environment, steps, methods with limitations, evidence URL
   - an observe step
   - unique IDs
   - prerequisites, routes and show/hide steps exist
   - action handlers exist
   - target tags are placed
   - tools are on the rack and defined
   - every cut/collect is followed by a dispose
   - no targeting of a removed plant
   - Replaced cases contain or remove the original, plant clean stock, and never spray the infected plant
   - asset keys bind in the catalog
4. If invalid: an "unavailable" panel with the reasons and Retry; no substitution.
5. Otherwise: load progress, create the session, build the room, specimen, targets and rack.

## 7. Scripts

- Runtime/Treatment: TreatmentServices, SqliteTreatmentRepository, SqliteProgressRepository, TreatmentContent, CaseKey, TreatmentCaseValidator, TreatmentSession, TreatmentController (about 930 lines), TreatmentButtonPlanner, TreatmentActionAnimator, TreatmentPanel, TreatmentCaseSelector, TreatmentTarget, TreatmentToolPickup, TreatmentSlot, LeafAnchors, TreatmentAssetCatalog, TreatmentStatusReadout.
- Editor: TreatmentAssetBuilder (Environments, Fixtures, Leaf, Targets, Tools), TreatmentSceneBuilder.
- Tests (EditMode): TreatmentTests (12), TreatmentButtonTests (5), ManualTreatmentTests (20).
- Manual treatment mode: see 10.7.

## 8. Editing content

1. Edit Tools/treatment_content_gen.py and run it, or edit the .sql file.
2. Bump content_version if step IDs changed.
3. Run BTP/Treatment/1 (and 2 if new asset keys were added).
4. Run the EditMode tests.

## 9. Limitations

- No Quest or Windows IL2CPP build has been tested with SQLite (link.xml preserves the row types).
- There is no physical grip-and-trigger tool use.
- The supplied shears, spray bottle and pipe models are unused.
- The sprayer never teaches products or doses.

## 10. Manual treatment mode

The Treatment Lab's button-only mode. The learner uses buttons only: they pick a crop and disease, read the disease information from the database, set each treatment level with **−** / **+**, apply the treatment, watch the same leaf respond, monitor it, and finish with a final assessment. A session lasts 1 minute and has 5 stages of 12 s each. It is an **educational simulation**: real infected tissue does not regenerate, and the levels are abstract 0-10 teaching scales, with no products, doses or concentrations.

**Update:** at the user's request the older step-lesson windows (TaskPanel, CaseNotesPanel), the Step lesson / Manual treatment switch and the step lesson's "Reset this case" button were removed, so Manual treatment is the lab's only mode. The step-lesson engine and its saved progress are kept. The scene test in TreatmentButtonTests now checks that the old windows are absent and the manual panels are wired.

### 10.1 Workflow

1. **Crop selection** and **disease selection**: the existing `TreatmentCaseSelector` on the CaseSelectorPanel (shared `SelectionState`).
2. **Database fetching**: `TreatmentDataManager.LoadManualCase` reads the case and its manual profile through `TreatmentServices` → `SqliteTreatmentRepository`, then runs `TreatmentCaseValidator.ValidateManual`. If the data is missing or invalid, the panel shows "Manual treatment unavailable" with the reasons. Another disease is never substituted.
3. **Disease information**: category, severity, summary, symptoms, management methods with their limitations, and the evidence publishers.
4. **Treatment recommendations**: each control shows its recommended range (from SQL) for learning. Nothing is pre-selected: every control starts below the range, marked "Not set yet".
5. **Manual adjustment**: one authored step per **−** / **+** press, clamped to the min and max. A press at a limit is accepted and says so.
6. **Apply treatment**: refused until every required control has been set. `TreatmentValidationManager` scores the levels from 0 to 1 against the recommendation and the combination rules. Feedback text comes from the database.
7. **Leaf recovery animation**: `LeafRecoveryController` moves the same leaf mesh and material towards the validated result over 3.5 s (gradual, and well inside one 12 s stage), with vein particles while it improves.
8. **Monitoring**: further adjustments and re-applies, plus **Check health**. At least one check is needed, and every pending change must be applied, before the final assessment.
9. **Final result**: initial vs final simulated health and active severity, the outcome, per-control feedback, the recovery note, the case's result text (on success) and the disclaimer.

### 10.2 Stages and timer

| Stage | Guidance window | Advances when |
|---|---|---|
| 1 Diagnosis | 0-12 s | "Diagnosis reviewed" (Disease information panel) |
| 2 Selection | 12-24 s | the first accepted **Apply treatment** |
| 3 Application | 24-36 s | "Continue to monitoring" (Progress panel) |
| 4 Monitoring | 36-48 s | "Proceed to final assessment" (needs ≥1 health check and no un-applied changes) |
| 5 Final assessment | 48-60 s | "Finish session" |

- The windows are guidance only. Stages move on the learner's actions, and the Progress panel says when the learner is behind the suggested stage.
- The length is two constants in `TreatmentTimer` (`SessionSeconds = 60`, `StageSeconds = SessionSeconds / 5`). The stage-chip labels ("(0-12 s)") and the clock ("0:00 / 1:00") are derived from them, so they cannot drift.
- At 1:00 the session moves to the final assessment. Applied treatments still count; an untreated session ends as "No treatment was applied".
- A saved unfinished session whose elapsed time is beyond the limit (e.g. saved under the earlier 10-minute length) is clamped to 1:00 on resume and continues in the final assessment.
- **The timer never changes the leaf.** Only a validated Apply sets a new recovery target; time only animates towards it (EditMode test `TimerAloneNeverChangesTheLeaf`).
- The timer pauses on application pause or focus loss (a Quest headset taken off) and resumes from the saved elapsed time after a restart.

### 10.3 Outcome-aware recovery

`LeafRecoveryModel.Evaluate(profile, outcome, valid, score)`:

- improvement = (score − 0.35) / 0.65, clamped to 0..1. It is 0 for invalid treatments and for every Replaced case.
- worsening = 1 for a blocked treatment (e.g. not contained), otherwise (0.35 − score) / 0.35, clamped to 0..1, × `max_worsen`.
- severity = initial × (1 − improvement × `max_severity_drop`) + worsening.
- health = initial + improvement × `max_health_gain` − worsening.
- Shader amounts = improvement × the per-case caps (`max_spot_fade` ≤ 0.8, `max_yellow_fade`, `max_green_gain`, `max_vein_gain`).

| Outcome | Leaf response to a good treatment |
|---|---|
| Managed (18) | Yellowing fades, green tissue and veins return, and brown spots dry, lighten, shrink at their edges and stop spreading. Spot cores never vanish, and health stays below 100%. |
| PestSuppressed (tomato/spider_mites) | Speckling stops spreading; a moderate improvement (lower caps). |
| Replaced (5) | No improvement: treatment cannot save the leaf. Correct containment only stops it worsening. Without containment (a blocking rule) it worsens, and the result explains containment plus replacement with clean stock. |

Poor choices (score below 0.35) give no improvement or worsening.

**Shader.** `LeafRecovery.shader` gains `_SpotFade`, `_YellowFade`, `_GreenGain`, `_VeinGain`, `_Worsen` and `_TreatGlow`, all defaulting to 0. At 0 the leaf renders exactly as before, so the Lab demo and the step lessons are unaffected. The masks come from the leaf's own textures:

- necrotic tissue: dark pixels × the damage channel (R) of `_RecoveryMap`
- chlorotic tissue: yellow-hue pixels
- veins: derived from the normal map's slope

`LeafRecoveryController` sets the amounts with a MaterialPropertyBlock on the specimen's own renderer. Detaching (leaving the mode) clears the block. There is no material swap and no healthy-leaf swap.

**Particles.** `LeafParticleController` emits up to 80 octahedron motes (8 triangles each, one draw call) along the vein segments in `LeafAnchors.Veins`. Those segments are a midrib plus side veins, baked in the editor by `TreatmentAssetBuilder.ComputeVeins` (menu 5, or as part of menu 2). Specimens built without baked veins fall back to segments from the petiole towards the anchor points.

### 10.4 Panels (Interface local space; viewer at the origin, leaf ahead at 0°)

| # | Panel | Where | Contents |
|---|---|---|---|
| 1 | CaseSelectorPanel (existing, 940 mm tall) | −64° | crops, diseases; **Learner**: < name > and + New |
| 2 | DiseaseInfoPanel | −30° | title, database status, information; Start / Resume / Diagnosis reviewed; Start over |
| 3 | TreatmentAdjustmentPanel | +36° | up to 5 control rows (name, description, current, recommended, range, status, − / +), Apply / Re-apply, feedback |
| 4 | TreatmentProgressPanel | +80° | clock (elapsed / 1:00, remaining, paused), 5 stage chips with windows and "(done)", guidance, the stage button, action history (consecutive identical refused presses shown once with "×N"; every press is still stored in `manual_actions`) |
| 5 | LeafHealthPanel | above the leaf (0°, 1.95 m, tilted) | health and active-severity bars, Check health (Monitoring only), disclaimer |
| 6 | FinalResultsPanel | +36° (replaces panel 3 when the session is complete) | result text; Start a new session |

- The NavigationPanel (−98°) keeps Return to Visiting Lab and Recenter view.
- The tool rack, lesson targets, cut marks and containment bag are hidden, and the specimen stands on its holder.

**Learners.** "+ New" creates "Learner N" (a VR headset has no keyboard). < and > cycle through the learners. The last learner (`settings.last_learner`) and the last mode (`settings.treatment_mode`) are remembered. If no learner exists when a session starts, "Learner 1" is created.

### 10.5 New content tables (treatment_content.sql; additions only)

| Table | Rows | Columns |
|---|---|---|
| case_profile | 24 | plant_id, disease_id (PK), description, symptoms (pipe-separated), initial_severity, initial_health, max_severity_drop, max_health_gain, max_worsen, max_spot_fade, max_yellow_fade, max_green_gain, max_vein_gain, recovery_note |
| treatment_controls | 69 | plant_id, disease_id, control_id (PK), ord, method_id (FK → methods), name, description, min_value, max_value, step_value, start_value, rec_min, rec_max, weight, required, feedback_low, feedback_high, feedback_ok |
| control_rules | 16 | plant_id, disease_id, rule_id (PK), control_a, a_min, a_max, control_b (nullable), b_min, b_max, penalty, blocks, feedback |

- Every control is derived from one of the case's own `methods` rows. Its in-range feedback quotes that method's limitation.
- `content_version` stays 2, because no step IDs changed.
- The generator `Tools/treatment_content_gen.py` reproduces the file byte for byte. Against the previous file, it adds 176 lines and changes none: 13 lines of schema and 163 of data.
- **All values are illustrative and need expert review** (marked in SQL comments).

Per-case controls (start → recommended range; weight):

| Case | Outcome | Severity / health | Controls (start -> recommended; * = optional) | Rules |
|---|---|---|---|---|
| apple/apple_scab | Managed | 0.3 / 0.74 (gain cap 0.16) | sanitation 2->6-8 (w1); airflow 1->5-7 (w0.8); protect 1->6-8 (w1); prevention* 0->6-10 (w0.4) | - |
| apple/black_rot | Managed | 0.3 / 0.74 (gain cap 0.16) | sanitation 2->8-10 (w1.4); protect 1->5-7 (w1) | mummies_left |
| apple/cedar_apple_rust | Managed | 0.25 / 0.79 (gain cap 0.13) | sanitation 1->7-9 (w1); protect 1->5-7 (w1); prevention* 0->6-10 (w0.5) | - |
| bell_pepper/bacterial_spot | Managed | 0.3 / 0.74 (gain cap 0.16) | replacement 2->8-10 (w1); sanitation 2->6-8 (w1); protect 0->3-5 (w0.5) | - |
| cherry/powdery_mildew | Managed | 0.35 / 0.7 (gain cap 0.18) | airflow 1->6-8 (w1); protect 1->6-8 (w1) | shaded |
| corn/gray_leaf_spot | Managed | 0.35 / 0.7 (gain cap 0.18) | protect 1->6-8 (w1.2); prevention 1->6-10 (w0.8); resistance 1->6-10 (w0.6) | - |
| corn/common_rust | Managed | 0.2 / 0.83 (gain cap 0.1) | monitoring 1->6-8 (w1.2); resistance 1->6-10 (w0.8) | - |
| corn/northern_leaf_blight | Managed | 0.3 / 0.74 (gain cap 0.16) | protect 1->6-8 (w1); prevention 1->6-10 (w0.8); resistance* 1->6-10 (w0.6) | - |
| grape/black_rot | Managed | 0.3 / 0.74 (gain cap 0.16) | sanitation 2->8-10 (w1.3); airflow 1->6-8 (w1); protect 1->5-7 (w1) | mummies_left |
| grape/esca | Replaced | 0.55 / 0.53 (gain cap 0) | containment 2->9-10 (w1.5); replacement 1->7-10 (w1); prevention 1->7-10 (w0.8) | not_contained (blocks) |
| grape/leaf_blight | Managed | 0.4 / 0.66 (gain cap 0.2) | protect 1->6-8 (w1.3); monitoring 1->6-8 (w1) | - |
| peach/bacterial_spot | Managed | 0.3 / 0.74 (gain cap 0.16) | stress 2->7-9 (w1.2); monitoring 1->6-8 (w1); prevention 0->6-10 (w0.6) | - |
| potato/early_blight | Managed | 0.35 / 0.7 (gain cap 0.18) | stress 2->6-8 (w1); protect 1->5-7 (w1); prevention* 1->6-10 (w0.6) | - |
| potato/late_blight | Replaced | 0.6 / 0.49 (gain cap 0) | containment 2->9-10 (w1.5); protect 1->6-8 (w1); replacement 1->7-10 (w1) | not_contained (blocks) |
| strawberry/leaf_scorch | Managed | 0.4 / 0.66 (gain cap 0.2) | drip 1->7-10 (w1); sanitation 1->6-8 (w1) | - |
| tomato/early_blight | Managed | 0.35 / 0.7 (gain cap 0.18) | sanitation 2->6-8 (w1); drip 1->7-10 (w1); protect 1->5-7 (w0.8); prevention* 0->5-10 (w0.4) | wet_spray |
| tomato/bacterial_spot | Managed | 0.3 / 0.74 (gain cap 0.16) | sanitation 2->6-8 (w1); drip 1->8-10 (w1.2); protect 0->3-5 (w0.5) | overhead |
| tomato/late_blight | Replaced | 0.6 / 0.49 (gain cap 0) | containment 2->9-10 (w1.5); protect 1->6-8 (w1); replacement 1->7-10 (w1) | not_contained (blocks) |
| tomato/leaf_mold | Managed | 0.35 / 0.7 (gain cap 0.18) | airflow 1->7-9 (w1.3); sanitation 1->5-7 (w1); protect 0->3-5 (w0.5) | closed_vents |
| tomato/septoria_leaf_spot | Managed | 0.4 / 0.66 (gain cap 0.2) | sanitation 2->6-8 (w1); drip 1->7-10 (w1); protect 1->4-6 (w0.7) | wet_spray |
| tomato/spider_mites | PestSuppressed | 0.3 / 0.74 (gain cap 0.15) | stress 2->6-8 (w1); biocontrol 1->6-8 (w1); contact 0->3-5 (w0.7) | kills_predators |
| tomato/target_spot | Managed | 0.4 / 0.66 (gain cap 0.2) | sanitation 2->6-8 (w1); airflow 1->7-9 (w1); protect 1->6-8 (w1) | wet_canopy |
| tomato/yellow_leaf_curl_virus | Replaced | 0.55 / 0.53 (gain cap 0) | containment 2->9-10 (w1.5); vector 1->7-10 (w1.2); replacement 1->7-10 (w1) | not_contained (blocks), open_vectors |
| tomato/mosaic_virus | Replaced | 0.5 / 0.57 (gain cap 0) | containment 2->9-10 (w1.5); sanitation 2->8-10 (w1.2); replacement 1->7-10 (w1) | not_contained (blocks), dirty_replant |

**Rules:**

- `wet_spray`: heavy overhead irrigation (drip ≤ 3) with spray coverage ≥ 5 on a leaf-wetness disease.
- `overhead`: drip ≤ 2 for bacterial spot.
- `closed_vents`: airflow ≤ 3 with spray ≥ 4 for leaf mold.
- `wet_canopy`, `shaded`: low airflow with heavy protection.
- `mummies_left`: spraying while mummies remain.
- `kills_predators`: predator release ≥ 6 with contact treatment ≥ 7.
- `open_vectors`: TYLCV with vector control ≤ 3.
- `dirty_replant`: mosaic virus, replanting with sanitation ≤ 4.
- `not_contained` (**blocks**): every Replaced case with containment ≤ 6.

Mild cases where spraying is not justified (corn common rust, strawberry leaf scorch, midsummer peach) have no spray control. The step lessons teach the same thing.

### 10.6 Progress database: schema version 2

`SqliteProgressRepository` now reads the stored `schema_version` first, then creates the v2 tables in one transaction and moves the version forward. A file written by a newer build keeps its higher version. Existing rows are never touched: the step-lesson `Save()` / `Reset()` (delete and re-insert of one case's rows) are unchanged.

| Table | Columns |
|---|---|
| learners | learner_id (PK autoincrement), name (UNIQUE, NOCASE), created_utc |
| manual_sessions | session_id (PK autoincrement), learner_id, lesson_id ("plant/disease"), plant_id, disease_id, content_version, started_utc, updated_utc, completed_utc, initial_severity, final_severity, initial_health, final_health, current_severity, current_health, applied_score, applied_valid, apply_count, health_checks, outcome, completed, current_stage, elapsed_seconds |
| manual_selections | session_id, control_id (PK), value, touched, applied_value |
| manual_actions | session_id, seq (PK), utc, stage, action, control_id, value, accepted, message (append-only history, refused actions included) |
| manual_stages | session_id, stage (PK), completed_utc |

- Saving a session is an UPDATE of its own row (or an INSERT when new), an update-or-insert of its selections, and INSERT OR IGNORE for history and stage times, all inside `RunInTransaction`. No session is ever deleted. "Start over" marks the unfinished session `outcome = 'Abandoned'` and starts a new one.
- Every action saves immediately; the clock saves every 2 s, and the session also saves on pause, focus loss, quit, leaving the lab, case change and learner change.
- **Resume after restart**: `LatestUnfinishedSession(learner, case)`. The Disease information panel then offers "Resume session (stage, elapsed)".
- Outcome keys: Managed, PartlyManaged, PestSuppressed, PartlySuppressed, Contained, PoorlyContained, NotContained, NotControlled, NotTreated, Abandoned.

### 10.7 Scripts

**Runtime** (`Scripts/Runtime/Treatment/Manual/`, namespace BTP.Treatment, so the existing link.xml rule preserves the row types):

| Script | Role |
|---|---|
| ManualTreatmentContent | ManualTreatmentProfile, TreatmentControlDef, TreatmentControlRule |
| TreatmentDataManager | crop → disease listing; loads and validates a manual case |
| TreatmentButtonController | − / + with step, clamp and the touched flag (no auto-select) |
| TreatmentValidationManager | per-control status and closeness, rules, 0..1 score, band, database feedback |
| LeafRecoveryModel | outcome-aware mapping, plus LeafHealthMonitor for the smooth displayed state |
| TreatmentTimer | 1-minute clock, 12 s guidance windows and their labels, pause and resume |
| TreatmentSessionManager | the pure C# state machine |
| ManualSessionRecord | LearnerRecord, ManualActionRecord |
| ProgressSaveManager | learners, remembered learner and mode, periodic saves, abandon |
| TreatmentResultManager | outcome keys and labels, apply summary, final result text, disclaimer |
| LeafRecoveryController, LeafParticleController | MonoBehaviours for the leaf and the particles |
| ManualTreatmentView | panel references and display |
| ManualTreatmentController | orchestrator; implements IEnvironmentExit |

**Changed:**

- SqliteTreatmentRepository: LoadManual. A missing table only disables the manual mode.
- TreatmentContent: `TreatmentCase.Manual`, `ManualError`.
- TreatmentCaseValidator: `ValidateManual`.
- SqliteProgressRepository: v2 migration, learners, sessions.
- TreatmentController: `SetManualMode`, the `CaseLoaded` event and `Specimen`. When manual mode is off, the step lesson is untouched.
- LeafAnchors: `Veins`, `VeinSegments()`.
- LeafRecovery.shader: the new properties.

**Editor:**

- ManualTreatmentSceneBuilder: panels, particles and components; called by menu 3.
- TreatmentSceneBuilder: the selector is taller (1060 mm, centre 1.5 m) and the helpers are internal.
- TreatmentAssetBuilder.Leaf: ComputeVeins and menu 5.

### 10.8 Scene configuration (menu items in order)

1. `python -I Tools/treatment_content_gen.py`: regenerates `treatment_content.sql`.
2. **BTP/Treatment/1 - Import Content Database**: re-imports the SQLiteAsset. The log shows "24 cases".
3. **BTP/Treatment/2 - Build Treatment Assets**: only after a full asset rebuild; it also bakes the vein paths. Otherwise run **BTP/Treatment/5 - Bake Leaf Vein Paths (Manual Treatment)**, which adds vein paths to the 24 existing specimen prefabs without rebuilding anything else.
4. **BTP/Treatment/3 - Build Treatment Scene**: rebuilds Treatment.unity with both modes.
5. **BTP/Treatment/4 - Connect Bootstrap, Lab and Farm**: only if XRBootstrap, Lab or Farm were rebuilt.
6. Play from XRBootstrap (or Treatment.unity; BootstrapGuard loads XRBootstrap). Choose **Manual treatment** on the CaseSelectorPanel.

### 10.9 Testing

- **EditMode**: Window > General > Test Runner > EditMode > Run All. Alternatively, with the Editor closed: `Unity.exe -batchmode -projectPath "<project>" -runTests -testPlatform EditMode -testResults results.xml -logFile test.log`. `ManualTreatmentTests` (20) cover:
  - all 24 profiles valid
  - the crop → disease listing
  - missing or invalid data blocking only that case
  - a database without the manual tables
  - abstract scales with no products
  - − / + steps and clamping
  - no auto-selection
  - validation and database feedback
  - combination rules
  - outcome-aware recovery (Replaced never improves; Managed never loses its spots or reaches 100%)
  - the health monitor
  - the timer never changing the leaf
  - the 60 s / 12 s guidance windows, their labels and pause
  - sessions saved past the time limit resuming in the final assessment
  - repeated refused presses collapsing in the history while every press is recorded
  - stage flow
  - success depending on valid actions
  - the v1 → v2 migration keeping rows
  - learners
  - session save/restore round trip

  The existing TreatmentTests (12) and TreatmentButtonTests (5) are unchanged.
- **Manual check in Play mode**:
  1. Choose Manual treatment and Start manual session, then press Diagnosis reviewed.
  2. Set every control with + and press Apply treatment. The leaf turns greener over about 3.5 s and the spots dry but remain.
  3. Continue to monitoring, Check health, Proceed to final assessment, Finish session. The results panel appears.
  4. Restart Play while a session is unfinished: Resume is offered.
- **Android / Quest offline checklist**:
  - Build Settings: Android, XRBootstrap first.
  - The content database ships inside the build as the SQLiteAsset referenced by TreatmentAssetCatalog on XRBootstrap. There is no network access, and no server is involved.
  - Progress is written to `Application.persistentDataPath/treatment_progress.db`. Existing v1 files migrate on first launch.
  - IL2CPP stripping: `Assets/_Project/link.xml` preserves `Gilzoide.SqliteNet` and the whole `BTP.Treatment` namespace (all row types).
  - Put the headset in airplane mode and run a full session.
  - Take the headset off mid-session (the timer pauses), then quit and relaunch: Resume should be offered with the same elapsed time, levels and leaf state.
  - Check frame time in the Treatment room with the particles active: at most 80 motes, one draw call.

### 10.10 Limits of this mode

- All values are teaching values that need expert review.
- Learner names are auto-numbered (no on-screen keyboard).
- The adjustment panel shows up to 5 controls. The current content has at most 4, and a test enforces 5 or fewer.
- Not yet verified on a Quest device or in an Android IL2CPP build.
