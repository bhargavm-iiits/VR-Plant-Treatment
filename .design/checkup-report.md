# CropScan XR local site checkup

**Scope** — 2026-11-16. Read-only browser check of Home, the Detect entry screen, Field selection and one live tomato case, About, Swagger and API health. Compared Home with the approved [landing hero](../Web/Docs/design-references/01-home-hero.png) and [opening motion brief](../Web/Docs/skills/cropscan-web-motion/references/motion-loading-and-3d.md).

The running homepage closely matches the approved composition and its map controls work. The opening falls short of the terrain reveal in the motion brief, and the Field entry link clips on a shorter desktop viewport.

## Verified behavior

- At 1586 × 992 the entire preview action fit in the hero. At 390 × 844 the browser used the 660 × 400 narrow image, showed a separate full-width preview strip and had no horizontal overflow.
- The greenhouse pin toggled the preview. Zoom moved the atlas to the 1.07 stop. The Detect page opened to a labeled file picker with format and size guidance.
- Field returned 14 cases. In Tomato Early Blight, taking the leaf wetness slider to 0 h stopped the cycle at Germination. Reset returned the case to its completed state.
- Swagger loaded. API health returned `ok`, with the `bioclip_vit_b16@v6` model loaded, database and storage reachable, and 14 cycles loaded. About rendered server-reported values of 77.1% for PlantDoc field images and 99.4% for PlantVillage lab images.

## Scorecard

| Dimension | Score | Evidence |
| --- | --- | --- |
| Visual fidelity | 3 / 4 | Terrain art, typography, color, waypoint and below-fold layout match the design direction |
| Opening motion | 1 / 4 | Preview entrance and scroll movement ship. The approved terrain entrance is absent |
| Responsive task access | 2 / 4 | Mobile and reference desktop passed the entry check. Short desktop clipped the Field CTA |
| Live data and task feedback | 3 / 4 | Health, About, the case list and the condition-to-cycle interaction worked |

## Findings

| ID | Severity | Area | Location and evidence | Proposed correction |
| --- | --- | --- | --- | --- |
| CS-01 | High | Desktop access | At 1536 × 776 the hero ended at y=704, while the Field link ran from y=686 to y=742. The visible strip excluded the link label. The preview is positioned inside the same cover-sized stage as the illustration and the hero clips overflow. [Hero CSS](../Backend/frontend/src/index.css#L309) · [Preview CSS](../Backend/frontend/src/index.css#L360) | Keep the pin on the scaled atlas and anchor the preview within the visible hero bounds. Shorten its image or switch to a compact strip at short desktop heights. Retest all zoom stops. |
| CS-02 | Medium | Opening motion | The hero renders a WebP and can drift vertically on scroll. Its computed entrance animation list contained only `preview-in`, a 220 ms preview entrance. [Home](../Backend/frontend/src/pages/Home.jsx#L70) · [Motion CSS](../Backend/frontend/src/index.css#L364) | Animate the terrain in distinct contour, relief, waypoint and preview beats. Keep the headline and both main actions stable from first render. Preserve the still reduced-motion path. |
| CS-03 | Medium | Field continuity | Home's greenhouse preview links to the unfiltered picker. The picker labels the photo as a tomato greenhouse while its default entry chooses Apple Scab from the first case in the list. [Homepage link](../Backend/frontend/src/pages/Home.jsx#L179) · [Field entry](../Backend/frontend/src/pages/Field.jsx#L358) | Carry the tomato context from the Home waypoint or label the entry explicitly with the chosen disease. Keep the general Field navigation open to all crops. |
| CS-04 | Medium | API readiness | Storage was reachable in this run. The aggregate health status checks the model and database, while a storage failure yields scan HTTP 502. [Health response](../Backend/services/api/app/routers/health.py#L38) · [Scan failure](../Backend/services/api/app/routers/scans.py#L131) | Use the storage flag in scan readiness or expose a dedicated readiness outcome for scans. Preserve any broader liveness check that has existing consumers. |

## Order of work and verification gaps

Address CS-01 first, then CS-02 and CS-03 together so the opening leads into a coherent Field path. Check CS-04 with a controlled storage failure outside the live app. Scan submission, Result states, headset launch, reduced-motion browser behavior and a full keyboard traversal remain unverified.

**Scoped verdict** — Block approval of the homepage's Field entry on short desktop viewports until the clipping is fixed. The API and live Field cycle worked in the tested environment.
