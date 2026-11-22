import { useEffect, useState } from 'react'

import { api } from '../lib/api'
import { useLang } from '../context/LanguageContext'
import { LANGS } from '../i18n'
import { usePageTitle } from '../lib/usePageTitle'
import ScrollReveal from '../components/ScrollReveal'

import ribbon from '../assets/art/about-aisle-ribbon.webp'
import fieldPhoto from '../assets/art/about-field-photo.webp'
import labPhoto from '../assets/art/about-lab-photo.webp'

// Keyed by the plant names in data/disease_info.json.
const crops = [
  { name: 'Apple', diseases: 3 },
  { name: 'Blueberry', diseases: 0 },
  { name: 'Cherry', diseases: 1 },
  { name: 'Corn (Maize)', diseases: 3 },
  { name: 'Grape', diseases: 3 },
  { name: 'Orange', diseases: 1 },
  { name: 'Peach', diseases: 1 },
  { name: 'Bell Pepper', diseases: 1 },
  { name: 'Potato', diseases: 2 },
  { name: 'Raspberry', diseases: 0 },
  { name: 'Soybean', diseases: 0 },
  { name: 'Squash', diseases: 1 },
  { name: 'Strawberry', diseases: 1 },
  { name: 'Tomato', diseases: 9 },
]

const phases = [
  { phase: 'about.phase1', title: 'about.phase1.title', desc: 'about.phase1.desc' },
  { phase: 'about.phase2', title: 'about.phase2.title', desc: 'about.phase2.desc' },
]

const pct = (x) => `${(x * 100).toFixed(1)}%`

/**
 * The deployed model's measured numbers, read from /api/model (its meta.json)
 * so this page cannot drift from what is actually serving scans. Nothing here
 * is typed in: with no model, or no answer, the page says so instead.
 */
function ModelMetrics() {
  const { t } = useLang()
  const [card, setCard] = useState(undefined) // undefined: loading, null: unreachable

  useEffect(() => {
    let alive = true
    api.model()
      .then((c) => alive && setCard(c))
      .catch(() => alive && setCard(null))
    return () => { alive = false }
  }, [])

  if (card === undefined) {
    return (
      <div aria-busy="true">
        <p role="status" className="muted">{t('about.loadingModel')}</p>
        <div aria-hidden="true">
          <div className="skel skel--row" />
          <div className="skel skel--row" />
        </div>
      </div>
    )
  }
  if (card === null) return <p className="note note--caution">{t('about.unreachable')}</p>
  if (!card.available) return <p className="note note--caution">{t('about.noModel')}</p>

  const m = card.metrics ?? {}
  const show = (x, fmt) => (x == null ? t('about.notMeasured') : fmt(x))
  return (
    <>
      <table className="mtable">
        <caption className="sr-only">{t('about.metricsTitle')}</caption>
        <thead className="sr-only">
          <tr>
            <th scope="col">{t('about.col.set')}</th>
            <th scope="col">{t('about.col.source')}</th>
            <th scope="col">{t('about.col.accuracy')}</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <th scope="row">{t('about.row.field')}</th>
            <td>PlantDoc</td>
            <td className="mtable__value">{show(m.field_acc, pct)}</td>
          </tr>
          <tr>
            <th scope="row">{t('about.row.lab')}</th>
            <td>PlantVillage</td>
            <td className="mtable__value">{show(m.lab_acc, pct)}</td>
          </tr>
        </tbody>
      </table>
      <p className="about__note">{t('about.splitNote')}</p>

      <dl className="facts">
        <div>
          <dt>{t('about.metric.gap')}</dt>
          <dd>{show(m.domain_gap, (x) => `${(x * 100).toFixed(1)} pp`)}</dd>
        </div>
        <div>
          <dt>{t('about.metric.f1')}</dt>
          <dd>{show(m.macro_f1_field, (x) => x.toFixed(3))}</dd>
        </div>
        <div>
          <dt>{t('about.metric.ece')}</dt>
          <dd>{show(m.ece, (x) => x.toFixed(3))}</dd>
        </div>
      </dl>
      <p className="muted">{t('about.metricsNote')}</p>
      <p className="about__version">
        {card.model_version}{card.trained_at ? ` · ${card.trained_at.slice(0, 10)}` : ''}
      </p>
    </>
  )
}

export default function About() {
  const { t } = useLang()
  usePageTitle(t('about.titleXr'))

  const techStack = [
    ['about.tech.model', 'Fine-tuned from BioCLIP ViT-B/16 (PlantVillage + 26 field-photo sources)'],
    ['about.tech.dataset', t('about.tech.datasetValue')],
    ['about.tech.training', t('about.tech.trainingValue')],
    ['about.tech.abstain', t('about.tech.abstainValue')],
    ['about.tech.backend', 'FastAPI + ONNX Runtime'],
    ['about.tech.frontend', 'React 18 + Vite + Tailwind CSS · three.js + WebXR'],
    ['about.tech.languages', LANGS.map((l) => l.name).join(' · ')],
  ]

  return (
    <div className="about">
      <div className="about__ribbon" aria-hidden="true">
        <img src={ribbon} alt="" width="332" height="916" decoding="async" />
      </div>

      <div className="about__main">
        <header className="about__head">
          <div>
            <h1 className="h1 h1--about" tabIndex={-1}>{t('about.titleXr')}</h1>
            <p className="lead">{t('about.intro')}</p>
          </div>
          {/* Illustrative: what "a field photo" and "a lab photo" mean below. */}
          <ScrollReveal as="div" className="about__pair">
            <figure>
              <img src={fieldPhoto} alt="" width="337" height="225" decoding="async" />
              <figcaption>{t('about.fieldPhoto')}</figcaption>
            </figure>
            <figure>
              <img src={labPhoto} alt="" width="321" height="224" decoding="async" />
              <figcaption>{t('about.labPhoto')}</figcaption>
            </figure>
          </ScrollReveal>
        </header>

        <ScrollReveal className="about__section" aria-labelledby="about-metrics">
          <h2 id="about-metrics" className="h2">{t('about.metricsTitle')}</h2>
          <ModelMetrics />
        </ScrollReveal>

        <ScrollReveal className="about__section" aria-labelledby="about-how">
          <h2 id="about-how" className="h2">{t('about.howTitle')}</h2>
          <ol className="pipe">
            <li>{t('detect.photo')}</li>
            <li className="pipe__key">{t('about.pipe.model')}</li>
            <li>{t('about.pipe.out')}</li>
          </ol>
          <p className="about__note">{t('about.pipeNote')}</p>
          <p className="muted">{t('about.lead')}</p>
          <p className="muted">{t('about.howBody')}</p>

          <div className="phases">
            {phases.map(({ phase, title, desc }) => (
              <div key={phase} className="phase">
                <p className="eyebrow">{t(phase)}</p>
                <h3>{t(title)}</h3>
                <p>{t(desc)}</p>
              </div>
            ))}
          </div>
        </ScrollReveal>

        <ScrollReveal className="about__section" aria-labelledby="about-tech">
          <h2 id="about-tech" className="h2">{t('about.techTitle')}</h2>
          <dl className="spec">
            {techStack.map(([label, value]) => (
              <div key={label}>
                <dt>{t(label)}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
        </ScrollReveal>

        <ScrollReveal className="about__section" aria-labelledby="about-data">
          <h2 id="about-data" className="h2">{t('about.datasetTitle')}</h2>
          <p className="muted">{t('about.datasetBody')}</p>
          <ul className="cropgrid">
            {crops.map(({ name, diseases }) => (
              <li key={name}>
                <strong>{t(`crop.${name}`)}</strong>
                <span>
                  {diseases === 0
                    ? t('about.healthyOnly')
                    : diseases === 1 ? t('about.oneDisease') : t('about.nDiseases', { n: diseases })}
                </span>
              </li>
            ))}
          </ul>
        </ScrollReveal>

        <p className="note note--caution">
          <strong>{t('common.disclaimerLabel')}</strong> {t('about.disclaimer')}
        </p>
      </div>
    </div>
  )
}
