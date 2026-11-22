import { useEffect } from 'react'

const SITE = 'CropScan XR'

/** Names the route in the tab and to a screen reader: "Detect crop disease · CropScan XR". */
export function usePageTitle(title) {
  useEffect(() => {
    document.title = title ? `${title} · ${SITE}` : SITE
  }, [title])
}

/** True when the reader has asked the OS for less motion. */
export function prefersReducedMotion() {
  return typeof window !== 'undefined'
    && typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}
