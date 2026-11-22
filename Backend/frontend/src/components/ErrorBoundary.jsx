import { Component } from 'react'

import { LanguageContext } from '../context/LanguageContext'
import { translate } from '../i18n'

/**
 * Stops one broken component blanking the whole document.
 *
 * Without a boundary, any error thrown during render unmounts the entire React
 * tree and the user gets a white page with no navbar and nothing in the
 * console they would think to look at. That is indistinguishable from "the site
 * is down", and it is what a WebGL failure, a malformed API response or a stale
 * hot-reload all look like.
 *
 * A boundary cannot catch errors in event handlers or async code -- only
 * render, lifecycle and constructors. That is exactly the class of failure that
 * blanks the page, which is why it is worth having.
 *
 * `inline` is for a boundary inside a page that already has its own h1 (the
 * field scene): it renders a compact notice with an h2 instead of a page.
 */
export default class ErrorBoundary extends Component {
  static contextType = LanguageContext
  state = { error: null }

  static getDerivedStateFromError(error) {
    return { error }
  }

  componentDidCatch(error, info) {
    // Keep it in the console for whoever opens devtools next.
    console.error('[CropScan] render error', error, info?.componentStack)
  }

  render() {
    const { error } = this.state
    if (!error) return this.props.children
    // Read straight from the context value: a class cannot call useLang().
    const t = (key) => translate(this.context?.lang ?? 'en', key)
    const { inline, title } = this.props
    const Heading = inline ? 'h2' : 'h1'

    return (
      <div className={inline ? 'broken broken--inline' : 'broken'} role="alert">
        <p className="eyebrow">{t('error.label')}</p>
        <Heading className="broken__title" tabIndex={inline ? undefined : -1}>
          {title ?? t('error.title')}
        </Heading>
        <p className="muted">{t('error.body')}</p>
        <pre className="broken__detail">{String(error?.message ?? error)}</pre>
        <div className="actions">
          <button type="button" className="btn-primary" onClick={() => window.location.reload()}>
            {t('error.reload')}
          </button>
          <button type="button" className="btn-secondary" onClick={() => this.setState({ error: null })}>
            {t('error.retry')}
          </button>
        </div>
      </div>
    )
  }
}
