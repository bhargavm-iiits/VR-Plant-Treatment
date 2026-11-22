/**
 * The Gemini pot-to-greenhouse timeline, in absolute reference seconds.
 * Source: Web/Docs/loading-video/Frame-by-Frame-Analysis.md and
 * Web/Docs/skills/cropscan-web-motion/references/gemini-intro-animation.md.
 *
 * One clock (`t`, owned by IntroHero) drives both the 3D scene and the HTML,
 * so they cannot drift. The website gates the clock on real readiness: it
 * never shows more progress than has actually loaded.
 */

export const BEAT = {
  textIn: 0.2,      // loading copy starts fading in
  tip: 1.0,         // first green tip breaks the soil
  midGrowth: 2.4,   // slim stem, two upper leaves, ≈47%
  mature: 4.2,      // full canopy, 100%
  aperture: 5.4,    // first faint greenhouse aperture
  overlap: 5.8,     // tall, narrow, translucent: both scenes readable
  expanded: 6.2,    // arch has grown up, down and right
  settled: 7.2,     // full arch, copy settled
  drift: 7.8,       // loose leaves start crossing the greenhouse
  end: 10.0,
}

/** Displayed percentage over time, read from the reference bar (monotonic). */
export const PROGRESS_KEYS = [
  [0, 0], [0.9, 0], [1.2, 22], [1.6, 23], [1.8, 27], [2.0, 33], [2.2, 45], [2.4, 47], [2.6, 49],
  [2.8, 60], [3.0, 66], [3.2, 71], [3.4, 72], [3.6, 74], [3.8, 88], [4.0, 99], [4.2, 100],
]

/** Piecewise-linear lookup over [[t, v], ...]. */
export function track(keys, t) {
  if (t <= keys[0][0]) return keys[0][1]
  for (let i = 1; i < keys.length; i++) {
    const [t1, v1] = keys[i]
    if (t <= t1) {
      const [t0, v0] = keys[i - 1]
      return v0 + ((v1 - v0) * (t - t0)) / (t1 - t0)
    }
  }
  return keys[keys.length - 1][1]
}

/** Smooth (eased) interpolation between keyframe values. */
export function ease(keys, t) {
  if (t <= keys[0][0]) return keys[0][1]
  for (let i = 1; i < keys.length; i++) {
    const [t1, v1] = keys[i]
    if (t <= t1) {
      const [t0, v0] = keys[i - 1]
      const u = (t - t0) / (t1 - t0)
      return v0 + (v1 - v0) * u * u * (3 - 2 * u)
    }
  }
  return keys[keys.length - 1][1]
}

export const progressAt = (t) => track(PROGRESS_KEYS, t)

/** The earliest clock time at which the displayed progress reaches p (0..100). */
export function timeForProgress(p) {
  for (let i = 1; i < PROGRESS_KEYS.length; i++) {
    const [t1, v1] = PROGRESS_KEYS[i]
    if (v1 >= p) {
      const [t0, v0] = PROGRESS_KEYS[i - 1]
      return v1 === v0 ? t0 : t0 + ((p - v0) * (t1 - t0)) / (v1 - v0)
    }
  }
  return BEAT.mature
}

/*
 * The aperture, keyed to the reference stills (E 5.8 s, F 6.2 s, G 7.2 s).
 * Positions are relative to the final arch centre, in units of the final
 * outer radius R (x right, y down, in screen-height units).
 */
const A = (vals) => vals.map((v, i) => [[5.4, 5.8, 6.2, 6.6, 7.2][i], v])
export const APERTURE = {
  // the opening's centre, as a fraction of the way from the seedling canopy
  // (1) to the doorway's centre (0)
  travel: A([1, 0.89, 0.59, 0.13, 0]),
  // half-width / half-height as fractions of the finished doorway
  halfW: A([0.257, 0.618, 0.915, 0.976, 1]),
  halfH: A([0.637, 0.877, 0.978, 0.996, 1]),
  round: A([0, 0, 0.75, 1, 1]),          // 0 = upright leaf, 1 = the rounded doorway
  tilt: A([0.04, 0.05, 0.02, 0, 0]),
  photo: A([0.12, 0.55, 0.88, 1, 1]),    // greenhouse opacity: the double exposure
  opening: A([1, 0.985, 0.92, 0.84, 0.816]), // clear opening ÷ outer edge: the rim grows out of the edge
  pot: A([1, 0.55, 0.04, 0, 0]),       // plant, pot, soil and contact shadow, together
}