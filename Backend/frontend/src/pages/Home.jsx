import { Link } from 'react-router-dom'

import IntroHero from '../components/IntroHero'
import ScrollReveal from '../components/ScrollReveal'
import { ChevronRight } from '../components/Icons'
import { useLang } from '../context/LanguageContext'
import { CROPS } from '../crops'
import { LANGS } from '../i18n'
import { usePageTitle } from '../lib/usePageTitle'

import aislePreview from '../assets/art/journey-greenhouse.webp'
import leafExample from '../assets/art/leaf-example-tomato.webp'
import cropTomato from '../assets/art/crop-tomato.webp'
import cropRice from '../assets/art/crop-rice.webp'
import cropPotato from '../assets/art/crop-potato.webp'
import cropGrape from '../assets/art/crop-grape.webp'
import cropMango from '../assets/art/crop-mango.webp'
import cropWheat from '../assets/art/crop-wheat.webp'

// The scan steps use the same example leaf. The third previews the greenhouse.
const STEPS = [
  { title: 'home.s1.title', body: 'home.s1.body', image: leafExample },
  { title: 'home.s2.title', body: 'home.s2.body', image: leafExample, scan: true },
  { title: 'home.s3.title', body: 'home.s3.body', image: aislePreview },
]

// The featured row. Every supported crop is listed in the disclosure below it.
const FEATURED = [
  ['Tomato', cropTomato], ['Rice', cropRice], ['Potato', cropPotato],
  ['Grape', cropGrape], ['Mango', cropMango], ['Wheat', cropWheat],
]

export default function Home() {
  const { t } = useLang()
  usePageTitle(`${t('journey.titleA')} ${t('journey.titleB')}`)

  const metrics = [
    ['68', 'home.stat.classes'],
    [String(CROPS.length), 'home.stat.crops'],
    ['14', 'home.stat.cycles'],
    [String(LANGS.length), 'home.stat.langs'],
  ]

  return (
    <>
      {/* ── Hero: the loading page plays into it on a session's first visit ── */}
      <IntroHero />

      {/* ── How it works ─────────────────────────────────── */}
      <section className="how" aria-labelledby="how-title">
        <ScrollReveal as="h2" id="how-title" className="how__title" tabIndex={-1}>{t('home.ctaHow')}</ScrollReveal>

        <ol className="steps">
          {STEPS.map(({ title, body, image, scan }, i) => (
            <ScrollReveal as="li" className="step" key={title} style={{ '--i': i }}>
              <div className="step__figure">
                <div className="step__media">
                  <img src={image} alt="" width="453" height="248" loading="lazy" decoding="async" />
                  {scan && <span className="step__scan" aria-hidden="true" />}
                </div>
                {i < STEPS.length - 1 && (
                  <span className="step__route" aria-hidden="true">
                    <span className="step__route-dot"><ChevronRight size={16} /></span>
                  </span>
                )}
              </div>
              <h3 className="step__title">{t(title)}</h3>
              <p className="step__body">{t(body)}</p>
            </ScrollReveal>
          ))}
        </ol>

        <ScrollReveal as="ul" className="metrics">
          {metrics.map(([value, label]) => (
            <li key={label}>
              <strong>{value}</strong>
              <span>{t(label)}</span>
            </li>
          ))}
        </ScrollReveal>
      </section>

      {/* ── Supported crops ──────────────────────────────── */}
      <section className="crops" aria-labelledby="crops-title">
        <ScrollReveal as="div" className="crops__head">
          <h2 id="crops-title" className="crops__title">{t('home.cropsTitle')}</h2>
          <Link to="/detect" className="btn-primary">{t('home.startScan')}</Link>
        </ScrollReveal>

        <ul className="shelf">
          {FEATURED.map(([plant, image], i) => (
            <ScrollReveal as="li" key={plant} style={{ '--i': i }}>
              <img src={image} alt="" loading="lazy" decoding="async" />
              <span>{t(`crop.${plant}`)}</span>
            </ScrollReveal>
          ))}
        </ul>

        <ScrollReveal as="details" className="crops__all">
          <summary>{t('home.allCrops', { n: CROPS.length })}</summary>
          <ul className="chips">
            {CROPS.map(([, plant]) => <li key={plant}>{t(`crop.${plant}`)}</li>)}
          </ul>
        </ScrollReveal>
      </section>
    </>
  )
}
