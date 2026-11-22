import { useState } from 'react'

import { useLang } from '../context/LanguageContext'
import { activityEnabled } from '../lib/activity'

const SEEN_KEY = 'cropscan.activityNotice'

function alreadySeen() {
  try {
    return localStorage.getItem(SEEN_KEY) === '1'
  } catch {
    return false
  }
}

/**
 * Says, in plain words, that the site records what the reader does (lib/activity.js).
 * It stays until it is acknowledged, and does not come back on this browser after that.
 * Recording is a property of the study build, not of pressing OK: the notice informs,
 * and a build with VITE_ACTIVITY=off records nothing and shows nothing.
 */
export default function ActivityNotice() {
  const { t } = useLang()
  const [seen, setSeen] = useState(alreadySeen)
  if (!activityEnabled || seen) return null

  const acknowledge = () => {
    setSeen(true)
    try {
      localStorage.setItem(SEEN_KEY, '1')
    } catch {
      // Private browsing: the notice shows again next visit, which is the safe direction.
    }
  }

  return (
    <aside className="notice-bar" aria-label={t('track.label')}>
      <p>{t('track.notice')}</p>
      <button type="button" className="btn-secondary btn-sm" onClick={acknowledge}>{t('track.ok')}</button>
    </aside>
  )
}
