import { Link, useLocation } from 'react-router-dom'
import { useEffect, useState } from 'react'

import { useLang } from '../context/LanguageContext'
import { LANGS } from '../i18n'
import { LeafMark } from './Icons'

// `section` lists the routes that belong under a link without being it: a
// result is part of Detect, the Quest demo part of Field. One source for both
// the wide and the narrow layout, so they cannot disagree about where you are.
const links = [
  { to: '/', key: 'nav.home', section: [] },
  { to: '/detect', key: 'nav.detect', section: ['/result'] },
  { to: '/field', key: 'nav.field', section: ['/quest-demo'] },
  { to: '/about', key: 'nav.about', section: [] },
]

/** 'page' on the route itself, 'true' on its parent section, nothing elsewhere. */
function current(pathname, { to, section }) {
  if (pathname === to) return 'page'
  return section.includes(pathname) ? 'true' : undefined
}

export default function Navbar() {
  const { pathname } = useLocation()
  const [scrolled, setScrolled] = useState(false)
  const { lang, setLang, t } = useLang()

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 12)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  return (
    <header className="site-header" data-scrolled={scrolled}>
      <div className="site-header__inner">
        <Link to="/" className="brand">
          <LeafMark />
          <span className="brand__name">CropScan XR</span>
        </Link>

        <nav aria-label={t('nav.main')} className="site-nav">
          <ul>
            {links.map((link) => (
              <li key={link.to}>
                <Link to={link.to} aria-current={current(pathname, link)}>
                  {t(link.key)}
                </Link>
              </li>
            ))}
          </ul>
        </nav>

        <div className="lang">
          <label htmlFor="site-lang" className="sr-only">{t('nav.language')}</label>
          <select
            id="site-lang"
            className="lang__select"
            value={lang}
            onChange={(e) => setLang(e.target.value)}
          >
            {LANGS.map(({ code, name }) => (
              // Each name is written in its own script, so it carries its own lang.
              <option key={code} value={code} lang={code}>{name}</option>
            ))}
          </select>
        </div>
      </div>
    </header>
  )
}
