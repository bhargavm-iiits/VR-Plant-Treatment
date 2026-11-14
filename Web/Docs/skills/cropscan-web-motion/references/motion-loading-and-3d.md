# Scroll, route transitions, loading and 3D

The [Home hero](../../../design-references/01-home-hero.png) and [greenhouse explorer](../../../design-references/07-field-explorer.png) define the camera's two endpoints. The motion keeps geography legible and exposes real status. Use the [shared tokens](../../cropscan-frontend/references/design-system.md) on motion surfaces and placeholders.

## Motion vocabulary

| Event | Target duration | Range and easing |
| --- | --- | --- |
| Button or nav feedback | 120–160 ms | Color and border, optional 1 px press, standard ease-out |
| Photo or content settle | 220–360 ms | Opacity and up to 12 px translate, `cubic-bezier(0.22, 1, 0.36, 1)` |
| Waypoint preview open | 180–240 ms | Opacity with up to 8 px travel, same ease-out |
| Waypoint preview close | 120–180 ms | Opacity, same anchor and no change to page flow |
| Map zoom control response | 220–300 ms | Scale 1.00 to about 1.08 to 1.16 between zoom stops |
| Intentional atlas-to-field handoff | 360–560 ms | Match area and image crop, optional, `cubic-bezier(0.4, 0, 0.2, 1)` |
| Field cycle redraw | 120–220 ms | Move or fill the current stage indicator, text updates immediately |

Favor transform and opacity. Avoid layout changes as a visual effect. User input, navigation and API state updates always retain priority over the motion. Finish or cancel in-flight animation on repeat interaction and route changes. Use the existing frontend stack. Implement the simple motions with CSS and observe only the relevant sections. Choose progressive enhancement for native scroll/route animation APIs and retain the static path when unsupported.

## Home first render

1. Paint the cream background, readable header, heading, two primary decisions and right-side map footprint in the initial layout. Keep heading, buttons and field preview action stable. The design's startup state feels like a partially rendered chart, with faint static contour lines in the art area.
2. Show a usable static atlas poster as soon as decoded. A small art-layer transition may start at 0.98 scale and settle to 1 over about 300 ms. The art remains stationary for a user with reduced motion. Reveal any extra translucent cloud/shadow layers with an opacity change only if genuinely helpful. Preserve the small greenhouse and relative anchor across lower-quality and final image variants.
3. Start with one selected greenhouse preview as shown in frame 01. Load the poster independently. The route from the pin to the eye-level preview remains in the DOM or SVG and stays aligned, with a single dot or leader rather than an animated trail on repeat.
4. After asset readiness, the hero still frame should closely match frame 01. No fixed-duration title reveal, animated word-by-word headline, intrusive intro movie, ambient cursor or splash that postpones actions.

## Home scroll storyboard

Use ordinary browser scroll. Do not capture wheel or touch for whole-page scrolling. Keep the shared nav sticky for the content journey. Prefer a threshold tied to what is visible over global page percentages.

| Scroll beat | Visual change | Stable elements |
| --- | --- | --- |
| Hero start | The preview shows the aisle inside the pinned greenhouse. Header has a fine bottom rule. | Copy and main actions in normal DOM flow |
| Hero moves upward | While most of the artwork remains visible, background contours shift up to 6 px and the terrain layer up to 16–24 px. Keep the effect shallow and stop computing when offscreen. | Heading, controls and user scroll position |
| Leaving hero | Keep the pin and line tied to their map coordinates. Move the atlas normally out of the viewport. A subdued nav shadow can develop once the hero lifts past the header. | Native Back, scrollbar and page anchors |
| Walkthrough arrives | At roughly 20–30% section visibility, lift the three image regions no more than 8–12 px and fade them in over ~260–360 ms, optional 45–60 ms stagger. Text remains readable. | Underlying source order and section layout |
| Route through walkthrough | Draw the small terracotta connecting lines across the gaps as their related step enters view. A partial-to-full stroke over ~300–420 ms is sufficient. Run once per visit when eligible. | Step headings and photos continue in normal flow |
| Metrics and crop shelf | Use a single subtle appearance on the crop art if desired. Display actual counts immediately. Keep figures and crop text stationary. | Metrics, featured crop names and “Start a scan” |

If CSS or a reveal observer fails, default to content visible. Disable scroll parallax on narrow phones and for reduced motion. Never pin long sections solely to prolong an animation. On a compatible wider pointer device, minor optional pointer parallax stays within 1–2° or 8 px and resets gently on leaving the art. The pin and preview must continue acting as a single anchored region.

## Waypoint, zoom and field handoff

- Base the pin on normalized artwork coordinates or a measured overlay anchor, including the transform origin and responsive crop. Use actual shape and size, not an absolute screen position that floats away on zoom. Recalculate when layout or art variant changes. For mobile the anchored panel can become a simple preview below the art.
- Bind the pin to one button with an accessible name, a clear visual selected state and `aria-expanded` if it toggles a region. Opening reveals the preview adjacent to the pin or in the reserved pane. Keep tab order logical. Close through the same marker, Escape when a dismissible preview owns focus, or a real dismiss control. Return focus to the trigger when dismissed. Leave Enter the field as a separate link that works without hover.
- Provide the two zoom buttons as discrete actions and clamp to usable crop bounds. Zoom the art and its pin together, preserving the user’s ability to read nearby controls. A reset or an equivalent way back to initial view is helpful when alternate zoom states hide geographic detail. Ordinary page scroll continues to work over the atlas.
- On explicit Enter the field, optionally transition a background snapshot or the same greenhouse poster from preview bounds to the Field region. Resolve the destination through the existing route. Make new screen content usable as soon as rendered, up to a few hundred milliseconds later for the optional transition. An image timeout, Back navigation, initial deep link or reduced motion uses a simple cut and the normal focus/scroll policy.
- Restore Home scroll and the selected atlas state when returning if it was previously established by the user. Direct route navigation uses its own initial focus and scroll behavior. Prevent detached overlays, orphan timers, leaked observers and repeated preloads on rapid back/forward.
- On Field, preserve existing drag/look semantics and add or retain discrete keyboard/touch equivalents when the renderer supports them. Give the current visual state a text description. Handle touch locally, with normal vertical scroll on the rest of the page. Controls, keyboard shortcuts and XR consent keep their existing scope.

## Loading screen and skeletons

### Shell and initial artwork

At 1586 × 992 keep the regular 72 px header visible and active. Use the same page background and gutter as the destination. For Home, display the real hero title and actions immediately. Reserve the map to y≈920 on desktop. The still placeholder is a cream topographic background, a muted 3D silhouette on the right and a terracotta locator if the target is known. Use one low-key status such as “Loading field preview…” only if that region would otherwise be ambiguous. Keep the preview action clickable when a destination is known, and a lightweight fallback image when it is still decoding.

For a genuinely pending route view with unavailable body, keep the shell and the destination breadcrumb if available. Place a still 72–88 px contour vignette and 8–10 px terracotta dot above one serif loading label at 26–34 px, aligned with the destination's content start or centered inside its reserved media region. Use one secondary line only if it explains what is pending. Preserve an inert, correctly sized skeleton for replacing complex data. Use 8–12 px skeleton radii and fine sandstone contrast. Keep skeletons inaccessible to screen readers and mark only the active data region busy. A quick response can skip the placeholder while the real action status is conveyed.

### Route and action states

| Pending operation | Keep visible and usable | Pending display and outcome |
| --- | --- | --- |
| Home map or aisle poster | Header, copy, both CTAs, section footprint | Static topography or poster, bounded opacity on decode, retain poster/links on media failure |
| Image decode | File picker, format and photo tips | Local status in preview, validated photo or useful inline corrective message |
| Scan submission | Chosen image, crop, correct action context | “Analysing…” on disabled submit and small fine scan cue within the image, use real busy flag, no percent without progress data. Navigate on success. Recover and keep chosen values on error. |
| Result deep link | Header, breadcrumb and fixed evidence/rail footprints | “Loading the result…” and geometry-matched panes. Show real result, missing/deleted scan, or retry/error once known. |
| Field case list or lookup | Header, scene poster, existing meaningful selection | “Loading diseases…” or a localized case status over small list skeleton, then eligible cases, no matches, or retry. |
| Field renderer or media | Case title, loaded condition controls and cycle content | “Loading the field…” over the reserved aisle poster. Fall back to poster and explain renderer/video availability if loading fails. Keep browser UI independent of immersive capabilities. |
| Installed VR or Quest handoff | Known route, device eligibility and a return path | Existing opening/waiting/opened/error state from metadata. Keep launch, pairing and recovery status real. |
| About model metadata | Editorial intro, photo pair and section title | Quiet 2–4 table row placeholders until real values or localized unavailable/error messages resolve. |

For an image scan cue, use a single small mask or sweep inside the crop brackets, at most about 1200–1600 ms for one gentle pass while pending. A static focus outline and status is enough thereafter. For a slow unresolved operation use the application’s real pending/error model and relevant recovery links. Avoid inferring diagnosis stages, exact percentages, or success from an elapsed timer. Use restrained indeterminate motion and stop it when the pending operation ends.

Unavailable WebGL or footage should expose the readable current context and existing slider/timeline functions with a clear static status. Media may retry without resubmitting the user’s scan. Use `aria-busy` and a modest polite status region for real transitions, with actionable error text close to the initiating control. Ensure placeholder content is non-focusable, announced label strings are localized, and the user does not lose focus during an asset load.

## Rendering and performance

Home is a poster-first layered render with a little CSS. The runtime Field view keeps the existing lazy Three.js/R3F/XR and video stack. Reuse its error boundary, playback options, and XR entry. Give canvases explicit width and height or aspect ratio, and do not introduce a WebGL requirement for nav, scan, or controls. Prefer a responsive optimized poster for route previews. Avoid shipping the reference PNGs or production fixture scans in the application.

Use a frame loop only while a scene needs it. Passive layers can render on demand, and ongoing motion should pause offscreen or when the tab is hidden. Cap renderer pixel ratio conservatively, especially on mobile, and honor existing settings and teardown. Dispose assets owned by added views, cancel subscriptions, and guard asynchronous image or media work against route and source changes. Test slow network, reduced motion, no WebGL, and showing the page again after backgrounding.

With `prefers-reduced-motion` enabled, show a still hero, full route connectors and immediately visible content. Change selection and navigation without spatial motion. Keep new status and field controls usable, and preserve media playback initiated by the user. Avoid scroll smoothing that overrides the user's preference.
