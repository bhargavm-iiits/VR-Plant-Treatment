import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'

import { useLang } from '../context/LanguageContext'
import { track } from '../lib/activity'
import { api } from '../lib/api'
import { keepFocusInside } from '../lib/dialog'
import { isQuestBrowser, questDemoIntent, QUEST_DEMO_PATH } from '../lib/questDemo'
import Sprout from './Sprout'

// How the API opened CropScan XR -> what the page says while it hands over.
const HOW = {
  forwarded: 'demo.front',          // the app was already running
  started: 'result.vrStarted',      // the installed player was started
  'unity-play': 'demo.unityPlay',   // Unity was open: it presses Play
  'unity-start': 'demo.unityStart', // Unity was closed: it is starting on the project
}

/**
 * "See VR Demo".
 *
 * On the laptop that runs the API, this opens the CropScan XR app itself: the
 * API brings a running app to the front, starts the installed player, or
 * starts Unity on the project and presses Play, even when Unity was closed. A
 * covering panel says what is happening while the app loads.
 *
 * Anywhere else it does what it did before: a Quest hands over to the
 * installed app, and any other browser goes to the Quest set-up page. The app
 * is only ever opened from this click.
 */
export default function DemoButton({ className = 'btn-secondary' }) {
  const { t } = useLang()
  const navigate = useNavigate()
  const [state, setState] = useState(null)   // null, or { phase: 'opening' | 'done' | 'failed', text }
  const triggerRef = useRef(null)
  const closeRef = useRef(null)

  // The panel covers the page, so focus goes to its one button.
  useEffect(() => {
    if (state) closeRef.current?.focus()
  }, [state])

  if (isQuestBrowser(navigator.userAgent)) {
    return (
      <a href={questDemoIntent(window.location.origin)} className={className}>
        {t('quest.demo')}
      </a>
    )
  }

  const close = () => {
    setState(null)
    triggerRef.current?.focus()
  }

  const open = async () => {
    if (state?.phase === 'opening') return
    const info = await api.xr.localLaunchInfo()
    if (!info?.available) {
      navigate(QUEST_DEMO_PATH)
      return
    }
    setState({ phase: 'opening', text: t('demo.opening') })
    try {
      const { status } = await api.xr.localDemo()
      track('demo_launch', status)
      setState({ phase: 'done', text: t(HOW[status] ?? 'demo.front') })
    } catch (error) {
      track('demo_failed')
      setState({ phase: 'failed', text: t('demo.failed', { reason: error.message }) })
    }
  }

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className={className}
        onClick={open}
        aria-disabled={state?.phase === 'opening'}
      >
        {t('quest.demo')}
      </button>

      {state && (
        <div
          className="handed"
          role="dialog"
          aria-modal="true"
          aria-labelledby="demo-handoff-title"
          aria-busy={state.phase === 'opening'}
          onKeyDown={(e) => {
            if (e.key === 'Escape') close()
            else keepFocusInside(e)
          }}
        >
          <div className="handed__body">
            <Sprout className="sprout--mark sprout--centre" progress={state.phase === 'opening' ? undefined : 1} />
            <p className="eyebrow">CropScan XR</p>
            <h2 id="demo-handoff-title" className="handed__title">{t('demo.title')}</h2>
            {/* A failure interrupts; progress waits its turn. */}
            {state.phase === 'failed'
              ? <p role="alert" className="note note--fail">{state.text}</p>
              : <p role="status">{state.text}</p>}
            {state.phase === 'done' && <p className="muted">{t('demo.recorded')}</p>}
            <div className="actions actions--centre">
              <button ref={closeRef} type="button" className="btn-secondary" onClick={close}>
                {t('demo.back')}
              </button>
              <Link to={QUEST_DEMO_PATH} className="link" onClick={() => setState(null)}>
                {t('quest.steps')}
              </Link>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
