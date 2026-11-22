import { Link, useLocation } from 'react-router-dom'

import { useLang } from '../context/LanguageContext'
import { usePageTitle } from '../lib/usePageTitle'
import ScrollReveal from '../components/ScrollReveal'

import ReliefScene from '../three/ReliefScene'

import terrainDepth from '../assets/art/notfound-depth.webp'
import terrain from '../assets/art/notfound-terrain.webp'

/**
 * Catch-all. Without this, an unknown URL matches no route and React Router
 * renders nothing: the navbar stays, the page body is empty, and it looks
 * exactly like the app crashed.
 */
export default function NotFound() {
  const { pathname } = useLocation()
  const { t } = useLang()
  usePageTitle(t('notFound.title'))

  return (
    <div className="nf">
      <div className="nf__copy">
        <p className="nf__code">404</p>
        <h1 className="h1 h1--nf" tabIndex={-1}>{t('notFound.title')}</h1>
        <p className="lead">{t('notFound.lead')}</p>
        {/* React escapes the path, so an odd URL is shown as text and nothing more. */}
        <p className="nf__path">
          {t('notFound.body')} <code>{pathname}</code>
        </p>
        <div className="actions">
          <Link to="/" className="btn-primary">{t('notFound.home')}</Link>
          <Link to="/detect" className="btn-secondary">{t('notFound.scan')}</Link>
        </div>
      </div>

      {/* The pin in this scene leads nowhere, so the whole picture is decoration. */}
      <ScrollReveal as="div" className="nf__art" data-relief-root aria-hidden="true">
        <img src={terrain} alt="" width="1011" height="916" decoding="async" />
        <ReliefScene image={terrain} depth={terrainDepth} pivot={[0.5, 0.6]} fit={[0.3, 0.4]} scrollTilt={0.1} pointerTilt={0.08} />
      </ScrollReveal>
    </div>
  )
}
