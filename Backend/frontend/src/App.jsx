import { useEffect, useRef } from 'react'
import { Routes, Route, useLocation, useNavigationType } from 'react-router-dom'

import ErrorBoundary from './components/ErrorBoundary'
import ActivityNotice from './components/ActivityNotice'
import Navbar from './components/Navbar'
import FallingLeaves from './components/FallingLeaves'
import { useLang } from './context/LanguageContext'
import { startActivity, trackPage } from './lib/activity'
import Home from './pages/Home'
import Detect from './pages/Detect'
import Result from './pages/Result'
import About from './pages/About'
import Field from './pages/Field'
import NotFound from './pages/NotFound'
import QuestDemo from './pages/QuestDemo'

/**
 * A client-side route change moves nothing by itself: the old page's scroll
 * position and focus stay where they were, and a screen reader hears silence.
 * So on a navigation the reader started, go to the top and put focus on the new
 * page's heading. Back and Forward are left alone: the browser restores those.
 *
 * /field is two views on one path (the list, and one disease), so its query
 * counts as part of the view.
 */
function useRouteFocus() {
  const { pathname, search } = useLocation()
  const navType = useNavigationType()
  const view = pathname === '/field' ? pathname + search : pathname
  const first = useRef(true)

  // The study log: one line per view opened, including the first.
  useEffect(() => {
    startActivity()
    trackPage(view)
  }, [view])

  useEffect(() => {
    if (first.current) {
      first.current = false
      return
    }
    if (navType === 'POP') return
    window.scrollTo(0, 0)
    const frame = requestAnimationFrame(() => {
      document.querySelector('main h1')?.focus({ preventScroll: true })
    })
    return () => cancelAnimationFrame(frame)
  }, [view, navType])
}

export default function App() {
  const { t } = useLang()
  useRouteFocus()

  return (
    <div className="app">
      <a className="skip-link" href="#main">{t('nav.skip')}</a>
      {/* Shown at the bottom of the screen, but early in the page for a
          keyboard or screen reader: being told you are recorded should not
          wait until after everything else. */}
      <ActivityNotice />
      <Navbar />
      <FallingLeaves />
      <main id="main" tabIndex={-1} className="app__main">
        {/* The navbar sits outside the boundary on purpose: if a page throws,
            the user should still be able to navigate away from it. */}
        <ErrorBoundary>
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/detect" element={<Detect />} />
            <Route path="/result" element={<Result />} />
            <Route path="/field" element={<Field />} />
            <Route path="/quest-demo" element={<QuestDemo />} />
            <Route path="/about" element={<About />} />
            {/* Catch-all. Without it an unknown URL renders an empty body,
                which is indistinguishable from a crash. */}
            <Route path="*" element={<NotFound />} />
          </Routes>
        </ErrorBoundary>
      </main>
    </div>
  )
}
