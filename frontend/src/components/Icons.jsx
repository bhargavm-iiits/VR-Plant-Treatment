/**
 * Inline icons. Every one is decorative: the control it sits in carries the
 * name, so each SVG is hidden from the accessibility tree and never focusable.
 */
function Svg({ size = 24, children, ...rest }) {
  return (
    <svg
      width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"
      aria-hidden="true" focusable="false" {...rest}
    >
      {children}
    </svg>
  )
}

/** The contour-leaf brand mark. */
export function LeafMark({ size = 46 }) {
  return (
    <svg
      width={size} height={size} viewBox="0 0 48 48" fill="none" stroke="currentColor"
      strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false"
    >
      <path d="M9 39C9 21 20 9 41 8c0 20-11 31-27 31z" />
      <path d="M5 44C13 33 22 24 34 16" />
    </svg>
  )
}

export const ArrowRight = (p) => <Svg {...p}><path d="M4 12h16M14 6l6 6-6 6" /></Svg>
export const ChevronRight = (p) => <Svg {...p}><path d="M9 5l7 7-7 7" /></Svg>
export const ChevronLeft = (p) => <Svg {...p}><path d="M15 5l-7 7 7 7" /></Svg>
export const ChevronDown = (p) => <Svg {...p}><path d="M5 9l7 7 7-7" /></Svg>
export const Plus = (p) => <Svg {...p}><path d="M12 5v14M5 12h14" /></Svg>
export const Minus = (p) => <Svg {...p}><path d="M5 12h14" /></Svg>
export const Check = (p) => <Svg {...p}><path d="M5 12.5l4.5 4.5L19 7.5" /></Svg>
export const Cross = (p) => <Svg {...p}><path d="M6 6l12 12M18 6L6 18" /></Svg>
export const Trash = (p) => (
  <Svg {...p}><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v6M14 11v6" /></Svg>
)
export const Sun = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2v2.5M12 19.5V22M2 12h2.5M19.5 12H22M4.9 4.9l1.8 1.8M17.3 17.3l1.8 1.8M4.9 19.1l1.8-1.8M17.3 6.7l1.8-1.8" />
  </Svg>
)
export const Leaf = (p) => (
  <Svg {...p}><path d="M5 19C5 10 10 4.5 20 4c0 10-5 15.500-12.500 15.500z" /><path d="M3 21.500C7 16 11 12 17 8" /></Svg>
)
export const Frame = (p) => (
  <Svg {...p}><path d="M4 9V5h4M16 5h4v4M20 15v4h-4M8 19H4v-4" /></Svg>
)
export const Picture = (p) => (
  <Svg {...p}><rect x="3" y="4" width="18" height="16" rx="2" /><circle cx="15.500" cy="9" r="1.500" /><path d="M3 17l5.500-5.500 4 4 2.500-2.500L21 18" /></Svg>
)
export const Mouse = (p) => (
  <Svg {...p}><rect x="7" y="3" width="10" height="18" rx="5" /><path d="M12 7v3" /></Svg>
)
export const Recenter = (p) => (
  <Svg {...p}><circle cx="12" cy="12" r="3" /><path d="M12 2v4M12 18v4M2 12h4M18 12h4" /></Svg>
)
export const Upload = (p) => (
  <Svg {...p}><path d="M12 16V4M7 9l5-5 5 5M4 16v3a1 1 0 001 1h14a1 1 0 001-1v-3" /></Svg>
)

/** Map waypoint: a filled terracotta teardrop with an ivory ring and eye. */
export function Pin({ size = 34 }) {
  return (
    <svg
      width={size} height={size * 1.3} viewBox="0 0 34 44" aria-hidden="true" focusable="false"
      className="pin-glyph"
    >
      <path
        d="M17 42C9 31 3.500 24.500 3.500 16.500a13.500 13.500 0 0127 0C30.500 24.500 25 31 17 42z"
        fill="var(--marker)" stroke="var(--canvas)" strokeWidth="2.500"
      />
      <circle cx="17" cy="16.500" r="5" fill="var(--canvas)" />
    </svg>
  )
}
