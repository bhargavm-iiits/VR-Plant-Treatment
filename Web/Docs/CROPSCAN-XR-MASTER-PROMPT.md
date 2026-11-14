# CropScan XR · From Field to Leaf

Copy the prompt below into the coding agent you want to build the redesign. Run it from the repository root and give it access to this folder. If the agent runs outside the repository, attach the ten reference PNGs and the skill folders as well.

[Open the numbered visual overview](design-references/field-to-leaf-overview.jpg)

## Paste-ready master prompt

```text
Redesign and implement the existing CropScan XR web frontend as From Field to Leaf. Act as a senior frontend engineer and 3D web designer. Deliver a working site, integrated with the existing API and XR flows. Use the supplied From Field to Leaf screens as the visual target. Use the repository as the source of truth for data and behavior.

PROJECT AND REFERENCES

Work in Backend/frontend, the existing React 18, Vite, React Router 6, Tailwind and CSS project. The prompt and references live in Web/Docs. Read both SKILL.md files in Web/Docs/skills or invoke $cropscan-frontend and $cropscan-web-motion if those skills are installed. Follow their references for detailed geometry, motion, loading, artwork, and verification. Open each corresponding PNG in Web/Docs/design-references at full size before working on that screen. Use the overview only to understand the overall journey. The supplied PNGs are design references. Build the navigation, labels, controls, forms, cards, graphics overlays and data with real DOM and load separate art-only assets.

Preserve the existing endpoints, routes and query parameters, environment settings, scan ownership and session behavior, all five languages, and working WebXR, Windows VR pairing and Quest handoff. Keep API responses, photos, confidence, stage status, access gates, treatment text, and measured model values live. A missed or uncertain model result must never look like a confirmed diagnosis. Do not use the example scan, mock weather state, or demo geometry as evidence about a real scan. Keep the current backend, Unity project, and deploy configuration outside the scope of the design implementation.

THE EXPERIENCE

Show the same tomato greenhouse at three scales. On Home it is a small glass structure in a tactile 3D landscape with rice terraces, maize, a crooked path, and exposed earthen edges. A terracotta waypoint opens a small photo preview of the greenhouse aisle. On Field, zoom into the greenhouse and show it again at eye height. In an eligible diagnosis, make the greenhouse image the visual handoff from leaf evidence to the disease cycle. Preserve warm afternoon light, pine vegetation, clay ground and the cream topographic plane. Give the main scan action first priority on every surface where it belongs.

Use warm ivory #F5F0E6, pine ink #132620, teal actions #126456, terracotta markers #CC592C, and fine sand rules #D8D0C0. Use DM Serif Text for the wordmark and display headings, Instrument Sans for body and controls, and the app’s existing Indic font fallbacks. At the 1586 × 992 desktop reference size use a 72 px shared header, about 44–56 px outer gutters, compact text links, and warm-white panels with restrained 10–14 px radii. Keep each screen’s distinctive composition. Avoid replacing the compositions with a card grid or adding generic glass, a logo intro, decorative labels, or web-game chrome.

SCREENS TO BUILD

01 Home hero `/` (01-home-hero.png). Keep “From field to leaf.” as an oversized pine serif headline on a clear left area, followed by the two-line introduction and Detect Disease / See VR Demo actions. Take the farm across the right and lower canvas with visible contours behind it. Place one greenhouse waypoint, one ground-level preview with Enter the field, and compact zoom controls. Show the start of How it works at the bottom of the desktop hero. Keep the headline, controls, and nav readable while the rest of the map art can crop.

02 Home below the fold `/` (02-home-scroll.png). Use three ordered image-led steps for photo upload, result, and field exploration, connected by a restrained terracotta route. Follow them with the existing coverage figures and a horizontal supported-crops display. Include every existing supported crop in an accessible display. The six objects in the reference establish the featured visible row.

03 Detect `/detect` (03-detect.png). Match the large bordered preview on the left and the crop selection, analysis action and photo tips on the right. Also build empty picker, drag-over, valid selection, remove, invalid or unreadable image, upload pending, server error and retry states. Use the existing file validation, optional crop choice and API submission path. Keep the selected photo and crop available when a retry is possible. Use a real file input accessible on desktop and mobile.

04 Result `/result?scan=…` (04-result.png). Use a small status/title area above matched crop photo and model-attention panes. Show real confidence, top predictions and severity only when supported. Fit treatment guidance in the right rail and follow with the greenhouse handoff. Preserve browser Field and installed VR as distinct, correctly gated actions. Cover healthy, missing scan, API failure, and unavailable model attention using the existing result logic. A refreshed deep link must load from the existing API.

05 Uncertain result (05-result-uncertain.png). Convert the result layout into a clear retake path. Place the user's photo next to one example of a clear leaf photo. Preserve distinct messages for unclear, not-a-leaf and wrong-crop outcomes. Give Take another photo priority. Show only information the system actually knows. Respect the existing gates for browser Field and installed VR.

06 Field selection `/field` (06-field-browser.png). Put the crop selector and disease list on the left, a cropped map relief with the greenhouse marker on the right, and a matching eye-level preview with Enter the field below. The list and availability come from the current disease API. Display selection visibly, keep links deep-linkable and support loading, no matches, and API failure.

07 Field explorer `/field?disease=…` or `/field?scan=…` (07-field-explorer.png). Open in the eye-level greenhouse or the available authentic footage for the selected case. Keep panorama, inspect marker, drag or discrete look controls, and Enter in VR in the left working viewport. Put Temperature, Leaf wetness, Humidity, Reset to ideal, and the infection-cycle timeline in a clear right rail. Retain the existing cycle engine, stage meanings, footage choices, XR entry and fallbacks. For cases without footage, keep the cycle controls usable.

08 Meta Quest demo `/quest-demo` (08-quest-demo.png). Use the text-led left pane with the setup steps and launch options. On the right show the shared greenhouse passing through a clay arch into the Visiting Lab. Keep headset/browser capability, HTTPS send behavior, installed-app link and APK availability governed by the actual Quest helpers.

09 About `/about` (09-about.png). Use the editorial split with field and lab photos, measured performance table, and model pipeline. Keep the real API metrics, dataset labels, coverage context, model version and existing methodology. Show clear loading and unavailable states. Do not type example percentages into production. The design PNG deliberately omits live scores.

10 404 (10-not-found.png). Keep the shared header, the heading “That page does not exist.”, Back to home and Scan a leaf. Put one forked path relief on the right. Preserve the unknown path in the recovery message where appropriate. Keep the user one action away from a useful route.

MOTION AND LOADING

Show the shell and useful content immediately. Render the hero with a reserved-size static artwork first. Transition artwork layers as assets become available, and keep the primary actions live throughout. Use restrained native scrolling with a sticky header. On desktop only, add shallow terrain/photo parallax and draw the route between the three How it works steps as they come into view. Use small hover and press responses on controls. The waypoint opens one anchored preview, with a tight 180–240 ms transform/opacity reveal. A user-initiated Field transition can match the map preview to the ground-level poster over roughly 360–560 ms. Respect back/forward and honor explicit close and Escape.

Design one shared loading language inside the site's cream shell. Use a restrained contour-and-waypoint motif and reserved skeletons where data is pending. For scan analysis, keep the real photo and crop visible, disable repeat submission, and use a small indeterminate annotation plus exact status. For result, Field and About, keep available content usable and indicate pending work in its own region. Announce real transitions with accessible status. Do not stage fake progress or force a timed preloader. Recover from failed or unavailable WebGL, slow assets, unavailable footage or API errors with a static poster, relevant status, and working controls. Disable decorative parallax, sweep animation and spatial transitions for reduced motion. Give touch, keyboard, and browser Back the same usability as mouse navigation.

IMPLEMENTATION AND ACCEPTANCE

Use the existing JS/CSS conventions and API client. Keep the initial bundle’s existing 180 KB gzip budget and keep Three.js out of the landing dependency graph and preloads. The default homepage should be achieved with responsive image layers, CSS and DOM. Reuse the existing lazy Field renderer and add art assets only as needed. Read the art handoff notes before generating terrain, greenhouse, crop-photo and Quest art. Avoid using reference screenshots or example scan data at runtime.

Develop the shared shell, tokens and art first, then landing and scan/result, then Field/VR and supporting routes. Make the design work at 1586 × 992, 1440 × 900, 1024 × 768, 768 × 1024 and 390 × 844. On narrow widths, move priority content first and stack the work surfaces. Keep the page readable at 320 CSS px, high zoom, and in all five locales. Follow the accessibility contract in the design system reference and run its verification table. Verify visible keyboard focus, meaningful headings and names, adequate contrast and hit areas, real form behavior, and animation fallbacks. Never require drag, hover, a motion preference, or WebGL to reach a core action.

Capture desktop screenshots for each of the ten reference frames. Compare position, crop, spacing, headings, action order and presence of the essential visual motifs against the originals. Capture mobile proof for Home, Detect, example Result and active Field. Check reduced motion, loading, failure recovery, and direct-route refresh. Run the repo's lint, unit, build/budget checks and the applicable Playwright tests. Use the configured backend when available. Label fixture-based browser proof as such and report what still depends on hardware or deployment. Finish with a concise implementation summary, paths to proof, and any remaining visual or runtime gaps.
```

## Detailed implementation files

- [Frontend skill](skills/cropscan-frontend/SKILL.md) with the [design system](skills/cropscan-frontend/references/design-system.md), [screen-by-screen layout](skills/cropscan-frontend/references/page-specifications.md), and [integration and verification checklist](skills/cropscan-frontend/references/integration-and-qa.md)
- [Motion and 3D skill](skills/cropscan-web-motion/SKILL.md) with the [scroll, transition, and loading choreography](skills/cropscan-web-motion/references/motion-loading-and-3d.md) and [art generation prompts](skills/cropscan-web-motion/references/asset-prompts.md)

The PNGs in `design-references/` are desktop design targets. Motion, loading, additional data states and responsive layouts are specified in the skill references.
