import { lazy, Suspense, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

import { api } from '../lib/api'
import Breadcrumb from '../components/Breadcrumb'
import ScrollReveal from '../components/ScrollReveal'
import { Check, Cross } from '../components/Icons'
import { useLang } from '../context/LanguageContext'
import LEAVES from '../data/leaves.json'
import { dialRanges, optimalDials, runCycle, suggestBreak } from '../field/cycleMachine'
import { usePageTitle } from '../lib/usePageTitle'

// three.js lives behind this import, so no other page downloads it. There is
// no WebXR, headset or video code on this page any more: it is a leaf library.
const LeafCanvas = lazy(() => import('../three/LeafCanvas'))

const LEAF_BASE = `${import.meta.env.BASE_URL}leaves/`
const DEFAULT_LEAF = LEAVES.find((leaf) => leaf.id === 'tomato_early_blight') ?? LEAVES[0]
const PLANTS = [...new Set(LEAVES.map((leaf) => leaf.plant))]

const DIALS = [
  ['temp_c', 'field.temperature', () => '°C'],
  ['leaf_wetness_hr', 'field.wetness', (t) => t('unit.hours').trim()],
  ['rh_pct', 'field.humidity', () => '%'],
]

function hasWebgl() {
  try {
    const canvas = document.createElement('canvas')
    return Boolean(canvas.getContext('webgl2') || canvas.getContext('webgl'))
  } catch {
    return false
  }
}

/**
 * The leaf library.
 *
 * Every diseased-leaf model the project has, one at a time, in a viewer that
 * turns with a drag or with buttons. Beside it: what the disease looks like in
 * words, and, where one has been written up, its infection cycle with the three
 * conditions that drive it.
 *
 * The selected leaf lives in the URL (?leaf=), so Back works and a view can be
 * shared. ?disease=<backend id> and ?scan=<scan id> still open the matching
 * leaf, which is how the Result page links here.
 */
export default function Field() {
  const { lang, t } = useLang()
  const [params] = useSearchParams()
  const leafParam = params.get('leaf')
  const diseaseParam = params.get('disease')
  const scanId = params.get('scan')

  const [plant, setPlant] = useState('')
  // id -> the disease's name in the reader's language, from the API. English
  // folder names stand in until it answers, and for leaves it does not know.
  const [names, setNames] = useState({})
  const [scan, setScan] = useState(null)        // { diseaseId } | { unknown: true } | { failed: true }
  const [detail, setDetail] = useState(null)    // the API's write-up of the selected disease
  const [detailState, setDetailState] = useState('idle')   // idle | loading | ready | failed
  const [dials, setDials] = useState(null)
  const [view, setView] = useState('loading')   // loading | ready | failed
  const [webgl] = useState(hasWebgl)

  useEffect(() => {
    let alive = true
    api.listDiseases({ lang })
      .then((list) => alive && setNames(Object.fromEntries(list.map((d) => [d.id, d.name]))))
      .catch(() => {})    // the English names already on screen stay
    return () => { alive = false }
  }, [lang])

  // A scan only says which disease to open. An unsure scan names none, so the
  // library opens without a selection of its own instead of guessing a leaf.
  useEffect(() => {
    if (!scanId) {
      setScan(null)
      return
    }
    let alive = true
    setScan(null)
    api.getScan(scanId, lang)
      .then((s) => {
        if (!alive) return
        setScan(s.status === 'uncertain' || !s.class_name ? { unknown: true } : { diseaseId: s.class_name })
      })
      .catch(() => alive && setScan({ failed: true }))
    return () => { alive = false }
  }, [scanId, lang])

  const wantedDisease = diseaseParam ?? scan?.diseaseId ?? null
  const chosen = (leafParam && LEAVES.find((leaf) => leaf.id === leafParam))
    || (wantedDisease && LEAVES.find((leaf) => leaf.backendId === wantedDisease))
    || null
  // Asked for something this library has no leaf for (a healthy class, say).
  const missing = !chosen && (leafParam || wantedDisease) ? (names[wantedDisease] ?? wantedDisease ?? leafParam) : null
  const leaf = chosen ?? DEFAULT_LEAF
  const backendId = leaf.backendId

  // The write-up and the infection cycle for this leaf's disease, when the
  // backend has one. The viewer never waits for it.
  useEffect(() => {
    setDetail(null)
    setDials(null)
    if (!backendId) {
      setDetailState('idle')
      return
    }
    let alive = true
    setDetailState('loading')
    api.getDisease(backendId, lang)
      .then((d) => {
        if (!alive) return
        setDetail(d)
        setDials(d.cycle ? optimalDials(d.cycle) : null)
        setDetailState('ready')
      })
      .catch((e) => {
        console.error('[CropScan] disease fetch failed', e)
        if (alive) setDetailState('failed')
      })
    return () => { alive = false }
  }, [backendId, lang])

  // A new leaf gets a fresh attempt at 3D, even if the last one could not load.
  useEffect(() => {
    setView('loading')
  }, [leaf.id])

  const cycle = detail?.cycle ?? null
  const run = useMemo(() => (cycle && dials ? runCycle(cycle, dials, [], lang) : null), [cycle, dials, lang])
  const hint = useMemo(() => (cycle && dials ? suggestBreak(cycle, dials, lang) : null), [cycle, dials, lang])
  const ranges = useMemo(() => (cycle ? dialRanges(cycle) : null), [cycle])
  const stageLabels = useMemo(
    () => Object.fromEntries((cycle?.stages ?? []).map((s) => [s.id, s.label])),
    [cycle],
  )

  const nameOf = (item) => names[item.backendId] ?? item.disease
  const cropOf = (item) => t(`crop.${item.plant}`)
  const shown = plant ? LEAVES.filter((item) => item.plant === plant) : LEAVES
  const leafName = nameOf(leaf)
  const leafLabel = t('leaf.alt', { disease: leafName, crop: cropOf(leaf) })
  usePageTitle(chosen ? leafName : t('leaf.title'))

  // Said once when the cycle changes stage, not on every slider step.
  const cycleEvent = !run ? ''
    : run.completed ? t('field.liveDone')
    : t('field.liveHalt', { stage: run.stages[run.haltedAt]?.label ?? '' })

  const showCanvas = webgl && view !== 'failed'

  return (
    <div className="leaflib">
      <header className="leaflib__head">
        <Breadcrumb items={[{ label: t('nav.home'), to: '/' }, { label: t('nav.field') }]} />
        <h1 className="h1 h1--field" tabIndex={-1}>{t('leaf.title')}</h1>
        <p className="lead">{t('leaf.lead')}</p>
        {scan?.unknown && <p className="note note--caution">{t('leaf.scanUnknown')}</p>}
        {scan?.failed && <p className="note note--fail" role="alert">{t('result.loadFailed')}</p>}
        {missing && !scan?.unknown && <p className="note note--caution">{t('leaf.noModel', { name: missing })}</p>}
      </header>

      <ScrollReveal className="leaflib__stage" aria-labelledby="leaf-name">
        <p className="eyebrow">{cropOf(leaf)}</p>
        <h2 id="leaf-name" className="leaflib__name">{leafName}</h2>

        <div className="leafview" data-state={showCanvas ? view : 'still'}>
          {/* The picture is always here: it is what shows while the model
              loads, and it is the page when 3D is not available. Once the
              model is up it steps back, and the canvas carries the name. */}
          <img
            className="leafview__poster"
            src={`${LEAF_BASE}${leaf.id}.webp`}
            alt={showCanvas && view === 'ready' ? '' : leafLabel}
            width="480"
            height="600"
            decoding="async"
          />
          {showCanvas && (
            <Suspense fallback={null}>
              <LeafCanvas src={`${LEAF_BASE}${leaf.id}.glb`} label={leafLabel} onState={setView} t={t} />
            </Suspense>
          )}
        </div>

        <p className="leafview__status" role="status">
          {!webgl || view === 'failed' ? t('leaf.failed') : view === 'loading' ? t('leaf.loading') : ''}
        </p>
        {showCanvas && <p className="hint">{t('leaf.drag')}</p>}
        {/* These are generated models, made to teach the look of a disease. */}
        <p className="hint">{t('leaf.illustrative')}</p>
      </ScrollReveal>

      <ScrollReveal as="nav" className="leaflib__chooser" aria-label={t('leaf.title')}>
        <div className="field field--inline">
          <label htmlFor="leaf-crop-filter">{t('field.cropFilter')}</label>
          <select id="leaf-crop-filter" className="select" value={plant} onChange={(e) => setPlant(e.target.value)}>
            <option value="">{t('field.allCrops')}</option>
            {PLANTS.map((p) => <option key={p} value={p}>{t(`crop.${p}`)}</option>)}
          </select>
        </div>
        <p className="hint leaflib__count">{t('leaf.count', { n: shown.length })}</p>

        <ul className="leaflist">
          {shown.map((item) => (
            <li key={item.id}>
              <Link
                to={`/field?leaf=${encodeURIComponent(item.id)}`}
                className="leaflist__row"
                aria-current={item.id === leaf.id ? 'true' : undefined}
              >
                <img src={`${LEAF_BASE}${item.id}.webp`} alt="" width="48" height="60" loading="lazy" decoding="async" />
                <span className="leaflist__text">
                  <span className="leaflist__name">{nameOf(item)}</span>
                  <span className="leaflist__crop">{cropOf(item)}</span>
                </span>
              </Link>
            </li>
          ))}
        </ul>

        <Link to="/detect" className="btn-secondary leaflib__scan">{t('notFound.scan')}</Link>
      </ScrollReveal>

      <aside className="leaflib__rail rail" aria-label={t('leaf.about', { name: leafName })}>
        {detailState === 'loading' && <p role="status" className="muted">{t('leaf.loadingInfo')}</p>}
        {detailState === 'failed' && <p className="note note--caution">{t('leaf.infoFailed')}</p>}
        {detailState === 'idle' && <p className="muted">{t('leaf.noInfo')}</p>}

        {detail?.symptoms && (
          <ScrollReveal aria-labelledby="leaf-symptoms-title">
            <h2 id="leaf-symptoms-title" className="rail__title">{t('result.symptoms')}</h2>
            <p>{detail.symptoms}</p>
            {detail.formation && <p className="muted leaflib__formation">{detail.formation}</p>}
          </ScrollReveal>
        )}

        {cycle && dials && ranges && (
          <ScrollReveal aria-labelledby="field-env-title" className="rail__section">
            <h2 id="field-env-title" className="rail__title">{t('field.environment')}</h2>
            {DIALS.map(([key, label, unitOf]) => {
              const r = ranges[key]
              const unit = unitOf(t)
              return (
                <div className="dial" key={key}>
                  <div className="dial__head">
                    <label htmlFor={`dial-${key}`}>{t(label)}</label>
                    <output htmlFor={`dial-${key}`}>{dials[key]} {unit}</output>
                  </div>
                  <input
                    id={`dial-${key}`}
                    type="range" min={r.min} max={r.max} step={r.step} value={dials[key]}
                    aria-valuetext={`${dials[key]} ${unit}`}
                    onChange={(e) => setDials({ ...dials, [key]: Number(e.target.value) })}
                  />
                </div>
              )
            })}
            <button type="button" className="link link--button" onClick={() => setDials(optimalDials(cycle))}>
              {t('field.reset')}
            </button>
          </ScrollReveal>
        )}

        {run && (
          <ScrollReveal aria-labelledby="field-cycle-title" className="rail__section">
            <h2 id="field-cycle-title" className="rail__title">{t('field.cycle')}</h2>
            <p className={run.completed ? 'rail__summary' : 'rail__summary rail__summary--halt'}>
              {run.summary}
            </p>
            {hint && <p className="hint">{hint}</p>}
            <p className="sr-only" role="status">{cycleEvent}</p>

            <ol className="timeline">
              {run.stages.map((s) => (
                <li
                  key={s.id}
                  data-outcome={s.outcome}
                  aria-current={!run.completed && run.stages[run.haltedAt]?.id === s.id ? 'step' : undefined}
                >
                  <span className="timeline__dot" aria-hidden="true">
                    {s.outcome === 'passed' && <Check size={14} />}
                    {(s.outcome === 'failed' || s.outcome === 'blocked') && <Cross size={14} />}
                  </span>
                  <span className="timeline__label">{s.label}</span>
                  <span className="timeline__outcome">{t(`outcome.${s.outcome}`)}</span>
                </li>
              ))}
            </ol>
          </ScrollReveal>
        )}

        {cycle?.interventions?.length > 0 && (
          <ScrollReveal aria-labelledby="field-break-title" className="rail__section">
            <h2 id="field-break-title" className="rail__subtitle">{t('field.breakIt')}</h2>
            <ul className="breaks">
              {cycle.interventions.slice(0, 4).map((iv) => (
                <li key={iv.action}>
                  <span>{iv.action}</span>
                  <span className="hint">
                    {stageLabels[iv.stage_id] ?? iv.stage_id} — {t(`effect.${iv.effect}`)}
                  </span>
                </li>
              ))}
            </ul>
          </ScrollReveal>
        )}
      </aside>
    </div>
  )
}
