# Scene 3 manual treatment concepts for Quest 2

Five first-person game concepts using direct tracked-hand interactions. Generated with the built-in image generation tool using [the existing Treatment Lab capture](../Treatment_Captures/room_eb_02.png) as the visual reference. The output images are opaque PNGs.

| Image | Player action | Basis |
|---|---|---|
| [Trim the marked patch](01_cut_affected_leaf_patch.png) | Grab the shears, align the blades with the marked edge patch, and squeeze. | Tomato early blight, database steps `cut` and `dispose`. |
| [Handheld spray bottle](02_handheld_spray_bottle.png) | Grip the bottle, aim at the marked healthy leaf, and pull the trigger. | Tomato early blight, database steps `assess` and `spray`. |
| [Granular NPK](03_scoop_granular_npk.png) | Pick up a scoop and tip granules onto soil beside the plant. | Potato early blight, database method `care` and control `stress`. The scoop and NPK application are proposed ways to represent nutrient support after a soil check. |
| [Liquid fertilizer](04_pour_liquid_fertilizer.png) | Grab and tilt a watering can to feed the root zone. | Tomato spider mites, database control `stress`. The watering can is a proposed nutrient-support interaction. Live mites are managed separately in the case. |
| [Mature compost](05_mix_mature_compost.png) | Grab a trowel and mix compost into a bed before planting. | Proposed soil-preparation activity based on extension guidance. |

[Treatment database](../../Assets/_Project/Data/Treatment/treatment_content.sql) · [Soil testing and fertilizer selection](https://extension.umn.edu/garden-and-home/yard-and-garden/soil-testing-for-lawns-and-gardens) · [Compost and soil preparation](https://extension.umn.edu/garden-and-home/yard-and-garden/living-soil-healthy-garden)

## Shared prompt

Use case: ui-mockup. Create ONE standalone widescreen 16:9 in-headset mockup for Scene 3 Treatment in a Unity game for Meta Quest 2. The supplied actual Unity screenshot is a reference for its white-framed glasshouse, tile floor, gray specimen holders with subtle cyan rings, and charcoal world-space signs. Clean grounded Unity URP game art, natural daylight, simple reachable workstations. First-person view, the player's visible anatomically correct tracked virtual hands directly HOLD AND USE a familiar hand tool. No controllers, no robot, no tablet-operated action. Show the physical point where the action happens, legible in one glance, with the plant and tool both visible. Use small clear in-world signage with only the text explicitly specified. A subtle cyan guide can indicate target, no prominent holograms. No brand names, amounts, fertilizer ratios, recipes, or instant healing. Each scene is an educational simulation. Different framing to emphasize the activity.

## Image prompts

### 01 · cut affected leaf patch

Pruning a tomato early blight specimen. First-person close three-quarter view of the large upright serrated tomato leaf on the existing cyan-ringed holder. Right tracked virtual hand physically grips small red-handled plant shears with fingers visibly curling around the two handles and the shiny short blades actually meeting a thin marked cut line around ONE small brown concentric lesion at the leaf margin. Left tracked hand holds the stand's support handle clear of blades. Show an edge notch and a small loose brown-centered clipped piece over an open hinged-lid plant waste bin. The still-green majority of the leaf and full leaf stalk are intact. Make the shears and contact large. Plain warm gray virtual work gloves. Exact signs 'Trim the marked patch', 'Tomato / Early blight', 'Plant waste'.

### 02 · handheld spray bottle

Protecting marked healthy leaves in the tomato early-blight high-risk simulation. Eye-level first-person view, right tracked virtual hand grips a simple plastic trigger spray bottle, index finger visibly curled around and squeezing the trigger, hand and nozzle in the near foreground. Show a SHORT SUBTLE cone of fine mist from the nozzle reaching exactly one cyan-outlined healthy upper tomato leaflet. Show an older brown spotted lower leaf clearly remaining outside the mist. A neutral physical bottle label reads 'Training spray'. Small world-space sign reads 'Protect healthy leaves', 'Tomato / Early blight'. Smooth gray virtual gloves and protective sleeve cuffs. One visible hand is enough. No aiming at the fruit, soil or learner. No UV light or digital beam.

### 03 · scoop granular npk

Soil-test-guided granular NPK used for plant stress support in the potato early-blight lesson, represented as a first-person game action. Slight downward view at a raised soil bed with several recognizable compound potato leaves, no tomatoes; one lower leaf has a couple of old brown marks and stays that way. Right tracked virtual hand grips a simple small measuring scoop with visible palm contact, tilting it as a modest trickle of small beige solid granules drops onto a narrow highlighted band of BARE soil BESIDE the plant and away from the stem and leaves. The scoop has no measurement marks. Nearby plain bag reads 'Granular NPK'. One small charcoal panel reads 'Apply to soil', 'Potato / Nutrient support', 'Soil test checked'. Bare or snug neutral virtual work-gloved hands clearly articulated. Make the solid pellets and the destination distinct, avoid a pile.

### 04 · pour liquid fertilizer

Root-feeding for a tomato plant in a stress-support station, in the tomato spider-mite lesson. First-person close low angle at a potted tomato plant, with sparse pale stippling on a couple of leaves, and clear visible soil. Right tracked hand wraps fingers around top handle of a small ordinary watering can; left tracked hand supports it from beneath while it tilts. A narrow water-like stream from its spout lands directly on exposed soil beside the root zone, with foliage visibly dry. Watering can plain label 'Diluted liquid fertilizer'. In-world sign 'Feed the root zone', 'Tomato / Nutrient support'. Keep the watering can and stream entirely visible. No mixing station, exact ratios, poured concentrate, or new green growth appearing. Gray virtual work gloves.

### 05 · mix mature compost

Separate pre-plant soil preparation workstation in the same greenhouse. Slight three-quarter downward view of a waist-high raised bed with loose light-brown planting soil. One tracked virtual hand has a clear traditional grip on a hand trowel and folds dark, fine, mature compost into the lighter soil in a small working patch. Show contrasting textures and the point where the steel blade enters the soil. The other tracked hand steadies the bed edge well away from the blade. Simple nearby tub labeled 'Mature compost'; small upright tomato seedlings wait in a propagation tray beyond the bed with no fertilizer touching them. One charcoal sign reads 'Prepare the planting bed'. No fresh manure or plant disease cure. No robots. Gray virtual work gloves.
