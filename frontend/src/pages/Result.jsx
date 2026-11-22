import { useLocation, useSearchParams, Link } from 'react-router-dom'
import { useEffect, useState } from 'react'

import Breadcrumb from '../components/Breadcrumb'
import { ArrowRight, ChevronDown } from '../components/Icons'
import VrLaunchCard from '../components/VrLaunchCard'
import { api } from '../lib/api'
import { useLang } from '../context/LanguageContext'
import { usePageTitle } from '../lib/usePageTitle'
import ScrollReveal from '../components/ScrollReveal'

import aisleWide from '../assets/art/greenhouse-aisle-wide.webp'
import leafClear from '../assets/art/leaf-example-clear.webp'
import Sprout from '../components/Sprout'

const NOT_A_LEAF = 'Not_a_leaf'

/** "Tomato___Early_blight" -> "Early blight", for a class the API did not name. */
const prettyClass = (id) => id.split('___').pop().replace(/_/g, ' ')

/** Why this scan may not enter the browser Field, in the reader's language. */
function fieldBlockedReason(result, t) {
  if (!result.hasCycle) return t('result.blockedNoCycle')
  return result.fieldBlockedReason
}

function Crumbs() {
  const { t } = useLang()
  return <Breadcrumb items={[{ label: t('nav.detect'), to: '/detect' }, { label: t('result.crumb') }]} />
}

/** A page that has no result to show: nothing scanned, a missing scan, a failed fetch. */
function Message({ title, body, children }) {
  usePageTitle(title)
  return (
    <div className="page page--contours">
      <Crumbs />
      <div className="message">
        <Sprout className="sprout--mark" progress={1} />
        <h1 className="h1 h1--result" tabIndex={-1}>{title}</h1>
        {body && <p className="lead">{body}</p>}
        <div className="actions">{children}</div>
      </div>
    </div>
  )
}

/** The fixed evidence and rail footprints, so the page does not jump when the scan arrives. */
function Loading() {
  const { t } = useLang()
  usePageTitle(t('result.loading'))
  return (
    <div className="page page--contours" aria-busy="true">
      <Crumbs />
      <Sprout className="sprout--mark" />
      <h1 className="h1 h1--result" tabIndex={-1} role="status">{t('result.loading')}</h1>
      <div className="result__grid" aria-hidden="true">
        <div className="skel skel--pane" />
        <div className="skel skel--pane" />
        <div className="skel skel--rail" />
      </div>
    </div>
  )
}

function Disclosure({ title, children }) {
  return (
    <details className="acc">
      <summary>
        <span>{title}</span>
        <ChevronDown size={22} />
      </summary>
      <p>{children}</p>
    </details>
  )
}

export default function Result() {
  const { state } = useLocation()
  const [params] = useSearchParams()
  const { lang, t } = useLang()

  // Router state paints the page instantly after a scan. The id in the URL is
  // what survives a reload and a language switch.
  const scanId = params.get('scan') ?? state?.result?.id ?? null
  const [result, setResult] = useState(state?.result ?? null)
  const [loadError, setLoadError] = useState(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    if (!scanId) return
    let alive = true
    setLoadError(null)
    api.getScan(scanId, lang)
      .then((r) => alive && setResult(r))
      .catch((e) => {
        console.error('[CropScan] scan fetch failed', e)
        if (alive) setLoadError(e)
      })
    return () => { alive = false }
  }, [scanId, lang, attempt])

  if (!result) {
    if (!scanId) {
      return (
        <Message title={t('result.none.title')} body={t('result.none.body')}>
          <Link to="/detect" className="btn-primary">{t('result.none.cta')}</Link>
        </Message>
      )
    }
    if (loadError) {
      const missing = loadError.status === 404
      return (
        <Message title={missing ? t('result.notFound') : t('result.loadFailed')}>
          {!missing && (
            <button type="button" className="btn-primary" onClick={() => setAttempt((n) => n + 1)}>
              {t('field.retry')}
            </button>
          )}
          <Link to="/detect" className={missing ? 'btn-primary' : 'btn-secondary'}>{t('result.none.cta')}</Link>
        </Message>
      )
    }
    return <Loading />
  }

  return <Scan result={result} localImage={state?.imageUrl} />
}

function Scan({ result, localImage }) {
  const { t } = useLang()

  // The router can restore an expired preview blob URL after a reload.
  const imageUrl = result.storedImageUrl ?? localImage
  const { class_name, confidence, top3, disease_info: info } = result
  const { gradcamUrl, status, modelVersion } = result
  const uncertain = status === 'uncertain'
  // The model's own "this is not a plant" answer (the Not_a_leaf class), when it has one
  const notLeaf = uncertain && top3?.[0]?.class_name === NOT_A_LEAF
  // A cherry leaf scanned with "Chilli" picked: say what it looks like instead of guessing
  const wrongCrop = uncertain && result.looksLikeCrop ? t(`crop.${result.looksLikeCrop}`) : null

  const isHealthy = !uncertain && (info?.is_healthy ?? class_name.includes('healthy'))
  const displayName = notLeaf ? t('result.notLeafTitle')
    : wrongCrop ? t('result.wrongCropTitle', { crop: wrongCrop })
    : uncertain ? t('result.uncertainTitle') : (info?.name || prettyClass(class_name))
  const plant = info?.plant ?? class_name.split('___')[0].replace(/_/g, ' ')
  const cropName = t(`crop.${plant}`)
  usePageTitle(displayName)

  const footer = (
    <ScrollReveal as="footer" className="result__foot">
      {modelVersion && <p className="result__version">{modelVersion} · {status}</p>}
      <p className="note note--caution">
        <strong>{t('common.disclaimerLabel')}</strong> {t('common.disclaimer')}
      </p>
    </ScrollReveal>
  )

  // ── Uncertain: a retake path, never a diagnosis ─────────────────────────
  // No scores here: "Healthy Peach 61.8%" next to a photo of a person reads as
  // an answer even under a "not a diagnosis" label. And no treatment: advising
  // on a guessed disease is the failure the abstain path exists to prevent.
  if (uncertain) {
    return (
      <div className="page page--contours result">
        <Crumbs />
        <p className="status status--caution">
          <span className="status__dot" aria-hidden="true" />
          {t('result.uncertainBadge')}
        </p>
        <h1 className="h1 h1--result" tabIndex={-1}>{displayName}</h1>
        <p className="lead">
          {notLeaf ? t('result.notLeafBody')
            : wrongCrop ? t('result.wrongCropBody', { crop: wrongCrop })
            : t('result.uncertainBody')}
        </p>

        {/* The way forward sits directly under the message, before the examples,
            so it is on screen however long the translated message runs. */}
        <div className="actions actions--lead">
          <Link to="/detect" className="btn-primary btn-lg">{t('result.retake')}</Link>
          <Link to="/" className="link">{t('result.home')}</Link>
        </div>

        <ScrollReveal as="ul" className="tipsrow">
          <li><strong>{t('detect.tip.light')}</strong><span>{t('detect.tip.lightDesc')}</span></li>
          <li><strong>{t('detect.tip.single')}</strong><span>{t('detect.tip.singleDesc')}</span></li>
          <li><strong>{t('detect.tip.fill')}</strong><span>{t('detect.tip.fillDesc')}</span></li>
        </ScrollReveal>

        <ScrollReveal as="div" className={imageUrl ? 'pair' : 'pair pair--single'}>
          {imageUrl && (
            <figure className="pair__item">
              <img src={imageUrl} alt={t('result.photoAltPlain')} />
              <figcaption>{t('result.yourPhoto')}</figcaption>
            </figure>
          )}
          <figure className="pair__item">
            <img src={leafClear} alt={t('result.clearPhotoAlt')} width="720" height="344" />
            <figcaption>{t('result.clearPhoto')}</figcaption>
          </figure>
        </ScrollReveal>

        {footer}
      </div>
    )
  }

  // ── A confident result: healthy, or a named disease ─────────────────────
  return (
    <div className="page page--contours result">
      <Crumbs />
      <p className={isHealthy ? 'status status--ok' : 'status status--found'}>
        <span className="status__dot" aria-hidden="true" />
        {isHealthy ? t('result.healthyBadge') : t('result.diseaseBadge')}
      </p>
      <h1 className="h1 h1--result" tabIndex={-1}>{displayName}</h1>

      <div className="result__top">
      <ul className="result__meta">
        <li>{t('result.plant')}: <strong>{cropName}</strong></li>
        {info?.severity && (
          <li>
            <span className={`sev sev--${info.severity}`} aria-hidden="true" />
            {t('result.severity')}: <strong>{t(`severity.${info.severity}`)}</strong>
          </li>
        )}
        <li className="result__conf">
          <span id="result-conf-label">{t('result.confidence')}</span>
          <span
            className="meter"
            role="meter"
            aria-labelledby="result-conf-label"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(confidence)}
            aria-valuetext={`${confidence.toFixed(1)}%`}
          >
            <span style={{ width: `${Math.max(0, Math.min(100, confidence))}%` }} />
          </span>
          <strong>{confidence.toFixed(1)}%</strong>
        </li>
      </ul>

      {top3?.length > 1 && (
        <details className="top3">
          <summary>{t('result.top')}</summary>
          <ol>
            {top3.map((p) => (
              <li key={p.class_name}>
                <span>{p.name ?? prettyClass(p.class_name)}</span>
                <span className="top3__value">{p.confidence.toFixed(1)}%</span>
              </li>
            ))}
          </ol>
        </details>
      )}
      </div>

      <div className="result__grid">
        <ScrollReveal as="figure" className="pane">
          <figcaption className="pane__label">{t('detect.photo')}</figcaption>
          {imageUrl
            ? <img src={imageUrl} alt={t('result.photoAltCrop', { crop: cropName })} />
            : <p className="pane__empty">{t('result.noPhoto')}</p>}
        </ScrollReveal>

        {/* The overlay is this scan's own, from the server. The sentence under
            it is the text equivalent: the picture is the evidence, so it cannot
            be the only place the meaning lives. */}
        <ScrollReveal as="figure" className="pane">
          <figcaption className="pane__label">{t('result.attention')}</figcaption>
          {gradcamUrl ? (
            <>
              <img src={gradcamUrl} alt={t('result.attentionAlt')} />
              <p className="pane__note">{t('result.attentionNote')}</p>
            </>
          ) : (
            <p className="pane__empty">{t('result.attentionNone')}</p>
          )}
        </ScrollReveal>

        <ScrollReveal className="guide" aria-labelledby="result-guide-title">
          {isHealthy ? (
            <>
              <h2 id="result-guide-title" className="guide__title">{t('result.healthyTitle')}</h2>
              <p>{t('result.healthyBody')}</p>
              {info?.prevention && (
                <p><strong>{t('result.preventionTip')}</strong> {info.prevention}</p>
              )}
            </>
          ) : (
            <>
              <h2 id="result-guide-title" className="guide__title">{t('result.guidance')}</h2>
              {info?.symptoms && <Disclosure title={t('result.symptoms')}>{info.symptoms}</Disclosure>}
              {/* How it forms: the cause, how it survives and spreads, and the
                  weather that drives it. Knowing why it happened is what makes
                  the prevention advice make sense. */}
              {info?.formation && <Disclosure title={t('result.formation')}>{info.formation}</Disclosure>}
              {info?.organic && <Disclosure title={t('result.organic')}>{info.organic}</Disclosure>}
              {info?.chemical && <Disclosure title={t('result.chemical')}>{info.chemical}</Disclosure>}
              {info?.prevention && <Disclosure title={t('result.prevention')}>{info.prevention}</Disclosure>}
            </>
          )}
        </ScrollReveal>
      </div>

      {/* Two ways on, kept apart: the browser Field (a link, gated by
          canEnterField) and the installed Windows VR app (the card below it,
          gated by canEnterVr). Each shows its own reason when it is closed. */}
      <ScrollReveal className="handoff" aria-labelledby="result-handoff-title">
        <img className="handoff__img" src={aisleWide} alt="" width="1046" height="284" loading="lazy" decoding="async" />
        <div className="handoff__body">
          <h2 id="result-handoff-title" className="handoff__title">{t('result.develops')}</h2>
          {result.canEnterField ? (
            <>
              <p>{t('result.leafBody')}</p>
              <Link to={`/field?scan=${encodeURIComponent(result.id)}`} className="btn-primary btn-lg">
                {t('result.viewLeaf')}
                <ArrowRight />
              </Link>
            </>
          ) : (
            <p className="muted">{fieldBlockedReason(result, t)}</p>
          )}
          <Link to="/detect" className="link">{t('result.again')}</Link>
        </div>
      </ScrollReveal>

      <VrLaunchCard result={result} t={t} />
      {footer}
    </div>
  )
}
