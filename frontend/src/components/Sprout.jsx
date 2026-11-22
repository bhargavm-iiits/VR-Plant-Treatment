import { useEffect, useId, useState } from 'react'

/**
 * A terracotta pot with a seedling that grows with `progress` (0 to 1).
 * Without `progress` it grows on a loop, for small busy states.
 */

// [x, y, angle, size, start] along the stem; each leaf unfolds over 0.18 of the growth.
const LEAVES = [
  [120, 120, 198, 0.62, 0.05],
  [120, 118, -20, 0.66, 0.1],
  [119, 100, 194, 0.98, 0.24],
  [121, 95, -16, 1.04, 0.32],
  [119, 74, 214, 1.18, 0.46],
  [121, 69, -34, 1.22, 0.54],
  [120, 48, 236, 1.02, 0.68],
  [121, 45, -56, 0.98, 0.76],
  [120, 30, -100, 0.72, 0.86],
]

const clamp01 = (v) => Math.min(1, Math.max(0, v))
const ease = (t) => 1 - Math.pow(1 - t, 3)

function useLoop(active) {
  const [g, setG] = useState(1)
  useEffect(() => {
    if (!active) return
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return
    let frame
    const start = performance.now()
    const tick = (now) => {
      const t = ((now - start) / 3200) % 1   // grow 75%, hold 25%
      setG(clamp01(t / 0.75))
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [active])
  return g
}

export default function Sprout({ progress, className = '' }) {
  const looped = useLoop(progress === undefined)
  const g = progress === undefined ? looped : clamp01(progress)
  const stem = ease(clamp01(g / 0.9))
  const id = useId().replace(/:/g, '')

  return (
    <svg className={`sprout ${className}`} viewBox="0 0 240 262" aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id={`${id}-pot`} x1="0" x2="1">
          <stop offset="0" stopColor="#9C4F2A" />
          <stop offset="0.32" stopColor="#C97B4F" />
          <stop offset="0.62" stopColor="#BC6B3F" />
          <stop offset="1" stopColor="#8A4423" />
        </linearGradient>
        <linearGradient id={`${id}-moss`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0.55" stopColor="#7E7A45" stopOpacity="0" />
          <stop offset="1" stopColor="#7E7A45" stopOpacity="0.45" />
        </linearGradient>
        <linearGradient id={`${id}-rim`} x1="0" x2="1">
          <stop offset="0" stopColor="#B4643A" />
          <stop offset="0.35" stopColor="#D88E62" />
          <stop offset="1" stopColor="#A85A33" />
        </linearGradient>
        <linearGradient id={`${id}-leaf`} x1="0" y1="0" x2="1" y2="0">
          <stop offset="0" stopColor="#4C8A2C" />
          <stop offset="1" stopColor="#86BC54" />
        </linearGradient>
        <radialGradient id={`${id}-soil`} cx="0.5" cy="0.4" r="0.7">
          <stop offset="0" stopColor="#4A3427" />
          <stop offset="1" stopColor="#2C1E16" />
        </radialGradient>
      </defs>

      <ellipse className="sprout__shadow" cx="110" cy="250" rx="98" ry="11" />

      {/* Pot body, then the rim's front band over it */}
      <path d="M58 150 L182 150 L170 236 Q168 247 156 247 L84 247 Q72 247 70 236 Z" fill={`url(#${id}-pot)`} />
      <path d="M58 150 L182 150 L170 236 Q168 247 156 247 L84 247 Q72 247 70 236 Z" fill={`url(#${id}-moss)`} />
      <ellipse cx="120" cy="132" rx="72" ry="16" fill="#D99A70" />
      <ellipse cx="120" cy="134" rx="62" ry="11.5" fill={`url(#${id}-soil)`} />
      <g fill="#5A4031">
        <circle cx="96" cy="133" r="2" /><circle cx="140" cy="131" r="1.6" /><circle cx="120" cy="138" r="1.8" />
        <circle cx="108" cy="129" r="1.3" /><circle cx="150" cy="137" r="1.5" /><circle cx="84" cy="136" r="1.2" />
      </g>

      {/* Stem and leaves, drawn between soil and the rim's front band */}
      <path
        d="M120 135 C 122 108, 116 70, 120 30"
        pathLength="1"
        fill="none"
        stroke="#5E9A35"
        strokeWidth="5"
        strokeLinecap="round"
        strokeDasharray="1 1"
        strokeDashoffset={1 - stem}
      />
      {LEAVES.map(([x, y, angle, size, start], i) => {
        const s = size * ease(clamp01((g - start) / 0.16))
        if (s <= 0.001) return null
        return (
          <g key={i} transform={`translate(${x} ${y}) rotate(${angle}) scale(${s})`}>
            <path d="M0 0 C 10 -19, 42 -21, 62 0 C 42 17, 10 16, 0 0 Z" fill={`url(#${id}-leaf)`} />
            <path d="M2 0 Q 32 -2, 58 0" stroke="#B5DB8A" strokeWidth="1.4" fill="none" opacity="0.8" />
          </g>
        )
      })}

      <path d="M48 132 A72 16 0 0 0 192 132 L192 151 A72 16 0 0 1 48 151 Z" fill={`url(#${id}-rim)`} />
      <path d="M48 151 A72 16 0 0 0 192 151" stroke="#7E3E1E" strokeOpacity="0.35" strokeWidth="2" fill="none" />
    </svg>
  )
}