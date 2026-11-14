# Gemini loading and landing layout

Use the [0.4 s loader](../../../../loading-video/frames/frame-02_00.4s.jpg), [7.2 s landing](../../../../loading-video/frames/frame-36_07.2s.jpg) and [9.8 s hold](../../../../loading-video/frames/frame-49_09.8s.jpg) at full size. These are the visual targets for the Gemini Home viewport. Inspect `Backend/frontend/src/pages/Home.jsx`, `src/components/IntroHero.jsx`, `src/components/Navbar.jsx`, and the `.intro` rules in `src/index.css`. The [scene animation guide](../../cropscan-web-motion/references/gemini-intro-animation.md) covers art and timing.

## Desktop alignment

Coordinates are approximate full-viewport pixels at **1280 × 720**, including the header. Use them to judge screenshots at that size. For scene-local positioning, subtract the header height. Scale the desktop art and copy coherently and recompose on narrow viewports.

| Element | Reference position and appearance |
| --- | --- |
| Header | About 60 px high. Two-leaf wordmark at x≈36. Right-aligned Home, Detect, Field, About, and a single outlined English dropdown. Olive underline on Home. |
| Empty canvas | Pale peach near the copy, warmer apricot toward the lower right. Faint ellipse rings and soft shadow ground the pot. Keep the left area clear. |
| Loading copy | x≈37, glyph top y≈138. Heavy rounded sans-serif at about 56 px in two lines, “Loading” then “the field.”. |
| Progress | Track x≈37 through 297 at y≈278, around 4 px high, olive fill and small round tip. Place the percentage below around y≈300. Tabular digits keep it steady. |
| Landing title | x≈40, y≈170–177. Two lines, “From canopy” then “to greenhouse.”. Roughly 56–59 px bold, tight leading. |
| Landing lead | x≈40, y≈311, about 24 px. “Explore the field after you scan.” |
| Main actions | x≈40. Filled olive “Detect Disease” at y≈379 with a cream arrow. Outlined “See VR Demo” at y≈448 with a play icon. Both about 269 × 53 px. |
| Portal and field action | Arch occupies the right side from about x=550. Place the pale peach “Enter the field” pill near x≈880–1095 and y≈630–695, rotated slightly upward to the right. Its hit area follows the anchor as art crops change. |

Use the reference gradient colors `#F5D2B4` and `#EFBD9A` as sampled starting points. Keep headline and controls dark olive and sample them in the actual composite. Reuse the site's sans-serif, logo, icons, and semantic components where they match. Keep one steady logo, one of each nav item, crisp type, and a monotonic progress display. The generated footage shows brief logo ghosting, duplicate nav items, and unstable digits. The final production layout cleans up those artifacts.

## Reveal and navigation

- Keep the visual timeline in sync with the art from `IntroHero`. The loader fades as the arch grows around 6.0–6.4 s. The first headline line appears around 6.6 s, the second line and lead around 6.8 s, then the primary and outlined actions settle by about 7.2 s. The arch pill becomes visible before the full left action stack.
- Render the wordmark, nav, heading, status, and CTAs in DOM order. Keep the art layer decorative and separate from those controls. Use current locale keys or extend the existing translations if new copy is needed. Give real pending progress an accessible label and avoid announcing every visual increment.
- Wire **Detect Disease** to the existing Detect route, **See VR Demo** through `DemoButton`, and **Enter the field** to the existing Field route. Keep shared navigation usable during the intro. When a CTA is visually hidden, keep it out of sequential focus until revealed. Show the static landing and actionable controls on skipped, reduced-motion, and fallback paths.
- Preserve the reference composition on desktop. At narrow widths, place copy and main actions above the cropped portal and bring the field link into the flow if needed. Preserve heading wraps in English at reference size while allowing translated text and zoomed text to wrap naturally.

## Proof before calling it a match

Compare browser screenshots of the loader and settled Home with the full-size reference stills. Check header height and baseline, left gutter, headline breaks, button dimensions, arch edge and aisle alignment, and the field pill's visual and interactive placement. Check 320 CSS px and 200% zoom for text clipping and overlap. Confirm the three CTAs and the menu with keyboard and pointer, then check reduced motion, repeat visit, and unavailable artwork. Use the project's existing lint, test, and build commands for implementation changes. Record which states and viewports were actually checked.
