import { Link } from 'react-router-dom'

import { useLang } from '../context/LanguageContext'

/**
 * `items` is a trail of { label, to }. The last one is the current page: it has
 * no link and carries aria-current, so a screen reader hears where it is.
 */
export default function Breadcrumb({ items }) {
  const { t } = useLang()
  return (
    <nav aria-label={t('nav.breadcrumb')} className="crumbs">
      <ol>
        {items.map(({ label, to }, i) => (
          <li key={`${label}-${i}`}>
            {to && i < items.length - 1
              ? <Link to={to}>{label}</Link>
              : <span aria-current="page">{label}</span>}
          </li>
        ))}
      </ol>
    </nav>
  )
}
