# CropScan XR scroll animation master prompt

The reference is `scrolling.mp4`, a 1920 × 1080 clip at 24 fps with 190 video frames. The folder contains 16 source stills sampled every 0.500 seconds, plus the final source frame at 7.875 seconds. The two storyboards are for review. Use the individual PNGs as image inputs.

Use `01_00.000s.png` as the first image and `17_07.875s.png` as the last. Attach `05_02.000s.png`, `08_03.500s.png`, `10_04.500s.png`, `11_05.000s.png`, and `12_05.500s.png` as intermediate references when your video tool accepts them. If it accepts the original clip as a motion reference, supply `scrolling.mp4` too. Keep the numbered images in order.

## Copy-paste prompt

```text
Recreate the supplied CropScan XR reference clip as a single continuous, 16:9 scrolling shot. Target 1920 × 1080 at 24 fps and approximately 7.9 seconds. Treat the supplied PNGs as consecutive frames from one camera move. Match their framing, scene layout, lighting, and timing. Use the original artwork in the first-frame reference to preserve the appearance of the opening website mockup.

Start on the existing wide composition of a black, rounded browser frame against a dark olive background. Inside it, match the peach wall and warm clay floor, ivory browser bar, dark olive heading and button, leafy wooden arch centered over the doorway, and the pale side card on the right. Keep the foliage, UI, and doorway fixed in their pictured relationship. Move the camera toward the arch along the stone aisle's centerline. The browser frame and page content naturally leave the image edges as the camera moves in. Keep the arch opening aligned with the same path through the greenhouse at every frame.

Once inside, preserve the greenhouse's timber roof, glass panes, warm upper-left daylight, stone path, dense potted foliage, left-side ferns, and right-side pink flowers. Keep the floor, roof beams, and background door continuous across the entrance. Bring one broad, naturally shaped green leaf toward the lens. Let it rotate from a narrow silhouette to a face-on leaf, briefly fill the image, then ease back to the final framing. Build the leaf move from a 2D photographic plate and image transforms. Preserve one leaf outline, its central vein, fine branching veins, and the pattern of water droplets throughout. End with the leaf sharp against the same greenhouse in warm, soft focus, its tip pointing upper left and its short stem lower right.

Match these reference frames by source timestamp.

01 | 0.000 s | Full browser mockup with dark olive margin on all sides. Center the arch within the reference page, with heading left and pale card right.
02 | 0.500 s | Hold nearly the same wide framing, with the first subtle forward movement.
03 | 1.000 s | The browser grows slightly in frame. The arch stays on the same axis and the top bar is still visible.
04 | 1.500 s | The browser frame almost fills the shot. The center arch broadens while the heading and card move toward the edges.
05 | 2.000 s | The page frame is mostly out of view. The arch dominates, with cropped text at left, a sliver of the pale card at right, and the aisle already visible through it.
06 | 2.500 s | Near the threshold. Leafy branches and the dark timber rim frame the edges, with the stone path centered beyond.
07 | 3.000 s | Cross the threshold. The arch is still visible at the sides as more of the greenhouse roof, plants, and path come into view.
08 | 3.500 s | Fully inside. The center path leads toward the far door. A small, dark leaf is visible ahead just right of center.
09 | 4.000 s | The leaf is larger and hangs at a steep diagonal in front of the aisle, with the greenhouse still readable behind it.
10 | 4.500 s | The leaf advances into the center and turns through an angled view. Its silhouette is broad enough to begin covering the aisle.
11 | 5.000 s | The leaf arrives nearest the lens and fills most of the image. The upper edge is briefly cropped, the gold-toned vein network is visible, and the central vein runs toward the lower-right stem.
12 | 5.500 s | The leaf eases back until its full outline fits at roughly three fifths of frame width. Focus locks on the leaf as the greenhouse softens.
13 | 6.000 s | Hold the angled macro composition. Show fine veins, water droplets on the surface, and tiny bright droplets along the lower edge.
14 | 6.500 s | Hold the same scale with only a slight natural drift. Keep the warm rim light and soft greenhouse background.
15 | 7.000 s | Maintain the same diagonal and left-of-center placement. Keep the distant timber and pink flowers softly blurred.
16 | 7.500 s | Continue the quiet hold. Preserve the same leaf shape, stem, vein pattern, and droplets.
17 | 7.875 s | Match the final reference still, with the whole sharp leaf and stem visible against the softly lit greenhouse.

Keep the warm peach, clay, wood, olive green, and golden daylight grade consistent. Use one smooth forward camera move and preserve the clip's brief opening hold, the faster push through the arch, the near-lens leaf pass, and the final stillness. Compose the shot so it reads naturally in reverse when the site scrubs the video. Exclude added captions, scan effects, holograms, extra screens, and 3D leaf models. Output a single landscape video frame at a time, with the reference storyboard labels and borders excluded from the render.
```
