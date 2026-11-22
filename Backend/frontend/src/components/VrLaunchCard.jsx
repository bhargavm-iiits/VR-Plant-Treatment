import { useCallback, useEffect, useRef, useState } from 'react'

import { api, vrAppLink } from '../lib/api'
import { keepFocusInside } from '../lib/dialog'
import ScrollReveal from './ScrollReveal'

const POLL_MS = 2500
const WATCH_MS = 3 * 60 * 1000

// How the API opened CropScan XR -> what the page says while it hands over.
const HOW = {
  forwarded: 'result.vrSent',       // the game was running
  'unity-play': 'result.vrSent',    // Unity was open: it presses Play
  started: 'result.vrStarted',      // the installed player was started
  'unity-start': 'result.vrUnityStarting', // Unity was closed: it is starting on the project
  'unity-wait': 'result.vrUnityStarting',  // Unity is busy (starting, compiling, closing): delivered when ready
}

/**
 * "Treat this plant in VR": opens this scan in the installed CropScan XR app on this computer through the
 * cropscanxr: link (the app then asks the API to open the scan, and the API checks that the paired player may).
 * The page then follows the launch the app records: opening, waiting for Quest Link, opened in the headset, or
 * why it failed. This is the installed-app path only; the browser Field is a separate link on the result page.
 *
 * `compact` is the Detect page's form: the two buttons and their messages, with no card or heading around them.
 * There both buttons are always shown. Open in CropScan XR is switched off, with its reason beside it, for a scan
 * that has no VR case; Pair VR belongs to this browser, not to one scan, so it works either way.
 */
export default function VrLaunchCard({ result, t, compact = false }) {
  const [pairing, setPairing] = useState(null)
  const [pairError, setPairError] = useState(null)
  const [session, setSession] = useState(null)
  // The laptop demo: the API runs here and opens CropScan XR itself. Otherwise the cropscanxr: link does.
  const [local, setLocal] = useState(null)
  const [sending, setSending] = useState(false)
  const [launchNote, setLaunchNote] = useState(null)
  // After a hand-off the page steps aside: it closes when the browser allows it, else covers itself.
  const [handedOff, setHandedOff] = useState(null)
  const watchUntil = useRef(0)
  const timer = useRef(null)
  const backRef = useRef(null)

  const refresh = useCallback(async () => {
    try {
      setSession(await api.xr.sessionForScan(result.id))
    } catch {
      // Status is a convenience; the launch itself does not depend on it.
    }
  }, [result.id])

  const poll = useCallback(() => {
    clearTimeout(timer.current)
    refresh().finally(() => {
      if (Date.now() < watchUntil.current) timer.current = setTimeout(poll, POLL_MS)
    })
  }, [refresh])

  useEffect(() => {
    if (result.canEnterVr) {
      refresh()
      api.xr.localLaunchInfo().then(setLocal)
    }
    return () => clearTimeout(timer.current)
  }, [result.canEnterVr, refresh])

  // The hand-off screen covers the page, so focus goes to its one control.
  useEffect(() => {
    if (handedOff) backRef.current?.focus()
  }, [handedOff])

  const startWatching = () => {
    watchUntil.current = Date.now() + WATCH_MS
    timer.current = setTimeout(poll, POLL_MS)
  }

  const enterLocally = async () => {
    if (sending) return
    setSending(true)
    setLaunchNote(null)
    try {
      const { status: how } = await api.xr.localLaunch(result.id)
      startWatching()
      setHandedOff(t(HOW[how] ?? 'result.vrSent'))
      // Browsers only let a page close a tab that a script opened; otherwise the hand-off screen stays.
      window.close()
    } catch (error) {
      setLaunchNote({ text: error.message, tone: 'fail' })
    } finally {
      setSending(false)
    }
  }

  const pair = async () => {
    setPairError(null)
    try {
      setPairing(await api.xr.pair())
    } catch (error) {
      setPairError(error.message)
    }
  }

  const launch = session?.latestLaunch
  let status = null
  if (launch?.status === 'opened') status = { text: t('result.vrOpened'), tone: 'ok' }
  else if (launch?.status === 'failed' || launch?.status === 'expired')
    status = { text: t('result.vrFailed', { reason: launch.detail || launch.status }), tone: 'fail' }
  else if (launch?.status === 'opening' || launch?.status === 'pending')
    status = { text: launch.detail || t('result.vrOpening'), tone: 'caution' }
  const saved = session?.treatmentResult

  if (handedOff) {
    return (
      <div
        className="handed"
        role="dialog"
        aria-modal="true"
        aria-labelledby="vr-handoff-title"
        onKeyDown={(e) => {
          if (e.key === 'Escape') setHandedOff(null)
          else keepFocusInside(e)
        }}
      >
        <div className="handed__body">
          <p className="eyebrow">CropScan XR</p>
          <h2 id="vr-handoff-title" className="handed__title">{t('result.vrHandoffTitle')}</h2>
          <p>{handedOff}</p>
          {status && <p role="status" className={`note note--${status.tone}`}>{status.text}</p>}
          <p className="muted">{t('result.vrHandoffBody')}</p>
          <button ref={backRef} type="button" className="btn-secondary" onClick={() => setHandedOff(null)}>
            {t('result.vrBack')}
          </button>
        </div>
      </div>
    )
  }

  // The words beside a switched-off Open button. The server's reason first; an unsure scan has its own sentence.
  const reasonId = compact ? 'vr-actions-reason' : 'vr-card-body'
  const blocked = result.vrBlockedReason
    || (result.status === 'uncertain' ? t('result.blockedUncertain') : t('result.vrUnavailable'))
  const size = compact ? ' btn-lg' : ''

  const openButton = result.canEnterVr && local?.available ? (
    // The API runs on this laptop: it hands the scan to CropScan XR, or starts the installed app.
    <button type="button" onClick={enterLocally} aria-disabled={sending} className={`btn-primary${size}`}>
      {t('result.vrOpen')}
    </button>
  ) : result.canEnterVr && local ? (
    // A plain link, followed straight from the click, so the browser can ask to open CropScan XR.
    <a href={vrAppLink(result.id)} onClick={startWatching} className={`btn-primary${size}`}>
      {t('result.vrOpen')}
    </a>
  ) : (
    // Closed (or still finding out): focusable all the same, so the reason beside it can be read.
    <button
      type="button"
      className={`btn-primary${size}`}
      aria-disabled="true"
      aria-describedby={result.canEnterVr ? undefined : reasonId}
    >
      {t('result.vrOpen')}
    </button>
  )

  const pairButton = (
    <button type="button" onClick={pair} className={`btn-secondary${size}`}>{t('result.pairVr')}</button>
  )

  const notes = (
    <>
      {result.canEnterVr && <p className="hint">{t('result.vrInstall')}</p>}
      {launchNote && <p role="status" className={`note note--${launchNote.tone}`}>{launchNote.text}</p>}
      {pairing && <p role="status" className="note note--ok">{t('result.pairCode', { code: pairing.code })}</p>}
      {pairError && <p role="alert" className="note note--fail">{pairError}</p>}
      {status && <p role="status" className={`note note--${status.tone}`}>{status.text}</p>}
      {saved?.outcome && <p className="hint">{t('result.vrSaved', { outcome: saved.outcome })}</p>}
    </>
  )

  if (compact) {
    return (
      <ScrollReveal as="div" className="vr-actions">
        <div className="vr-actions__buttons">
          {openButton}
          {pairButton}
        </div>
        {!result.canEnterVr && <p id={reasonId} className="muted">{blocked}</p>}
        <div className="vr-actions__notes">{notes}</div>
      </ScrollReveal>
    )
  }

  return (
    <ScrollReveal className="vr-card" aria-labelledby="vr-card-title" data-open={result.canEnterVr}>
      <div className="vr-card__copy">
        <p className="eyebrow">CropScan XR</p>
        <h2 id="vr-card-title" className="vr-card__title">{t('result.vrTitle')}</h2>
        <p id={reasonId} className="muted">
          {result.canEnterVr ? t('result.vrBody') : blocked}
        </p>
      </div>

      <div className="vr-card__actions">
        {openButton}
        {result.canEnterVr && pairButton}
      </div>

      <div className="vr-card__notes">{notes}</div>
    </ScrollReveal>
  )
}
