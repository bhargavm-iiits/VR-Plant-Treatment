# Treatment Lab: simple concepts

Seven concept boards proposing a simpler, more effective `Assets/Scenes/Treatment.unity`. They are flat illustrations drawn in SVG by `generate_concepts.py` (run `python generate_concepts.py`; needs Chrome or Edge and Pillow), not screenshots or AI renders.

The problem they address: the manual mode currently spreads six floating panels from −98° to +80° around one leaf, so the learner keeps turning their head and reading numbers.

| Image | Idea | Why it is simpler |
|---|---|---|
| [00 Overview](00_overview.png) | All seven on one sheet | For slides or a quick comparison |
| [01 One leaf, one card](01_one_leaf_one_card.png) | One card beside the leaf: Diagnose → Treat → Recover, three controls, one Apply | Replaces the six panels; green dashed zone shows the right amount |
| [02 Three stations](02_three_stations.png) | Look, Treat and Result stations joined by a floor path | The room explains the order |
| [03 Grab a tool, touch the leaf](03_grab_tool_touch_leaf.png) | Shears, drip valve, fan and spray on a tray; a cyan ring marks the target | Hands instead of sliders; one instruction at a time |
| [04 Turn the dials](04_treatment_dials.png) | One dial per control with a green good zone | Mistakes are visible before Apply; uses `rec_min` / `rec_max` |
| [05 Fast-forward the recovery](05_recovery_timeline.png) | Time slider showing Day 0 → Week 6 | Shows recovery honestly: spots dry but stay |
| [06 One clear result](06_clear_result_card.png) | Before/after, one score, one feedback line per control | Tells the learner what to change on "Try again" |
| [07 Contain and replace](07_contain_and_replace.png) | Bag the virus plant, clean up, plant clean stock | Makes the five Replaced cases visual |

The concepts reuse existing data (`treatment_controls`, `case_profile`, outcomes), so most of them change only the view layer (`ManualTreatmentView`, `ManualTreatmentSceneBuilder`). The numbers on the boards are illustrative only.

**Suggested combination:** 01 + 03 for the main flow, 05 for the recovery, 06 for the end screen, and 07 for the Replaced cases.