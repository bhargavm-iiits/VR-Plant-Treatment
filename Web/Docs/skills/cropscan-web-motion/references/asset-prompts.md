# Artwork briefs for From Field to Leaf

These are art-only prompts for implementers to adapt in an image generator or to a Blender brief. Build web text, borders, treatment content, route lines, pins and buttons as native components. First open the target [visual references](../../cropscan-frontend/references/page-specifications.md). Use the accepted artwork from the first matching scene as the style reference for its crops and variants.

## Shared reference direction

Paste this before each relevant brief.

```text
Artwork only for the CropScan XR From Field to Leaf web design. A real agricultural landscape and tomato greenhouse under low golden afternoon light, with realistic shapes, scale and planting. Use pine greens, warmer olive highlights, terra-cotta soil and warm ivory stone. Give surfaces physically credible texture, edge light and contact shadows. Keep the style as a premium landscape architectural render or high-quality agricultural photograph. The greenhouse is a single low aluminum hoop structure with evenly spaced curved ribs, diffuse translucent glazing, one narrow central earthen path, drip irrigation lines, and continuous cultivated rows of mature tomato plants. Use the supplied matching visual reference as framing guidance. Provide no UI, letters, wordmarks, iconography, measurement graphics, decorative scan effect, or watermarks.
```

## 01 · Hero terrain

Primary output is one horizontal art plate with clear left reading space and a prominent greenhouse at a stable position. Use a companion topographic layer only if the artwork needs to move without the page.

```text
Create a landscape 16:10 perspective 3D terrain relief for a homepage hero. Camera looks downward at a grazing 35-degree bird’s-eye angle, almost orthographic, with foreground in the bottom left and higher slopes rising toward the upper right. Put ripe maize in the near left foreground, a winding pale footpath traveling diagonally behind it, gently flooded rice terraces on the middle-left hillside, and outdoor tomato beds across the right slope. Set a translucent aluminum hoop greenhouse beside the bend in the path, centered around 0.62 canvas width and 0.53 canvas height. Give the roof a recognizable arch and rib pattern that will match the eye-level poster. Add one substantial exposed cut edge of layered clay and dark root soil along the front. Daylight from the upper left, soft real cast shadows, slightly darker distant trees. Leave the upper-left 34 percent visually quiet with warm ivory paper and faint topographic contour lines. Terrain should fill the right side and lower edge. Keep key roof and path within a safe crop, and leave enough detail for a tighter desktop and mobile crop. Render just the landscape and background.
```

Recommended asset preparation is one 2k-class desktop master and a separate close crop for narrow screens that still shows greenhouse, near path, and part of the maize or terraces. Preserve crop and anchor data with the media configuration. If using a separate relief layer, preserve real alpha and supply a static composite as a fallback. Use the same accepted greenhouse texture and lighting in all reveals.

## 02 · Field close-up

```text
Use the accepted terrain atlas image as the geographic reference. Render a tighter three-quarter view of that exact hoop greenhouse, looking from above and a little from the entrance side. Keep the curved path connected to its door, authentic deep-green tomato beds around it and a narrow slice of textured soil above an ivory topographic plane. Give the horticulture patch enough visible depth with vertical staking and tomato vines. The roof sits in the upper middle-right, visible for a UI marker added separately. The art has a clear clean outline and fades only by cropping at the right edge. Horizontal 3:2 to 16:10 with a conservative safe area and no text or marker.
```

## 03 · Matching eye-level aisle

```text
Render the view from about 1.35 m above the dirt aisle inside the exact hoop greenhouse from the approved atlas. Align the camera with a long natural vanishing point down the repeating ribs. In the close left and right rows, use detailed foliage, individual ripe tomatoes, stakes, and visible black drip hoses along the soil. Let golden afternoon light through the glazing toward the rear left with physically plausible shadow. Keep enough clean visual separation between crop detail and the soil path. At about 72 percent across and slightly below half height, position one accessible foreground tomato leaflet with subtle brown and ochre marks. Its shape must remain easy to recognize and large enough for a small interactive marker. The image remains front-to-back readable, with strong real depth and natural greens, without any generated UI. Provide one wide 16:10 master safely croppable to 16:9, 4:3 and 5:1.
```

Reuse this poster in the Home preview, Field selector, result handoff, and initial explorer when appropriate. Keep the artwork separate from real case footage. A true 360° view needs compatible captured or rendered spherical material and the existing viewer, with its expected projection. A wide flat poster can fill or crop to the display ratio and serves as a static fallback.

## 04 · Shared example leaf and paired photo

```text
Create a high-resolution top-down documentary study of one compound tomato leaf from the same crop rows. On a matte warm gray-limestone background, arrange several connected tapered leaflets diagonally, with a clearly visible stem, crisp veins and near-edge highlights. The terminal leaflet has a few restrained ochre and brown spots with realistic texture. Keep the entire main outline inside a wide composition with 12 percent safety margin. Use soft natural sidelight from upper left, plausible contact shadow and neutral exposure. Camera geometry must be held exactly when making a paired example. Deliver a clean photograph with no text, marks, UI or overlay baked into it.
```

Use one label-free illustrative image for the Home step and retake example. Reuse the exact pixels if a paired illustrative attention state is required. Draw a separate soft mask in the identified region and label the demo accordingly when necessary. On actual Result screens, use the scan’s real original and its own server-supplied attention overlay. The case photo and overlay have a shared crop, image fit, and any view transforms.

Optional companion for the uncertain example or field/lab explanatory pair.

```text
Capture a separate documentary greenhouse view of a leaf attached to its plant in context. For a photo-quality example show one soft, intentionally out-of-focus phone capture with other green clutter, visually legible but unusable for close inspection. For a field reference show sharply focused natural attachment, visible foliage and mild background separation. Use a controlled neutral backing for the separate lab view and the same leaf family. Match the established warm light and realistic specimen detail. Export each photo independently without labels.
```

## 05 · Crop display

```text
Natural realistic studio study of [CROP] on warm ivory, at a slight three-quarter angle, soft cast shadow toward lower right. One compact identifiable cut specimen, some leaves connected to fruit, ear or tuber as appropriate. Match the reference CropScan lighting. Show the entire important silhouette, no surrounding pots, tools or text. Use no labels. Create each crop as a separate composition and maintain compatible shadow, exposure, camera angle and size for alignment on one continuous limestone tray.
```

Use Tomato, Rice, Potato, Grape, Mango and Wheat for the featured row. Other existing crops retain their text labels and access. If generating transparent cutouts, request actual alpha. Keep floor shadows separate if they do not composite well. The pale bench can be one separate horizontal art layer or a plain surface defined in CSS.

## 06 · Quest art

```text
Make one coherent architectural render of the familiar sunlit greenhouse aisle meeting its quiet Visiting Lab through a pale rounded terracotta-plaster threshold. On left, faithfully show the tomato rows and receding hoop roof. At right, show a clean lab with one microscope, papers and a small leaf specimen display. On a low limestone ledge in the near right foreground, place an accurately proportioned white consumer VR headset with two controllers. Reflected warm light, grounded shadows, no floating apparatus or product logos. Preserve the greenhouse and lab in one continuous perspective. The right 60 percent of a webpage will display this art. Keep it clean enough for a strong angled edge in the crop. Deliver art only.
```

## 07 · 404 terrain cutaway

```text
From the same warm sculpted agricultural atlas, create a small lifted edge of terrain with a recognizable fork in a pale path. One branch reaches a small hoop greenhouse with tomato plants. The other branch traces across a green cultivated patch. Rich layered cut soil edge, distant soft foliage and one large warm soft shadow on pale ivory. Design a right-weighted 3:2 crop with a quiet left edge and generous breathing room. No pin, text or web UI. The scene should feel like an overlooked corner of the same atlas.
```

## Export and visual validation

- Keep each production asset separate from design-reference screenshots and out of unrelated projects. Use descriptive names such as `atlas-overview-desktop`, `atlas-overview-narrow`, `greenhouse-aisle-poster`, `leaf-example-tomato`, and `quest-lab-threshold` under the frontend's chosen asset scheme.
- Prepare WebP or AVIF with images that fit the target viewport and relevant DPR. Keep a fallback where genuinely needed. Use explicit image dimensions and responsive sources. Quality can be adjusted to preserve visible leaf, path and shadow detail. Only prioritize the actual hero image that controls first paint. Load noncritical artwork as it approaches view. Crop small preview images from the shared poster at an efficient size.
- Verify each edited image in the actual page at desktop and mobile, not only as a standalone graphic. Check the greenhouse roof is recognizable, pins and crop safe areas stay correct, and no illustrated lettering competes with interface text.
- Keep true field recordings and case data distinct from generated example art. Decorative images can use empty alt text when their role is duplicated in adjacent controls. Prefer meaningful alt text for evidence or instructions where the image is needed to complete the task.
