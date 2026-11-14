# Gemini pot-to-greenhouse animation

Use the [5 fps frame analysis](../../../../loading-video/Frame-by-Frame-Analysis.md) and the full-size stills below to match the supplied 10-second composition. The [master prompt and eight image inputs](../../../../loading-video/Master-Prompt.md) are the brief for regenerating video or an art plate. Use the [landing layout](../../cropscan-frontend/references/gemini-landing-layout.md) for live copy and controls.

## Match the camera and blocking first

The reference is 1280 × 720, with a header above a roughly 1280 × 660 scene. Use full-viewport coordinates when reading stills. Subtract the measured header offset before projecting coordinates into the scene. Reuse the lazy React Three Fiber scene in `Backend/frontend/src/three/intro/FieldIntro.jsx` and the timeline owner in `src/components/IntroHero.jsx`. Compare their current parameters with the stills before changing either.

1. Lock the desktop camera and establish the pot silhouette. At the opening the rim spans about x=580–808, the lip is around y=400, and the base meets the ground near (695, 585). Show a shallow top view of granular soil. Use warm matte terracotta, minor wear around the base, a leftward contact shadow, and faint elliptical floor rings.
2. Keep the plant rooted at the soil surface. Animate stem extension, leaf-pair emergence, and side branching on separate eased curves from that origin. Reuse the existing pot, soil, and leaf assets where the silhouette allows. If a single leaf mesh creates a uniformly scaling mature plant, split or rig the foliage into stages so the first two leaves read clearly before the late side leaves unfurl.
3. Establish the finished greenhouse before tuning the wipe. The outer arch reaches left to x≈550, touches the header boundary, and runs off the right and bottom edges. Use a broad, matte clay/plaster inner rim, overhead glass and straight supports, plant rows with small yellow blooms, and an eye-level aisle aimed around (820, 450). Keep sunlight from the upper left and the aisle's perspective stable as the aperture grows. For a backdrop plate, project the art behind the opening and size it to cover the final crop.
4. Open a narrow upright leaf silhouette through the canopy and expand toward the right. Ease the contour into the broad arch and let the intended double exposure between plant and greenhouse peak near 5.8 s. Carry foreground foliage around the final rim with a few separate drifting leaves at different depths. Keep the camera still during reference capture. Add restrained pointer response after the reveal only when the existing product behavior calls for it.

## Reference beats

| Time | Visual anchor and cues |
| --- | --- |
| [0.0 s](../../../../loading-video/frames/frame-00_00.0s.jpg) | Open directly on peach, pot, soil, and shadow. The first loading text starts fading in around 0.2 s. |
| [1.0 s](../../../../loading-video/frames/frame-05_01.0s.jpg) | First green tip breaks the soil. |
| [2.4 s](../../../../loading-video/frames/frame-12_02.4s.jpg) | Slim upright stem, two upper leaves, smaller basal leaves, roughly 47% on the reference bar. |
| [4.2 s](../../../../loading-video/frames/frame-21_04.2s.jpg) | Mature broad-leaf silhouette around 340 px wide, full bar. Hold about 1.2 s in the reference. |
| [5.4 s](../../../../loading-video/frames/frame-27_05.4s.jpg) | First faint greenhouse aperture through the mature plant. |
| [5.8 s](../../../../loading-video/frames/frame-29_05.8s.jpg) | Aperture is tall, narrow, and translucent. Both scenes are recognizable. |
| [6.2 s](../../../../loading-video/frames/frame-31_06.2s.jpg) | Arch has expanded up, down, and right. Loader and plant are fading. |
| [7.2 s](../../../../loading-video/frames/frame-36_07.2s.jpg) | Full right-side arch and settled left copy. |
| [9.8 s](../../../../loading-video/frames/frame-49_09.8s.jpg) | Same composition, with loose leaves crossing the greenhouse. |

For reference renders, reproduce the absolute timeline. In the website, use one clock for the normalized scene and HTML reveal and gate completion on actual readiness. On a ready run, tune the growth, hold, and portal intervals against the table. On a slow run, let the startup phase extend or use the finished fallback. Use a determinate value only when it tracks measured readiness. If a load fails or times out, resolve to the static hero and its controls. Treat a fixed percentage in a cinematic export as part of that animation.

Use the same stable poster in reduced-motion and renderer/media failure states. Render the poster crop in the same space as the final arch so recovery has no layout jump. Keep 3D and textures out of the critical HTML path, match the existing bundle budget, and suspend frame work when the hero is offscreen or the tab is hidden.

## Verify in the live Home route

The current `/?intro` replay path can exercise a normal animated run in a browser. If available, the existing `/?intro=debug` hook can pose scene geometry for deterministic screenshots. A posed screenshot checks framing. A timed recording also checks easing, opacity handoff, and the real loader clock.

Capture at 1280 × 720 with the pointer held still. Compare at least 0.0, 2.4, 4.2, 5.8, 6.2, and 7.2 s. For frame-by-frame work, sample the recorded result every 0.2 s and align those captures with the source. Check the soil center, pot’s ground contact, leaf silhouettes, arch border, aisle vanishing point, and order of the UI reveal. Exercise the static path with reduced motion and a failed media or WebGL load. Report measured timing and framing differences.
