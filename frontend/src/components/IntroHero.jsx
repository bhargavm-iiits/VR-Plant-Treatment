import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'

import DemoButton from './DemoButton'
import { ArrowRight, ChevronDown } from './Icons'
import { useLang } from '../context/LanguageContext'
import { BEAT, progressAt, timeForProgress } from '../three/intro/timeline'
import settledPoster from '../assets/art/journey-doorway-wide.webp'
import portraitPoster from '../assets/art/journey-doorway-portrait.webp'
import heroShadows from '../assets/art/hero-dappled-shadows.webp'

/**
 * The original field loader and the scroll-driven greenhouse tour.
 *
 * One clock, `t`, in the reference's own seconds (src/three/intro/timeline.js)
 * drives the 3D scene and this HTML together. It is gated on real readiness:
 * the plant never shows more growth than the scene has actually loaded, and
 * the aperture never opens before the greenhouse is uploaded and compiled. On
 * a ready connection it plays the reference beats exactly; on a slow one it
 * simply waits at the current beat.
 *
 *   startup  page painted, loader at 0%, scene chunk and models loading
 *   0–4.2    seedling grows with the bar; 1.0 first tip, 2.4 ≈47%, 4.2 100%
 *   4.2–5.4  hold
 *   5.4–7.2  aperture → arch; loader out 6.0–6.4; copy 6.4–7.2
 *
 * First visit of a session only (?intro replays it). Later visits, reduced
 * motion and automated browsers open on the settled hero. A click or key
 * press runs the rest at 4× once everything is loaded.
 *
 * The settled poster (a render of the opening doorway, in the same space
 * as the live arch) is the hero for reduced motion, for no WebGL, and if the
 * scene cannot load; on repeat visits it shows at once and cross-fades into
 * the live scene when that is ready.
 *
 * After the loader settles, native scroll drives the same live camera.
 */

const FieldIntro = lazy(() =>
  import('../three/intro/FieldIntro')
    .then((m) => { chunkLoaded = true; return m })
    .catch(() => { chunkFailed = true; return { default: () => null } }),
)
let chunkLoaded = false
let chunkFailed = false

const SEEN_KEY = 'cropscan.intro.seen'
const GIVE_UP_S = 15
const DONE_T = BEAT.end
const CHAPTERS = [
  { id: 'threshold', at: 0.29, label: 'journey.arch' },
  { id: 'greenhouse', at: 0.52, label: 'journey.aisle' },
  { id: 'leaf', at: 0.88, label: 'journey.leaf' },
]

function chapterAt(progress) {
  if (progress >= 0.72) return 'leaf'
  if (progress >= 0.44) return 'greenhouse'
  if (progress >= 0.24) return 'threshold'
  return 'landing'
}

const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches

function hasWebGL() {
  try {
    const c = document.createElement('canvas')
    return Boolean(c.getContext('webgl2') || c.getContext('webgl'))
  } catch {
    return false
  }
}

function shouldPlay() {
  if (typeof window === 'undefined') return false
  if (new URLSearchParams(window.location.search).has('intro')) return true
  if (navigator.webdriver || reducedMotion()) return false
  try {
    if (sessionStorage.getItem(SEEN_KEY)) return false
  } catch { /* storage blocked: play it */ }
  return true
}

export default function IntroHero() {
  const { t } = useLang()
  const [webgl] = useState(hasWebGL)
  const [still, setStill] = useState(reducedMotion)
  const [sceneFailed, setSceneFailed] = useState(false)
  const live = webgl && !still   // reduced motion: the poster only, no WebGL scene
  const [play] = useState(() => live && shouldPlay())
  const [phase, setPhase] = useState(play ? 'loading' : 'done')   // loading | reveal | done
  const [pct, setPct] = useState(play ? 0 : 100)
  const [revealed, setRevealed] = useState(!play)
  const [canvasIn, setCanvasIn] = useState(false)
  const [active, setActive] = useState(true)
  const [chapter, setChapter] = useState('landing')
  const [atOpening, setAtOpening] = useState(true)
  const journeyRef = useRef(null)
  const sectionRef = useRef(null)
  const copyRef = useRef(null)
  const skipRef = useRef(null)
  const skip = useRef(false)
  const touring = live && !sceneFailed

  const store = useMemo(() => ({
    t: play ? 0 : DONE_T,
    journey: 0,
    journeyView: 0,
    walkView: 0,
    loader: 0,
    potReady: false,
    panoReady: false,
    archReady: false,
    worldReady: false,
    failed: false,
    animate: typeof window !== 'undefined' && !reducedMotion(),
    pointer: { x: 0, y: 0 },
  }), [play])

  useEffect(() => {
    const media = window.matchMedia('(prefers-reduced-motion: reduce)')
    const update = () => setStill(media.matches)
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [])

  useEffect(() => {
    if (!play) return
    try { sessionStorage.setItem(SEEN_KEY, '1') } catch { /* ignore */ }
  }, [play])

  // The clock.
  useEffect(() => {
    const section = sectionRef.current
    if (!section) return
    if (!live) {
      store.t = DONE_T
      section.style.setProperty('--t', String(DONE_T))
      setPct(100)
      setRevealed(true)
      setPhase('done')
      setCanvasIn(false)
      return
    }
    if (new URLSearchParams(window.location.search).get('intro') === 'debug') {
      // QA: freeze the clock; the review script sets store.t directly.
      window.__cropscanIntro = { store, section, setRevealed }
      setCanvasIn(true)
      return undefined
    }
    let frame
    let last = 0
    let started = !play
    const begin = performance.now()
    let lastPct = -1
    let finished = !play
    let sceneShown = false
    section.style.setProperty('--t', play ? '0.6' : String(DONE_T))

    const tick = (now) => {
      const dt = last ? Math.min(0.1, (now - last) / 1000) : 0
      last = now
      const sinceBegin = (now - begin) / 1000

      // the scene (or its chunk, or a model) failed: show the static landing page
      if (store.failed || chunkFailed) {
        setSceneFailed(true)
        store.t = DONE_T
        section.style.setProperty('--t', String(DONE_T))
        setCanvasIn(false)
        setPct(100)
        setRevealed(true)
        setPhase('done')
        return
      }

      if (!sceneShown && store.worldReady) {
        setCanvasIn(true)
        sceneShown = true
      }
      if (finished) {
        // The scene still reports WebGL failures after settling. Stop advancing
        // the intro clock while continuing to watch for a fallback.
        frame = requestAnimationFrame(tick)
        return
      }

      if (!started) {
        // startup: wait until the whole scene is loaded, uploaded and compiled
        // (the compile alone can block for ~1 s), then fade it in and start
        // the clock, so no frame of the visible animation ever stalls
        if (store.worldReady || sinceBegin > GIVE_UP_S) {
          started = true
          if (store.potReady) setCanvasIn(true)
          // the loading copy is already painted with the page: start after its
          // fade-in so it never dims and returns
          store.t = 0.6
        }
        frame = requestAnimationFrame(tick)
        return
      }

      const real = store.potReady && store.worldReady
        ? 1
        : chunkLoaded ? 0.12 + 0.86 * Math.min(store.loader, 0.99) : 0
      const loadedEnough = real >= 1 || sinceBegin > GIVE_UP_S

      let next = store.t + dt * (skip.current ? 4 : 1)
      // gate the growth on what has really loaded
      if (next < BEAT.mature) next = Math.min(next, Math.max(store.t, timeForProgress(real * 100)))
      // and never open the aperture before the greenhouse is ready
      if (!loadedEnough) next = Math.min(next, BEAT.aperture - 0.05)
      store.t = next
      section.style.setProperty('--t', next.toFixed(3))

      const p = Math.round(progressAt(next))
      if (p !== lastPct) { lastPct = p; setPct(p) }
      if (next >= BEAT.aperture) setPhase((ph) => (ph === 'loading' ? 'reveal' : ph))
      if (next >= 6.4) setRevealed(true)
      if (!finished && next >= DONE_T) {
        finished = true
        setPhase('done')
      }
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [live, play, store])

  // Click or key: run the rest at 4× once everything has loaded.
  useEffect(() => {
    if (phase === 'done') return
    const hurry = (e) => {
      if (e.type === 'keydown' && ['Shift', 'Control', 'Alt', 'Meta', 'Tab'].includes(e.key)) return
      if (store.worldReady) skip.current = true
    }
    window.addEventListener('keydown', hurry)
    const section = sectionRef.current
    section?.addEventListener('pointerdown', hurry)
    return () => {
      window.removeEventListener('keydown', hurry)
      section?.removeEventListener('pointerdown', hurry)
    }
  }, [phase, store])

  // Pointer response (after the reveal) and no rendering while off screen.
  useEffect(() => {
    const section = sectionRef.current
    if (!section) return
    const move = (e) => {
      const b = section.getBoundingClientRect()
      store.pointer.x = ((e.clientX - b.left) / b.width) * 2 - 1
      store.pointer.y = ((e.clientY - b.top) / b.height) * 2 - 1
    }
    const leave = () => { store.pointer.x = 0; store.pointer.y = 0 }
    section.addEventListener('pointermove', move)
    section.addEventListener('pointerleave', leave)
    let inView = true
    const vis = () => setActive(inView && !document.hidden)
    document.addEventListener('visibilitychange', vis)
    let io
    if ('IntersectionObserver' in window) {
      io = new IntersectionObserver(([entry]) => {
        inView = entry.isIntersecting
        vis()
        // Let the ambient leaves drift while the opening scene is visible.
        section.dataset.inview = String(entry.isIntersecting)
      }, { threshold: 0 })
      io.observe(section)
    }
    return () => {
      section.removeEventListener('pointermove', move)
      section.removeEventListener('pointerleave', leave)
      document.removeEventListener('visibilitychange', vis)
      io?.disconnect()
    }
  }, [store])

  useEffect(() => {
    if (copyRef.current) copyRef.current.inert = !revealed || !atOpening
  }, [revealed, atOpening])

  useEffect(() => {
    const track = journeyRef.current
    const stage = sectionRef.current
    if (!track || !stage) return
    let raf = 0
    let lastChapter = 'landing'
    let lastOpening = true
    const update = () => {
      raf = 0
      const top = Number.parseFloat(getComputedStyle(stage).top) || 0
      const distance = track.offsetHeight - stage.offsetHeight
      const journey = touring && revealed && distance > 0
        ? Math.min(1, Math.max(0, (top - track.getBoundingClientRect().top) / distance))
        : 0
      store.journey = journey
      track.dataset.progress = journey.toFixed(4)
      track.style.setProperty('--journey', journey.toFixed(4))
      const nextChapter = chapterAt(journey)
      if (lastChapter !== nextChapter) {
        lastChapter = nextChapter
        setChapter(nextChapter)
      }
      const opening = journey < 0.185
      if (lastOpening !== opening) {
        lastOpening = opening
        setAtOpening(opening)
      }
    }
    const schedule = () => { if (!raf) raf = requestAnimationFrame(update) }
    setChapter('landing')
    setAtOpening(true)
    update()
    window.addEventListener('scroll', schedule, { passive: true })
    window.addEventListener('resize', schedule)
    return () => {
      window.removeEventListener('scroll', schedule)
      window.removeEventListener('resize', schedule)
      cancelAnimationFrame(raf)
    }
  }, [revealed, store, touring])

  function seek(at) {
    const track = journeyRef.current
    const stage = sectionRef.current
    if (!track || !stage) return
    const top = Number.parseFloat(getComputedStyle(stage).top) || 0
    const y = window.scrollY + track.getBoundingClientRect().top - top
    window.scrollTo({ top: y + at * (track.offsetHeight - stage.offsetHeight), behavior: 'smooth' })
  }

  function skipJourney(event) {
    const target = document.getElementById('how-title')
    if (!target) return
    event.preventDefault()
    target.scrollIntoView({ block: 'start' })
    target.focus({ preventScroll: true })
  }

  return (
    <div
      ref={journeyRef}
      className="intro-journey"
      data-mode={touring ? 'live' : 'still'}
      data-beat={chapter}
      data-progress="0"
      data-travel={revealed && touring}
      data-opening={atOpening}
    >
      <section
        ref={sectionRef}
        className="intro"
        data-scene={touring}
        data-phase={phase}
        data-revealed={revealed}
        data-canvas={canvasIn}
        aria-labelledby="hero-title"
        style={{ '--t': play ? 0.6 : DONE_T }}
      >
        {play && (
          <div className="intro__loading" role="status" aria-hidden={revealed}>
            <p className="intro__loading-title">{t('loader.title')}</p>
            <div
              className="intro__bar"
              role="progressbar"
              aria-label={t('loader.progress')}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={pct}
              style={{ '--p': pct / 100 }}
            >
              <span />
            </div>
            <p className="intro__pct" aria-hidden="true">{pct}%</p>
          </div>
        )}

        <div className="intro__copy" ref={copyRef}>
          <h1 id="hero-title" className="intro__title" tabIndex={-1}>
            <span className="intro__line intro__line--1">{t('journey.titleA')}</span>{' '}
            <span className="intro__line intro__line--2">{t('journey.titleB')}</span>
          </h1>
          <p className="intro__lead">{t('journey.lead')}</p>
          <div className="intro__actions">
            <Link to="/detect" className="intro__btn intro__btn--solid">
              {t('home.ctaDetect')}
              <ArrowRight size={22} />
            </Link>
            {touring ? (
              <button className="intro__btn intro__btn--ghost" type="button" onClick={() => {
                seek(CHAPTERS[0].at)
                skipRef.current?.focus({ preventScroll: true })
              }}>
                {t('journey.explore')} <ChevronDown size={18} aria-hidden="true" />
              </button>
            ) : <DemoButton className="intro__btn intro__btn--ghost" />}
          </div>
        </div>

        <div className="intro__art">
          <picture>
            <source media="(max-aspect-ratio: 1/1)" srcSet={portraitPoster} />
            <img className="intro__poster" src={settledPoster} alt="" width="1440" height="828" decoding="async" />
          </picture>
          <img className="intro__shadows" src={heroShadows} alt="" width="2400" height="1100" decoding="async" />

          {touring && (
            <div className="intro__stage" aria-hidden="true">
              <Suspense fallback={null}>
                <FieldIntro store={store} active={active} />
              </Suspense>
            </div>
          )}
        </div>
        {revealed && (
          <div className="journey-controls">
            {touring && (
              <nav className="journey-index" aria-label={t('journey.title')}>
                {CHAPTERS.map(({ id, at, label }) => (
                  <button key={id} type="button" aria-current={chapter === id ? 'step' : undefined} onClick={() => seek(at)}>
                    <span aria-hidden="true" className="journey-index__line" />
                    <span>{t(label)}</span>
                  </button>
                ))}
              </nav>
            )}
            <div className="journey-footer">
              {touring && <div className="journey-progress" aria-hidden="true"><span /></div>}
              {touring && (
                <a href="#how-title" ref={skipRef} className="journey-skip" onClick={skipJourney}>
                  {t('journey.skip')}
                </a>
              )}
              <Link to="/field" className="journey-field">
                {t('result.enterField')} <ArrowRight size={16} aria-hidden="true" />
              </Link>
            </div>
          </div>
        )}
      </section>
    </div>
  )
}
