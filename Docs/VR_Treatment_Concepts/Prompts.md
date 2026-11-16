# VR Treatment Scene Concepts

Created: 1 November 2026.

Generated with the built-in image generation tool using the project's existing Lab, Farm, and plant-model screenshots as references. These are proposed first-person VR screens, not captures of implemented treatment gameplay. Existing Unity scenes and scripts were not changed.

## Images

| Image | Teaching case | Interaction shown |
|---|---|---|
| [01 - Inspect the plant](01_leaf_inspection.png) | Tomato / early blight | Inspect leaf symptoms with a magnifier and confirm the supplied teaching case. |
| [02 - Prune, dispose, and clean tools](02_pruning_and_sanitation.png) | Tomato / early blight | Remove a marked leaf, retain healthy foliage, dispose of material, and clean tools. |
| [03 - Correct irrigation and airflow](03_irrigation_and_ventilation.png) | Tomato / leaf mold | Position drip irrigation at the soil and operate ventilation controls. |
| [04 - Choose and apply suitable protection](04_targeted_protection.png) | Tomato / early blight | Apply justified crop-appropriate protection within the teaching scenario. |
| [05 - Review healthy new growth](05_follow_up_and_new_growth.png) | Tomato / early blight | Inspect healthy new growth while recognising that old lesions remain. |
| [06 - Contain viruses and establish a replacement](06_virus_containment_and_replacement.png) | Tomato / yellow leaf curl virus | Keep the infected plant contained and establish a separate healthy transplant. |

Images 01, 02, 04, and 05 illustrate the early-blight workflow. Image 03 is a separate leaf-mold environment-control example; image 06 is a separate virus-management example. The six images are related interface concepts, not one universal treatment sequence for every disease.

## Reference images

1. Lab architecture and interface: `D:\Unity Projects\Plant Disease\BTP_Project\Docs\Screenshots\12_lab_overview_tomato_week1.png`
2. Farm, hills, and surrounding crops: `D:\Unity Projects\Plant Disease\BTP_Project\Docs\Screenshots\03_farm_specimen_row.png`
3. Crop model appearance: `D:\Unity Projects\Plant Disease\BTP_Project\Docs\Screenshots\23_plant_lab_prefabs_9_species.png`
4. Shared style reference for images 02-06: `01_leaf_inspection.png` in this directory, an unchanged copy of the first generated image.

All references were used to generate new images; the source screenshots were not edited. Original generated files were retained.

## Final prompt set

For each image, concatenate the shared prompt below with its corresponding image-specific prompt. Images 02-06 also used reference image 4. Output transparency was disabled for every image.

### Shared prompt

```text
Use case: ui-mockup.
Asset type: high-fidelity first-person VR agricultural education game screen concept, one standalone landscape 16:9 image, clean 1920x1080-style presentation.
Reference image 1 is the actual existing Unity project LAB screenshot: use it only as the architectural and UI style reference, NOT as an edit target. Reference image 2 is the project's FARM screenshot: use for the farmland and hills visible through the glass. Reference image 3 is the nine-species MODEL sheet: use for believable crop appearance and recognisable tomato form. Create a NEW teaching scene in this same modest, buildable Unity URP world.
Visual identity: an airy white metal-framed glasshouse, white tile floor, green field and low hills outside, soft natural daylight, cylindrical grey specimen pedestals, cyan selection rings. Sharply rendered leafy plants with natural flaws and believable soil. World-space charcoal panels with white sans-serif text, cyan active states, muted amber warnings; generous spacing, readable large type, short exact text. Refine the current layout into an uncluttered training workstation without redesigning the greenhouse. Polished attainable VR game art, not a photograph, not an extravagant science-fiction lab.
Composition: single first-person headset eye view, normal perspective with subtle wide angle, at most two anatomically correct gloved virtual hands in foreground as appropriate. A waist-high reachable plant workstation is central, task UI anchored off to one side, tools within reach. UI must be physically in the world, not a flat screen overlay. No headset border, binocular distortion, monitor, keyboard, Unity editor, third-person avatar, extra hands, watermarks, decorative code or tiny text. Avoid huge unreadable menus.
Scientific constraints: no magical healing beams, no instantaneous conversion of dead tissue into green tissue, no false health/chlorophyll percentage, no pesticide product brands or mixing doses. Small corner/footer text reads exactly "CONCEPT • TRAINING SIMULATION". This is a proposed interactive screen, not evidence of implemented gameplay.
```

### 01 - Inspect the plant

```text
Screen 1: INSPECTION, case TOMATO / EARLY BLIGHT.
Camera close enough to see a tomato plant with red fruit, several healthy leaves, and a few lower leaves with tan-brown concentric early-blight lesions and yellowing. Beside the whole plant, the familiar existing cylindrical grey pedestal displays ONE enlarged detached diseased tomato leaf upright as an inspection reference. A gloved hand holds a simple circular magnifier aimed at one lower leaf; the magnifier shows enlarged concentric spots, not glowing organisms. A second hand may gently rotate the specimen. A subtle cyan outline indicates the inspected leaf.
One main floating charcoal panel uses this exact text, with clear hierarchy:
"Tomato • Early blight"
"1  Inspect"
"Check older lower leaves"
"Look for concentric spots"
"Inspect both leaf surfaces"
Button: "Confirm teaching case"
Small supporting panel: "Inspect → Prepare → Treat → Recheck"
Keep the main plant unobscured. Show pruning shears, gloves, and a closed disposal container neatly on a nearby shelf, but no treatment being applied yet.
```

### 02 - Prune, dispose, and clean tools

```text
Screen 2: HANDS-ON PRUNING AND SANITATION, case TOMATO / EARLY BLIGHT.
The central tomato plant retains MOST of its healthy canopy and intact stem and red fruit. A selected lower diseased leaf and its thin petiole are marked with a small cyan outline. A right gloved hand holds realistic pruning shears around the leaf petiole, carefully avoiding the main stem; left gloved hand supports only that selected leaf. The action is about removing ONE marked diseased leaf, never stripping the plant or cutting the central stem. A small rigid lidded disposal container with visible "Plant waste" label sits directly beside the working area. An unobtrusive sanitation dock behind it holds cleaned shears and a sealed container marked "Tool hygiene", without dosing instructions. No loose diseased leaves scattered on the ground.
Large world-space panel exact text:
"Tomato • Early blight"
"2  Remove affected leaves"
"Cut only the marked petiole"
"Keep healthy foliage"
"Place removed leaf in Plant waste"
"Then clean the tools"
Bottom action button: "Show cut point"
A short arrow guide points to the petiole, not the stem. The screen must visibly teach precision and disposal. Preserve the realistic greenhouse architecture.
Reference image 4 is the just-generated inspection screen. Match its plant scale, clean grey gloves and sleeves, tile material, panel typography, cyan colour and attainable game-render finish so these screens form one coherent set. Change the task and camera framing as specified above.
```

### 03 - Correct irrigation and airflow

```text
Screen 3: GROWING-CONDITION CORRECTION, case TOMATO / LEAF MOLD. This is a clearly different disease lesson, explicitly labelled.
Show a waist-high tomato bed on a compact accessible lab treatment platform. A few lower leaves show yellow patches; an enlarged detached leaf reference shows olive-brown mold on its underside. Most canopy is intact. A gloved hand positions the end of a black drip-irrigation emitter at the SOIL near the plant's root zone. Show a subtle small drip hitting soil ONLY, no water spraying or splashing foliage. The plant is supported by a simple stake.
The greenhouse has a realistically open roof vent, and a modest circulation fan in background. A second gloved hand points at the nearby in-world vent-control panel. Use natural daylight and low hills beyond glass; no futuristic tubes.
Main panel exact text:
"Tomato • Leaf mold"
"3  Correct growing conditions"
"Water the soil, keep leaves dry"
"Improve ventilation"
"Reduce prolonged high humidity"
Three clear state indicators with icons:
"Drip irrigation  ON"
"Roof vent  OPEN"
"Air circulation  ON"
Button: "Recheck conditions"
The image should immediately explain root-zone watering and greenhouse airflow, without numeric humidity guarantees or medication.
Reference image 4 is the generated inspection screen. Match its clean grey gloves and sleeves, tile material, dark panel typography, cyan colour and attainable game-render finish. This screen is the explicitly labelled LEAF MOLD case; show olive underside mold rather than concentric early-blight spots.
```

### 04 - Choose and apply suitable protection

```text
Screen 4: APPROPRIATE PROTECTIVE TREATMENT, case TOMATO / EARLY BLIGHT in a high-risk teaching scenario.
First-person close view of a staked tomato plant on its pedestal. Some old lower lesions remain brown; upper green foliage is the target for protection. The right gloved hand holds a simple modest grey hand sprayer, the left may steady its base, nozzle pointing toward designated foliage. Render a SMALL realistic fine mist cone, never a hose jet, laser or glow, with no spray drifting onto the user's hands, soil, fruit basket or equipment. A thin cyan outline indicates designated leaf surfaces, not all objects. A neutral teaching card on sprayer reads "Training only" and "Protective treatment" with no product name or dose.
Large anchored panel exact text:
"Tomato • Early blight"
"4  Protect healthy foliage"
"High-risk teaching scenario"
"Choose a crop-appropriate treatment"
"Follow the label and treatment window"
"Old lesions will remain"
A short checked preparatory line: "Protection and label checks complete"
Bottom button: "Check coverage"
Show suitable full protective sleeves and gloves as visible first-person equipment; do not imply gloves alone exhaust real product PPE requirements. No fictional percentage recovery, no blanket cure, no bottle dose or mixing formula. The visual is an education simulation of justified protection.
Reference image 4 is the generated inspection screen. Match its room, natural daylight, panel typography, cyan colour and game-render finish. For this SPRAYING step specifically, change the gardening gloves to smooth grey chemical-resistant gloves overlapping protective sleeve cuffs; do not copy the knitted glove material. One or both hands hold the sprayer safely behind the nozzle, outside the mist. The small volume of mist reaches foliage, never people or soil.
```

### 05 - Review healthy new growth

```text
Screen 5: FOLLOW-UP AND REALISTIC OUTCOME, case TOMATO / EARLY BLIGHT. A time-compressed later observation in the SAME greenhouse style.
Two matching cylindrical grey pedestals form an obvious comparison, both holding recognisable same-species tomato plants. Left is a snapshot reference with more damaged lower leaves, clearly labelled "Before". Right is the treated current specimen with vigorous healthy upper/new growth and intact tomato fruits, but with a FEW surviving older lower leaves still visibly bearing brown lesions; never turn scars green. Cyan marker leads the viewer to a newly expanded clean upper leaf. The plants are framed at comfortable VR inspection distance. At most one gloved hand points toward that new leaf.
Main world-space panel exact text:
"Tomato • Early blight"
"5  Follow-up inspection"
"Healthy new growth"
"Old lesions remain"
"No new lesions in this simulation"
Status: "Disease managed"
Button: "Continue monitoring"
A small unambiguous caption reads "Simulated later observation".
A short large-type checklist contains "Inspect new leaves" and "Check for renewed spread". No 100 percent health, no chlorophyll or real-world success guarantee. The picture communicates task-dependent disease management, not instant cure. Use warmth in the light while retaining natural daylight and the cyan/charcoal interface.
Reference image 4 is the generated inspection screen. Match its tomato model styling, grey pedestals, tile material, panel typography, cyan colour and game-render finish. Fit BOTH comparison specimens comfortably in frame, without giant UI obscuring them. Label the left historical snapshot 'Before' and the right current specimen 'After management'. The right specimen still has two or three scarred lower leaves; its new upper leaves are healthy, not a whole-plant green recolour.
```

### 06 - Contain viruses and establish a replacement

```text
Screen 6: VIRUS MANAGEMENT AND HEALTHY REPLACEMENT, explicitly case TOMATO / YELLOW LEAF CURL VIRUS.
One first-person view of the greenhouse treatment station after containment. The ORIGINAL short sick tomato specimen, whose small leaves are curled and yellow, is clearly visible inside a transparent CLOSED containment bag in a separate covered disposal/containment compartment on one side, labelled "Infected plant • contained". It is NOT being healed or sprayed. There is a clear physical divider between that compartment and the clean planting area.
The central clean pot contains a small HEALTHY NEW tomato transplant with straight fresh green leaves. Two gloved hands are gently positioning that new transplant into clean soil, exactly one plant being planted. A small plant label reads "Healthy replacement". A fine insect mesh enclosure and a whitefly-monitoring card can be seen behind the clean station, indicating vector exclusion/monitoring; not a magical sterilisation chamber.
Main readable charcoal UI exact text:
"Tomato • Yellow leaf curl virus"
"6  Contain and replace"
"No cure for the infected plant"
"Contain infected material"
"Manage whiteflies"
"Plant clean stock"
Status: "Healthy replacement established"
Button: "Monitor the replacement"
No fungicide bottle, healing effect, pesticide dose, or claim that an infected plant is cured. Make the distinction between the closed contained ORIGINAL specimen and the separately planted NEW specimen immediately clear.
Reference image 4 is the generated inspection screen. Match its room, natural daylight, clean grey gloves, panel typography, cyan colour and game-render finish. IMPORTANT: the sick original is INSIDE a fully closed transparent containment bag behind a physical divider in a separate compartment; the new green seedling is visibly a DIFFERENT smaller plant on the clean work side. Avoid full-sized fruiting tomato as the replacement; show a young transplant. The sealed sick plant must have yellow curled leaves rather than early-blight concentric lesions. Visually mark containment and clean replacement distinctly.
```

