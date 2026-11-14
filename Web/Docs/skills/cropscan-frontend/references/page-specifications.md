# Page-by-page visual specification

The PNGs cover seven route families and selected alternate states. Coordinates are approximate starting points measured from the top-left of the 1586 × 992 desktop viewport. Use the images for alignment and the route code for actual behavior. Resolve minor inconsistencies in the generated wordmarks and typography with the shared [design system](design-system.md).

| Frame | Route or state | Full-size design target |
| --- | --- | --- |
| 01 | `/` first viewport | [Home hero](../../../design-references/01-home-hero.png) |
| 02 | `/` below the hero | [Home walkthrough](../../../design-references/02-home-scroll.png) |
| 03 | `/detect` with an accepted file | [Photo ready to analyse](../../../design-references/03-detect.png) |
| 04 | `/result?scan=…` confirmed example | [Diagnosis and evidence](../../../design-references/04-result.png) |
| 05 | `/result?scan=…` uncertain | [Retake guidance](../../../design-references/05-result-uncertain.png) |
| 06 | `/field` without selection | [Disease selection](../../../design-references/06-field-browser.png) |
| 07 | `/field?disease=…` or `/field?scan=…` | [Greenhouse explorer](../../../design-references/07-field-explorer.png) |
| 08 | `/quest-demo` | [Meta Quest demo](../../../design-references/08-quest-demo.png) |
| 09 | `/about` | [Methodology](../../../design-references/09-about.png) |
| 10 | unmatched route | [404 recovery](../../../design-references/10-not-found.png) |

## 01 · Home hero

**Geometry.** Place the hero against the shared header at y≈72. The cream topographic atlas extends to y≈920. Set the copy block around x=45 and y=170, about 510 px wide. Use the serif headline “From field to leaf.” at roughly 100 px with a compact two-line lockup. Set “Scan a leaf. Explore the conditions behind the diagnosis.” below in a two-line paragraph up to about 460 px. Give the buttons roughly 16 px horizontal separation and 26–30 px space above. The row begins around y=500. Let the next heading “How it works” appear at the bottom of the first desktop viewport.

**Atlas.** Crop the terrain into the right edge and across the lower canvas. Maize fills the lower left, terraced rice sits behind, and tomato rows and greenhouse sit center-right. The greenhouse roof is roughly at x=850–1140 and y=410–570. Preserve the cutaway edge and clear cream area for the copy. Place one terracotta pin near the greenhouse center and connect it to a bottom-right preview around x=1130, y=566, 425 × 290 px. Use an aisle image above the “Enter the field” action. Keep the + and − controls compact in the upper right.

**Interaction.** Show the preview selected on the initial desktop frame. The waypoint toggles or focuses it. “Detect Disease” opens Detect, “See VR Demo” uses the browser-aware Quest behavior, and “Enter the field” opens the real Field route or an existing eligible case when explicitly mapped. Keep the preview link if the atlas fails. Show zoom controls only if they work. On narrow screens, order heading, copy, actions, mapped art, preview, next section. Use a greenhouse-focused crop and move the preview into an inline strip when an overlapping panel would hide the map or controls.

## 02 · Home below the fold

**Geometry.** Under the sticky header, center the serif “How it works” at about 76–84 px, with 34–44 px before the photo row. The three images are about 450 × 250 px, with ~65 px gaps for subdued terracotta connectors. Align titles and descriptions to each photo. Bring the four-metric band into y≈575–660 between hairlines. Place “Supported crops” and the right-aligned “Start a scan” above the crop display. Keep the three tiles and six featured crop cutouts easy to distinguish.

| Step | Heading | Body |
| --- | --- | --- |
| Capture | Upload a leaf photo | Use a clear photo of one leaf. |
| Diagnose | Read your result | See the diagnosis and treatment guidance. |
| Explore | Explore the field | Follow the disease cycle in the browser or in VR. |

Use the same leaf photo and its illustrative attention state in the first two images. The third shows the same greenhouse aisle. Preserve current class, crop, cycle and language counts. Show the six featured crops from the image, then keep the existing full crop list reachable. On narrow screens stack the steps, use a vertical connector only if it aids comprehension, and reflow metrics into two columns. Use a grid or accessible scrollable crop display with a visible way to see the remaining crops.

## 03 · Detect

**Geometry.** Set the small “Home / Detect” breadcrumb around y≈100. Place a 64–72 px serif heading and short intro above the form. From y≈280 divide the work area roughly 56/44 with a 12–24 px gap. In the bordered left area, use a photo close to 16:9 and four fine corner marks. Put “Leaf photo” above, “Remove photo” and formats below. On the right, group the crop dropdown and full-width “Analyse Disease” in one block. Divide beneath it for the four photo tips. Keep the contour lines low in the background.

**States.** Before upload, put the native accessible picker and drop area in the same footprint, with format and one-leaf guidance. For a selected file show its real preview and allow remove and replacement. Preserve file validation, optional crop hint, input reset and preview cleanup. Put local validation next to the picker and API failure near the primary action. Preserve valid inputs for retry. Use the [scan loading rules](../../cropscan-web-motion/references/motion-loading-and-3d.md). On narrow screens, stack photo, crop dropdown, action and photo tips. Allow phone library or camera choice through the accessible picker.

## 04 · Result with diagnosis

**Geometry.** Use a compact breadcrumb and textual result status, a disease headline at about 56–64 px, and context for crop, severity, confidence and top predictions beneath. From y≈295 lay out two equally sized evidence panels, each about one-third of the section, and a slightly narrower guidance rail. Preserve identical geometry for the original and attention images. Beneath the grid add the wide greenhouse strip and adjacent “See how it develops” with eligible next actions.

**Evidence and treatment.** The reference is an example of Tomato Early Blight. Use the returned scan title, original image, available attention, actual status and confidence. Keep the original when attention is absent. Use the existing healthy and missing-result behavior. Render available symptoms, organic guidance, chemical guidance and prevention in readable accessible disclosures. Keep the existing advisory copy, measured confidence where supported, and real top predictions. Preserve scan reload using the query string.

**Path semantics.** Keep the browser Field route and the Windows VR launch as separate affordances with clear labels. Use “Enter the field” for the browser path where room allows, and identify installed VR through “Treat this plant in VR” or adjacent text. Preserve pairing, eligibility, opening, opened, failed and saved treatment states. Keep “Detect another.” At narrow widths put metadata first, then evidence as a stacked pair or a toggle in a fixed-size photo area. Follow with guidance and greenhouse handoff.

## 05 · Uncertain result

**Geometry.** Keep the header and result breadcrumb. Put a small ochre textual “Uncertain” status above a large serif message. Follow with a direct next action and the three photo tips. Put the user's photo next to a readable example in matching sized frames, captioned “Your photo” and “A clear leaf photo.” Place “Take another photo” and “Back to home” just below. At a narrow size keep the retake action close to the message and preserve the order and labels of the examples.

**Variants.** Keep distinct system messages for unclear, non-leaf and wrong-crop outcomes. Use a suggested crop only when the server returns one. Do not infer a diagnosis, severity or attention. If no original can be displayed, keep the example, guidance and retake action. Defer eligibility for either field path to the real result.

## 06 · Field disease selection

**Geometry.** Give the left chooser about one-third width with breadcrumb, large serif “Explore the field”, short lead, optional crop selector and the real supported disease list. Use selected-row pale teal, a leading teal rule and a chevron. On the right, tightly crop the same 3D atlas around the tomato greenhouse, using a single labelled pin. Under it place an eye-level preview around 4:1 with “Enter the field” aligned toward the right. Keep “Scan a leaf” near the chooser.

**States.** Reserve list space while loading and show selection semantically. Keep a selected case tied to a real identifier. Display no matches and actionable errors in the list area. Keep the artwork decorative if the selection is unavailable, with usable scan and navigation links. On mobile keep selection and venue action close in reading order and make the art secondary to the chooser.

## 07 · Field greenhouse explorer

**Geometry.** Preserve the shared bar and add a compact context band from y≈72–144, with disease context left and “Pick another disease” right. Give the visual viewport roughly 72% width and a cream rail around 370–440 px on desktop. The view should have a clear ground-level vanishing point and foliage from near to far. Place the inspect cue on one actual relevant leaf, and the view instruction, look or recenter controls, expand if implemented, and “Enter in VR” along the bottom. Set “Environment” in serif. Keep the range controls, reset and vertical cycle timeline aligned with a fine divider.

**Actual cycle.** Label Temperature, Leaf wetness and Humidity with their live values and units. Preserve native input min, max and step. Render applicable stages and their real completed, stopped and not-reached meanings. Keep all published flat or immersive footage choices. The greenhouse imagery provides an accurate matched preview or illustrative poster. Ensure it is identified as a preview when necessary. If no footage or scene is available, explain the state and keep the conditions and cycle usable. Include an inspect marker only if it acts on a relevant view or annotation.

**Narrow.** Use a stable viewport around 4:3 or up to 420 CSS px tall, then the conditions and cycle. Support normal vertical scroll outside the scene and discrete controls when dragging is unavailable. Keep stage and condition information readable as text without the scene.

## 08 · Meta Quest demo

**Geometry.** Put the small Field breadcrumb above the intro. Position “Walk the farm in VR.” around x≈52, y≈150. Let the text column take about 36–42%, and let the shared greenhouse continue on the right through a clay arch into a Visiting Lab. Use a naturally angled physical threshold for depth. Keep the headset and controllers on a low limestone shelf so the door and crops remain legible. Arrange launch controls in a clear hierarchy and add the short ordered setup steps under a fine rule.

**Actual links.** Highlight “Send page to Meta Quest” where the existing HTTPS send link is valid. Preserve the installed app link and optional APK download from configuration. Use the browser-aware direct path on Quest and the real fallback behavior. On narrow screens put setup and allowed actions before the image if space is constrained. Custom protocols open only from explicit user actions.

## 09 · About and methodology

**Geometry.** Use a sparse cream editorial reading grid. An optional left greenhouse photo ribbon gives context while leaving sufficient width for text. To the right of the intro show aligned example field and lab photos with real text labels. Make the measured performance table the next major section and follow it with the explanatory note and text-based model pipeline. Preserve method details and context below the reference crop.

**Data.** Keep real measured values, model version, dataset distinctions, and supporting sections from the current About route. The reference illustrates the dataset layout. Feed it values from the current app and display a localized unavailable state where necessary. Keep the paired example imagery visually subordinate to actual metrics. On narrow screens prioritize the intro and measured values and let long labels wrap.

## 10 · 404 route recovery

Use the same header, then a spacious split. Place the status number, pine serif heading, existing route context and actions in the left area. “Back to home” leads, with “Scan a leaf” outlined beside it. On the right, a small lifted terrain cutaway shows a forked trail and the map’s greenhouse in the same light. Treat the single pin as decoration unless it has a real destination. Render the unknown route as escaped text and preserve available navigation. On narrow screens show both recovery actions before the scene.
