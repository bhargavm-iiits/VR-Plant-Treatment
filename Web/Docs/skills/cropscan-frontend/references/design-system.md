# From Field to Leaf design system

Use these values as a starting point at the 1586 × 992 reference size. When approximation is visible, compare to the [full-size screen for the route](page-specifications.md). Reference geometry is measured in viewport CSS pixels at that size. Use fluid layout between sizes and let longer translated strings wrap.

## Art direction

- Pair a cream topographic atlas with an eye-level tomato greenhouse. The atlas forms the Home and Field selection views. The aisle frames the result handoff, active Field and Quest surfaces. Keep the scale change understandable by reusing the hoop roof, same row orientation, warm light and waypoint.
- Use terrain texture around the atlas and at the bottom or edges of content pages. Keep it almost imperceptible where there is text. Use real edge lighting and shallow, warm shadows on the art. Keep UI surfaces flat with quiet borders.
- Use teal for the action that continues the workflow. Terracotta identifies a location or current timeline step. Ochre can identify a caution. Pair each status color with words or a clear accessible name.
- Preserve the route layout even if a photo, WebGL, video or font takes longer to load. Page structure and product actions must stay legible over the fallback.

## Tokens

| Role | Target | Use |
| --- | --- | --- |
| Canvas | `#F5F0E6` | Warm ivory page and primary nav |
| Surface | `#FBF7EF` | Quiet work surfaces on Detect, Result and Field |
| Ink | `#132620` | Display headings, body, labels and icons |
| Muted ink | `#4F5B51` | Explanatory details and metadata |
| Action | `#126456` | Main buttons, links and the active nav rule |
| Action hover | `#0E5146` | Hover and primary enabled pressed progression |
| Selection | `#DDEBE3` | Crop and disease selection fill, always paired with a rule, outline, weight or icon |
| Border | `#D8D0C0` | Decorative hairlines, sparse card and image borders |
| Control border | `#4F5B51` | The edge of inputs, dropdowns, the drop zone and any control identified by its boundary |
| Focus ring | `#132620` | Keyboard focus outline, with an ivory inner ring over artwork, footage and filled teal |
| Marker | `#CC592C` | Map waypoint, selected stage and route connector |
| Caution ink | `#7A4717` | Uncertain status and recoverable data messages |
| Caution surface | `#F6E9D5` | Small pending-or-uncertain state surfaces |
| Failure ink | `#8D362A` | Actual action failure with matching text |

Measured against canvas, ink is 13.9:1, muted ink 6.3:1, action 6.2:1, caution ink 6.7:1 and failure ink 6.9:1, so all are safe for body text. Three tokens are not text or boundary colors. Border is 1.35:1 and Selection is 1.08:1 on canvas, so neither can be the only thing that marks a control edge, a selected row or keyboard focus. Marker is 3.67:1, which suits pins, connectors and text of 24 px or larger, and fails for smaller text in either direction (terracotta on ivory or ivory on terracotta).

Check contrast against the rendered surface, especially where media, selected or disabled fills appear. The terracotta pin can be filled and ringed in ivory. Use a pine or dark terracotta text label on a pale surface where the pin needs a caption. For text over footage, use a quiet dark scrim limited to the caption band.

Use a 4 px spacing base. Typical gaps are 8, 12, 16, 24, 32, 44, 56, 72 and 96 px. Use 8–10 px radii on inputs and buttons, 10–14 px for photos and small panels, and 16 px for the Home preview. Use 1 px hairlines. Default shadows are absent or `0 4px 18px rgba(31, 34, 25, 0.09)` on a floating preview. Physical shadows belong in the render.

## Typography

| Role | Family | Desktop target | Narrow target |
| --- | --- | --- | --- |
| Wordmark | DM Serif Text | 34–40 px, line height 1 | 25–29 px |
| Landing hero | DM Serif Text | 92–104 px, line height 0.98–1.04 | 42–56 px, line height 1.02–1.08 |
| Interior H1 | DM Serif Text | 54–72 px, line height 1.02–1.08 | 36–46 px |
| Section title | DM Serif Text | 38–52 px, line height 1.06 | 28–36 px |
| Subheading or card title | DM Serif Text | 25–36 px | 23–29 px |
| Intro body | Instrument Sans | 21–28 px, line height 1.25–1.45 | 16–19 px, line height 1.4–1.5 |
| Main body | Instrument Sans | 16–18 px, line height 1.45–1.6 | 16 px minimum where feasible |
| Navigation and buttons | Instrument Sans | 17–22 px, weight 550–650 | 15–17 px, form fields at least 16 px |
| Labels and captions | Instrument Sans | 13–16 px | 13–15 px |

Use the existing Noto fallback chain for Telugu, Devanagari, Tamil and Kannada, with higher line height if a script needs it. Avoid relying on an exact English line count in controls. Use an explicit editorial break on the English hero only if it reliably matches the target and allows narrow or translated layouts to reflow. Use a fallback metric or local fonts so the heading and action row stay close when fonts swap.

## Layout grid and breakpoints

- At 1586 px start with 44–56 px page gutters, a 12-column grid, and 24–28 px grid gaps. The main detail screens are centered with a practical maximum width around 1496 px. Let atlas and panorama art bleed according to their own page rules.
- At 1440 and 1280 px keep comparable visual rhythm, scale display type with `clamp()`, and use at least 32–44 px gutters while the two-column composition fits.
- Around 1024 px use 24–32 px gutters, reduce hero font and preview size, and use a narrower split only if work panels remain comfortable. A Field rail should get roughly 300 px when side by side. Stack before its labels and range controls crowd.
- From roughly 768 to 960 px let task content choose the breakpoint. Stack Detect and Result guidance if the work surfaces become narrow. Put the Field control rail below the panorama when the labels or viewport get constrained. Keep atlas callouts anchored or use a preview strip outside the artwork.
- Below roughly 768 px start with 16–20 px gutters and one reading column. Use a compact brand and language row, with the existing Home, Detect, Field and About navigation in a second row. Preserve all destinations and active-state affordance. Avoid masking long localized labels or making page content scroll horizontally.
- At 390 × 844 show the main heading and primary action before the deepest art crop. At 320 CSS px, 200% zoom, and with long translations, allow routes, form groups, action pairs and metric rows to reflow. Photo and canvas viewport sizes may change to fit.

## Shared header

At the reference viewport, use a 70–72 px header with the brand starting at x≈44. The contour leaf outline is roughly 44–48 px wide, followed by a 12 px gap and the serif wordmark. Keep the four center links on a level baseline with visible spacing of roughly 36–48 px. Align English and its chevron near the right gutter. Apply the teal 3 px active underline below the link and a 1 px sand rule along the header bottom. Route activity is indicated visibly and with `aria-current` where appropriate. Use the actual locale selector, accessible name and brand link.

After scroll keep the bar opaque enough to remain readable and add only a restrained rule or shadow. Set scroll padding or target margins to keep named sections and keyboard focus from hiding under the sticky header. At narrow sizes, preserve the two-row height so reflow does not jump.

## Controls and repeatable composition

- **Primary action.** Teal filled with ivory text, horizontal padding 20–26 px and 48–56 px high. Full-width inside the scan form where shown. A drawer, low-density landing action pair, and a dense media toolbar can use different widths while keeping consistent text and material.
- **Secondary action.** Pine text with a 1 px pine border and pale or clear fill. Use an underlined link for a tertiary or secondary navigation action with low visual weight. Keep focus visible on each control independent of color.
- **Input and disclosure.** Native form groups with connected labels, gap 8 px, field height around 48 px, a 1 px Control border, and sensible input widths. Give accordions a text label and visible disclosure state. Avoid clickable nested controls. Click, keyboard and touch must reach all actions.
- **Waypoint and preview.** Terracotta dot or map pin with pale ring, label and a visible pointer and keyboard focus state. The hit region should be around 44 × 44 px even if the marker is smaller. Anchor to the same location on the art when it scales, then move visual detail into an ordinary preview panel at small widths.
- **Image panels.** Tight crops on an ivory or sand surface, stable aspect ratio and small captions. Keep key subjects inside the mobile crop. Match geometry on paired result panes. Preserve access to original evidence and avoid putting analysis overlays on the wrong leaf or on a placeholder.
- **Data indication.** Keep design demo states out of production data. Pair confidence and timeline bars with real values/status and text where available. Put supplementary labels near the thing they describe.

Choose semantic icons and routes rather than ornamental extras. The focus ring is a 2 px solid Focus ring outline with 2–3 px offset, shown on `:focus-visible`. Over artwork, footage or a filled teal control, add a 2 px ivory inner ring so the outline holds 3:1 against whatever is behind it. Use touch targets around 44 × 44 px and check hit areas on the map, disclosure and dismiss controls. For persistent state, use underline, outline, weight, text or shape in addition to color.

## Accessibility contract

These rules apply to every route. Route every label, alt text and announced string through the localization layer.

### Page structure and route changes

- Each route has one `h1`, sequential heading levels below it, and `header`, `nav`, `main` and `footer` landmarks. The first focusable element is a skip link to `main`.
- On each client-side navigation, set a localized document title that names the route and, where known, the disease or state. Move focus to the new `h1` (with `tabIndex={-1}`), except on browser Back and Forward, where the browser's restored scroll and focus stand.
- Keep `<html lang>` in step with the selected locale. In the language selector, give each option written in its own script the matching `lang`.
- Breadcrumbs are a `nav` with a localized label, an ordered list, and `aria-current="page"` on the last item. The active header link uses `aria-current="page"`. Narrow and wide layouts render one navigation landmark, not two.

### Images, artwork and scenes

- The atlas, terrain, contour lines, connectors, forked-path relief and photo ribbons are decorative: `alt=""` or `aria-hidden="true"`.
- The user's leaf photo has an alt that says it is their uploaded photo and names the crop when known. The example photo on the uncertain result has an alt describing a clear single leaf.
- The model-attention pane needs a text equivalent next to it, because the overlay is the evidence. State in words what the result data supports, such as that highlighted regions show where the model focused. When attention is unavailable, say so in text.
- Crop cutouts carry the crop name as visible text, with the image itself decorative.
- A canvas or panorama either has `role="img"` with a localized name and the adjacent text description of the current state, or is `aria-hidden` with that description exposed in the DOM. Never leave a focusable element inside an `aria-hidden` subtree.
- Autoplaying or looping footage longer than five seconds starts muted and has a visible pause control.

### Forms and actions

- Every input, select and range has a connected `label` through `htmlFor` and `id`. Placeholder text is never the label.
- The Detect drop zone wraps a real labelled `input type="file"`. Dragging is an enhancement to that input and never a clickable `div`. The crop choice is optional and must not be marked required.
- Link each validation or API error to its control with `aria-describedby`, set `aria-invalid`, and render the message with `role="alert"`. Pending and success messages use `role="status"`.
- While a scan is being analysed, keep the submit button focusable with `aria-disabled="true"` and block the repeat action in the handler. A natively `disabled` button drops keyboard focus mid-task.
- Icon-only controls (zoom in and out, dismiss, recenter, expand, the language chevron) have a localized `aria-label`, and their icons are `aria-hidden`.
- Use `button` for actions and `a` for navigation. No `onClick` on a `div` or `span`, no positive `tabIndex`.

### Selection, disclosure and live values

- Field disease rows are links, because each has a URL. Mark the current one with `aria-current="true"` and the visible rule and chevron. Do not use `role="listbox"` around links.
- Treatment disclosures use native `details` and `summary`, or a `button` with `aria-expanded` and `aria-controls`.
- The Home waypoint is one `button` with `aria-expanded` and `aria-controls` for its preview. The preview is non-modal: do not trap focus, do not move focus into it on page load, and do not open it on hover alone.
- Temperature, Leaf wetness and Humidity are native range inputs with `aria-valuetext` giving the value and unit. Announce a change of infection stage through one polite live region, and not on every slider step.
- Confidence and timeline bars always sit beside their value or status as text. Completed, stopped and not-reached stages differ by text or icon as well as color.
- The supported-crops display wraps as a grid by default. If it scrolls horizontally, the scroller is focusable (`tabIndex={0}`), has `role="region"` and a localized label, and shows that more crops exist.

### Motion and input

- Every animation in the motion reference is removed or reduced under `prefers-reduced-motion`, and content defaults to visible if a reveal fails.
- No core action depends on drag, hover, a pointer gesture, WebGL or a keyboard shortcut. Single-key shortcuts in the Field scene act only while the scene has focus.
