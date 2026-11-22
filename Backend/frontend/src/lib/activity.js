/**
 * The study's activity log for the website.
 *
 * Records what a participant does: pages opened, controls pressed, settings changed, how far a page was scrolled,
 * and the tab being hidden or shown. Events wait in memory and go to POST /api/activity in small batches; the
 * Unity app writes to the same table. The site says so in a visible notice (components/ActivityNotice.jsx).
 *
 * What is deliberately NOT recorded: anything typed, any file or file name, any photo, and pointer movement. A
 * control is described by its own label, which is the site's text, never the participant's.
 *
 * One page load is one visit with its own ID, and every event has a sequence number, so a batch that is sent twice
 * (a retry, or the page closing mid-request) is stored once. Set VITE_ACTIVITY=off to build a site that records nothing.
 */

const BASE = import.meta.env.VITE_API_URL || ''
const ENABLED = import.meta.env.VITE_ACTIVITY !== 'off'
const SESSION_KEY = 'cropscan.session'      // the same browser session token lib/api.js keeps
const SESSION_HEADER = 'X-CropScan-Session'
const FLUSH_MS = 4000
const BATCH = 50
const MAX_QUEUED = 1000
const SCROLL_MARKS = [25, 50, 75, 100]

const visitId = newId()
let queue = []
let seq = 0
let timer = 0
let sending = false
let started = false
let scrolled = new Set()

function newId() {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID()
  // An older browser: still a valid version-4 UUID, which is all the API asks for.
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = Math.floor(Math.random() * 16)
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16)
  })
}

function sessionToken() {
  for (const store of ['localStorage', 'sessionStorage']) {
    try {
      const token = globalThis[store].getItem(SESSION_KEY)
      if (token) return token
    } catch {
      // Storage can be disabled; the event is still recorded, without a session.
    }
  }
  return null
}

const here = () => window.location.pathname + window.location.search

function envelope(events) {
  return {
    source: 'web',
    visitId,
    lang: document.documentElement.lang || null,
    viewport: `${window.innerWidth}x${window.innerHeight}`,
    events,
  }
}

/** Add one event. `target` is a short label for what was acted on; `data` is a small plain object. */
export function track(kind, target, data) {
  if (!ENABLED || typeof window === 'undefined' || queue.length >= MAX_QUEUED) return
  const event = { seq: seq++, kind, ts: Date.now(), path: here() }
  if (target) event.target = String(target).slice(0, 160)
  if (data && Object.keys(data).length) event.data = data
  queue.push(event)
  if (queue.length >= BATCH) flush()
  else if (!timer) timer = window.setTimeout(flush, FLUSH_MS)
}

let lastPage = null

/** A route change. Scroll milestones start again for the new page. A repeat of the same view is one line, not two. */
export function trackPage(title) {
  if (title === lastPage) return
  lastPage = title
  scrolled = new Set()
  track('page_view', title)
}

async function flush() {
  window.clearTimeout(timer)
  timer = 0
  if (sending || queue.length === 0) return
  sending = true
  const batch = queue.slice(0, BATCH)
  try {
    const headers = { 'Content-Type': 'application/json' }
    const token = sessionToken()
    if (token) headers[SESSION_HEADER] = token
    const response = await fetch(`${BASE}/api/activity`, {
      method: 'POST', headers, body: JSON.stringify(envelope(batch)), keepalive: true,
    })
    // Stored, or refused for good (a 4xx will not improve on a retry): either way these are done.
    if (response.ok || (response.status >= 400 && response.status < 500)) queue = queue.slice(batch.length)
  } catch {
    // Offline or the API is down: the same events go again on the next flush.
  } finally {
    sending = false
    if (queue.length && !timer) timer = window.setTimeout(flush, FLUSH_MS)
  }
}

/** The page is going away: hand whatever is left to the browser, which sends it after the page has gone. */
function flushOnLeave() {
  if (queue.length === 0) return
  const batch = queue.slice(0, 200)
  try {
    const body = new Blob([JSON.stringify(envelope(batch))], { type: 'application/json' })
    if (navigator.sendBeacon?.(`${BASE}/api/activity`, body)) queue = queue.slice(batch.length)
  } catch {
    // Nothing more can be done from a closing page.
  }
}

/** The site's own label for a control: its accessible name, else its visible text. Never a typed value. */
function labelOf(el) {
  const named = el.getAttribute('aria-label')
    || (el.labels?.[0]?.textContent)
    || (el.getAttribute('aria-labelledby') && document.getElementById(el.getAttribute('aria-labelledby'))?.textContent)
    || el.textContent
    || el.getAttribute('name')
    || el.id
  return (named || el.tagName).replace(/\s+/g, ' ').trim().slice(0, 80)
}

function onClick(e) {
  const el = e.target instanceof Element
    ? e.target.closest('a, button, summary, [role="button"], input[type="checkbox"], input[type="radio"]')
    : null
  if (!el) return
  const data = { el: el.tagName.toLowerCase() }
  if (el.tagName === 'A') {
    const href = el.getAttribute('href') || ''
    // An in-site path is recorded; an outside address is only noted as outside.
    data.href = href.startsWith('/') ? href.slice(0, 120) : 'external'
  }
  if (el.getAttribute('aria-expanded') !== null) data.expanded = el.getAttribute('aria-expanded')
  if (el.getAttribute('aria-disabled') === 'true') data.disabled = true
  track('click', labelOf(el), data)
}

function onChange(e) {
  const el = e.target
  if (!(el instanceof HTMLElement)) return
  if (el instanceof HTMLSelectElement) {
    track('change', labelOf(el), { el: 'select', value: String(el.value).slice(0, 60) })
  } else if (el instanceof HTMLInputElement && el.type === 'range') {
    track('change', labelOf(el), { el: 'range', value: Number(el.value) })
  } else if (el instanceof HTMLInputElement && el.type === 'file') {
    // That a photo was chosen, and how many. Never its name or contents.
    track('change', labelOf(el), { el: 'file', files: el.files?.length ?? 0 })
  }
  // Text fields are not recorded at all.
}

function onToggle(e) {
  const el = e.target
  if (el instanceof HTMLDetailsElement) {
    const summary = el.querySelector('summary')
    track('toggle', summary ? labelOf(summary) : 'details', { open: el.open })
  }
}

function onScroll() {
  const room = document.documentElement.scrollHeight - window.innerHeight
  if (room <= 0) return
  const percent = Math.round((window.scrollY / room) * 100)
  for (const mark of SCROLL_MARKS) {
    if (percent >= mark && !scrolled.has(mark)) {
      scrolled.add(mark)
      track('scroll_depth', null, { percent: mark })
    }
  }
}

/** Install the listeners, once. Called from App. */
export function startActivity() {
  if (!ENABLED || started || typeof window === 'undefined') return
  started = true
  track('visit_start', null, {
    referrer: document.referrer ? 'yes' : 'none',
    touch: navigator.maxTouchPoints > 0,
    dpr: window.devicePixelRatio || 1,
  })
  document.addEventListener('click', onClick, true)
  document.addEventListener('change', onChange, true)
  document.addEventListener('toggle', onToggle, true)
  window.addEventListener('scroll', onScroll, { passive: true })
  document.addEventListener('visibilitychange', () => {
    track(document.visibilityState === 'hidden' ? 'tab_hidden' : 'tab_shown')
    if (document.visibilityState === 'hidden') flushOnLeave()
  })
  window.addEventListener('pagehide', flushOnLeave)
}

export const activityEnabled = ENABLED
