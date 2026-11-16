# Scene 3 treatment concepts for Quest 2

Ten first-person design concepts for the Unity Treatment Lab. The treatment actions come from the scene’s SQLite content. The proposed machines and controls turn those actions into controller-free Quest 2 interactions. The images were generated with the built-in image generation tool.

The reference for every generation was [room_eb_02.png](../Treatment_Captures/room_eb_02.png). Transparency was disabled. For each image, combine the indicated shared prompt with its scene-specific prompt.

## Quest 2 interactions

The project already uses Unity XR Hands and XR Interaction Toolkit. Android has OpenXR Hand Tracking and Meta Hand Tracking Aim enabled. The stations below show proposed interactions for that rig. The images are concept renders for future Unity work. [Bootstrap scene builder](../../Assets/_Project/Scripts/Editor/BootstrapSceneBuilder.cs) · [Android OpenXR settings](../../Assets/XR/Settings/OpenXR%20Package%20Settings.asset) · [Meta hand interaction guidance](https://developers.meta.com/vr/documentation/unity/unity-handtracking-interactions/)

I loaded the [treatment SQL](../../Assets/_Project/Data/Treatment/treatment_content.sql) in SQLite and checked the case, method, treatment control, and authored step or follow-up for each image. Each frame shows one action within a case workflow.

| Concept and case | Hand input | Database method or checkpoint | Treatment shown |
|---|---|---|---|
| [01 · Robotic lesion removal](01_robotic_selective_pruning.png) · Tomato early blight | Index poke | `sanitation`, steps `cut` and `dispose` | Trim the marked lesion patch while preserving the healthy tissue and petiole. |
| [02 · Targeted protection](02_vision_guided_protection.png) · Tomato early blight | Ray and pinch | `protect`, steps `assess` and `spray` | In the database’s high-risk scenario, protect selected healthy tissue. Old lesions remain. |
| [03 · Root-zone irrigation](03_sensor_guided_drip.png) · Tomato bacterial spot | Pinch and turn | `irrigation`, step `valve` | Shift to drip irrigation at the soil to limit splash and keep foliage dry. |
| [04 · Adaptive ventilation](04_adaptive_ventilation.png) · Tomato leaf mold | Index drag | `environment`, steps `vent` and `fan` | Open the roof vent and circulate air to dry foliage faster. |
| [05 · Sanitation station](05_automated_sanitation.png) · Tomato Septoria leaf spot | Grab and push | `sanitation`, steps `dispose` and `clean_tools` | Dispose of cut tissue in covered plant waste and clean the shears in a separate dock. |
| [06 · Biological control](06_biocontrol_release.png) · Tomato two-spotted spider mites | Pinch and place | `biological`, steps `route` and `release` | Hang a predatory-mite sachet beside the marked colony. Suppression takes time. |
| [07 · Quarantine and replacement](07_quarantine_and_replacement.png) · Tomato yellow leaf curl virus | Grab and slide | `contain`, `vectors`, `clean_stock` | Keep the infected stock isolated. Place separate clean stock behind whitefly-exclusion mesh. |
| [08 · Collect berry mummies](08_grape_mummy_collection.png) · Grape black rot | Pinch and place | `sanitation`, steps `mummies` and `collect` | Collect shrivelled berries from the vine and ground. |
| [09 · Orchard cleanup](09_orchard_leaf_litter_collection.png) · Apple scab | Ray and pinch | `sanitation` method | Collect infected fallen leaves for disposal to reduce the overwintering spore source. |
| [10 · Treatment follow-up](10_time_lapse_verification.png) · Tomato early blight | Pinch and drag | `new_growth` and `old_lesions` follow-ups | Compare clean new growth with persistent scars on the older leaf. |

## Shared prompts

### Shared prompt A

Use case: ui-mockup. Generate ONE standalone widescreen 16:9, first-person Meta Quest 2 game concept for Scene 3 Treatment in an agricultural Unity VR simulator. Reference screenshot is for the simple white-framed glasshouse and white tile floor, its cyan-ringed gray specimen pedestal, and restrained charcoal/cyan world-space UI. Reimagine the workstation with plausible advanced white/gray hardware, modular geometry, and legible Unity URP game art in daylight. Show the player's tracked virtual hand(s) prominently at the lower foreground, natural bare skin-tone meshes, anatomically correct five fingers, no gloves, no physical controllers. Show one obvious hand gesture interacting with its specified target; make index/thumb contact unambiguous for pinches and fingertip contact obvious for pokes. Reachable controls at bench height; gesture and hardware response are distinct. Keep machine actions outside the player's hands. Feature a clearly visible leaf or plant specimen. Use only the explicitly quoted strings below, rendered legibly on world-space controls. No brands, doses, chemical names, headset border, watermarks, split screens, instant lesion healing or sci-fi magic. Cyan arrows or rays are optional UI overlays only.

### Shared prompt B

Use case: ui-mockup. Generate one standalone wide 16:9 first-person Meta Quest 2 gameplay concept for Scene 3 Treatment, matching the supplied Unity VR screenshot as a reference for its simple white-framed glasshouse or vineyard bay, white tiles, gray cyan-ringed specimen pedestal and charcoal/cyan world-space UI. Polished plausible Unity URP game art. Show the player's bare tracked virtual hands prominently in the lower foreground with natural skin tone, five fingers, no physical controllers, one unambiguous poke or pinch or grab as specified, with the treatment response clear. In-world text only, render the quoted copy exactly. No brands, doses, watermarks, instant lesion healing or graphic effects beyond restrained cyan UI. Foreground touch targets should be reachable at bench height.

### Shared prompt C

Use case: ui-mockup. One standalone 16:9 first-person Meta Quest 2 Unity Treatment Lab concept. Reference is for simple white glasshouse architecture, tiles, gray cyan-ringed specimen stand, restrained charcoal/cyan world-space controls. Polished plausible Unity URP game art. Bare naturally skinned anatomically correct tracked virtual hands in foreground, controller-free. Exact in-world text only. No brands, doses, watermarks or magical healing.

## Scene prompts

### [01 · Robotic selective pruning](01_robotic_selective_pruning.png)

Shared prompt B

Tomato early blight, treatment database cut and dispose steps. Show a large mounted serrated tomato leaflet on the central specimen stand with a few tan-brown concentric lesions and yellow halos. The entire black-mounted green petiole is visibly intact, away from the cutter. A tabletop robot carefully removes ONE small lesion-bearing edge patch from the leaf blade. The marked cut traces just around the spot and leaves most of the healthy blade and entire stalk intact. Clearly show a small V-shaped missing bite at the edge and a matching little brown-centered tissue fragment falling toward an open lidded waste bin. No stalk cuts. In near foreground a virtual right index finger visibly touches the large cyan 'Confirm patch trim' button on a safety console. Exact UI text: 'Robotic lesion removal', 'Tomato / Early blight', 'Confirm patch trim', 'Plant waste'.

### [02 · Vision guided protection](02_vision_guided_protection.png)

Shared prompt A

Diagonal view into a sealed glass-fronted training booth with a tomato canopy. Fixed machine-vision camera and one compact micro-spray nozzle on a rail. Small mist cone within booth targets ONLY a cyan-mapped patch of healthy upper foliage; older lower scars remain brown. In foreground the player's bare hand performs clear index-to-thumb pinch, a thin pointing ray terminates on the exterior start button. Do not confuse input ray with spray. Exact text 'Targeted protection', 'Tomato / Early blight', 'Start simulation'.

### [03 · Sensor guided drip](03_sensor_guided_drip.png)

Shared prompt A

Low first-person view into a waist-high tomato bed. Show a black drip emitter pinned to soil beside stem, tiny drop hitting only soil, adjacent paired-prong soil sensor. Dry tomato leaf with small dark bacterial spots in foreground. Player's visible bare hand pinches the edges of a large knurled flow dial, thumb and index in visible contact, short cyan adjustment arc. Exact text 'Root-zone irrigation', 'Tomato / Bacterial spot', 'Keep foliage dry'.

### [04 · Adaptive ventilation](04_adaptive_ventilation.png)

Shared prompt B

Database case tomato leaf mold, open the roof vent and start air circulation. In the front center, an enlarged flipped tomato leaflet is mounted underside facing viewer, with diffuse olive-brown velvety irregular patches (NO concentric rings, NO discrete brown target spots), pale green veins. In background a raised tomato bed, open roof vent and small fan. Thin translucent cyan airflow lines lead above the bed out of the open vent, not over UI. Prominently show a bare index fingertip touching a large cyan knob on the reachable vertical vent slider, other fingers curled. Show a single charcoal screen beside the leaf. Exact copy: 'Adaptive ventilation', 'Tomato / Leaf mold', 'Airflow preview'. No magnifier display required, make physical leaflet correctly show leaf mold.

### [05 · Automated sanitation](05_automated_sanitation.png)

Shared prompt B

Database case tomato Septoria leaf spot, dispose of cut lesion tissue and clean the shears. Player's bare tracked hand clearly grips a broad cyan handle, sliding a lidded plant-waste drawer shut. Through the small clear drawer lid window show just 3 small triangular or irregular patches of trimmed leaf blade, each with tiny dark rimmed spots with pale centers. NO whole leaves in the waste. On a cyan-ringed stand in the background the majority of the original large leaf remains mounted on its intact stalk and shows three small cutouts along its edge. A separate enclosed cleaning dock holds red shears. Exact text: 'Sanitation station', 'Tomato / Septoria leaf spot', 'Plant waste', 'Tool cleaning'. No chemical instructions.

### [06 · Biocontrol release](06_biocontrol_release.png)

Shared prompt C

Tomato two-spotted spider mites, database step release: place the release sachet at the colony. Close three-quarter view of one live tomato plant at a reachable VR treatment bench. A crisp tomato leaflet is angled underside towards viewer, with fine webbing near a branch and pale speckling. A cyan AR bracket marks the mite colony. The tracked virtual right hand pinches the wide tab of a SMALL MATTE PAPER sachet labelled 'Predatory mites' and is visibly hanging it on the stem clip immediately next to that marked colony. It must be a small physical sachet with an eyelet on the plant, NOT a label or button, NOT an electronic cartridge. The other hand is optional. A compact mounted macro camera observes the leaf from a few inches away and one subtle charcoal status display reads 'Tomato / Spider mites' and 'Place sachet at colony'. No instant improvement or oversized airborne bugs.

### [07 · Quarantine and replacement](07_quarantine_and_replacement.png)

Shared prompt A

Two clearly divided bench zones. On left an old stunted tomato plant with yellow upward-curled leaves fully enclosed inside a sealed clear quarantine chamber. On right a separate young healthy tomato seedling in clean growing media on an insect-screened nursery tray. In the foreground one bare tracked hand visibly grips the cyan external handle of the clean nursery tray to align it in its slot. Exact text 'Quarantine and replace', 'Tomato / Yellow leaf curl virus', 'Infected stock', 'Clean stock'. Leave the original visibly sick and untouched.

### [08 · Grape mummy collection](08_grape_mummy_collection.png)

Shared prompt B

Database case grape black rot, collect shrivelled black berry mummies from the vine and ground. First-person vineyard bay with light floor and rows visible beyond white frame. One foreground bare virtual hand pinches the broad blue tab of a small gripper, which holds a very clearly shrivelled matte black dried grape berry just above an open rigid plant-waste container. Several visibly shrivelled black berries still lie on the soil tray beneath the vine. A cluster of plump green healthy grapes stays attached and untouched in the upper background, and a mounted grape leaf shows tan spots with dark borders. Use a subtle cyan highlight to show the selected dried berry only. Exact screen text: 'Collect berry mummies', 'Grape / Black rot', 'Check vine and ground'. No spray or airflow machinery, focus on sanitation.

### [09 · Orchard leaf litter collection](09_orchard_leaf_litter_collection.png)

Shared prompt A

Bright open-sided orchard treatment bay with young apple tree and soil, matching the project white framing and charcoal/cyan UI. A small low wheeled collection robot with enclosed debris bin steers over fallen spotted leaves, standing tree untouched. Large olive-spotted apple scab leaf on near pedestal. Foreground hand makes a clear thumb-index pinch, thin cyan pointing ray terminates at a large circular floor waypoint just ahead of the rover. Exact text 'Collect fallen leaves', 'Apple / Scab', 'Set cleanup route'.

### [10 · Time lapse verification](10_time_lapse_verification.png)

Shared prompt A

Eye-level close view of tomato plant on cyan-ringed gray pedestal, imaging camera on short arc rail. On same plant show clean green upper new leaf and older lower leaf with visible concentric brown scars. Foreground bare hand visibly pinches oversized cyan timeline handle between thumb and index, dragging sideways on short near-field track with abstract ticks. Small in-world chart; subtle cyan bracket on new growth and amber ring on a persistent spot. Exact text 'Treatment follow-up', 'Tomato / Early blight', 'New growth', 'Old lesions remain'. No numeric efficacy claims or leaf morphing.
