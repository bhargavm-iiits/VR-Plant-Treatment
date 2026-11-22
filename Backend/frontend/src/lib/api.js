/**
 * API client.
 *
 * The backend moved from /predict to /api/scans during the rebuild. Rather
 * than rewrite Result.jsx, `adaptScan` maps the new response onto the shape
 * the page already renders — class_name, confidence as a percentage, top3 —
 * and carries the genuinely new fields alongside it.
 */

const BASE = import.meta.env.VITE_API_URL || ''

// CropScan XR: a server-issued browser session. Scans uploaded with it can be opened in the VR app, and the VR player
// is paired to it. Only the token is kept, in this browser's storage; the server stores its hash.
const SESSION_KEY = 'cropscan.session'
const SESSION_HEADER = 'X-CropScan-Session'

const SESSION_STORES = ['localStorage', 'sessionStorage']
let pageSession = null
let rejectedSession = null
let sessionPromise = null

function storedSession() {
  for (const store of SESSION_STORES) {
    try {
      const token = globalThis[store].getItem(SESSION_KEY)
      if (token && token !== rejectedSession) return token
    } catch {
      // Storage can be disabled in an embedded or private browser.
    }
  }
  return pageSession
}

function forgetSession(token) {
  if (pageSession === token) pageSession = null
  rejectedSession = token
  for (const store of SESSION_STORES) {
    try {
      const storage = globalThis[store]
      if (storage.getItem(SESSION_KEY) === token) storage.removeItem(SESSION_KEY)
    } catch {
      // Ignore unavailable or read-only storage.
    }
  }
}

/** The browser session token, created on first use. Resolves to null if the API cannot issue one. */
export function ensureSession() {
  const existing = storedSession()
  if (existing) return Promise.resolve(existing)
  sessionPromise ??= fetch(`${BASE}/api/xr/browser-sessions`, { method: 'POST' })
    .then((r) => (r.ok ? r.json() : null))
    .then((body) => {
      const token = body?.sessionToken ?? null
      if (token) {
        pageSession = token
        for (const store of SESSION_STORES) {
          try {
            globalThis[store].setItem(SESSION_KEY, token)
            break
          } catch {
            // The request can still use the in-memory token.
          }
        }
      }
      return token
    })
    .catch(() => null)
    .finally(() => {
      sessionPromise = null
    })
  return sessionPromise
}

/** The cropscanxr: link that opens a scan in the installed Windows VR app on this computer. */
export function vrAppLink(scanId) {
  return `cropscanxr://open-field?scan=${encodeURIComponent(scanId)}`
}

async function request(path, init = {}, token = storedSession()) {
  const headers = new Headers(init.headers || {})
  if (token && !headers.has(SESSION_HEADER)) headers.set(SESSION_HEADER, token)
  const response = await fetch(`${BASE}${path}`, { ...init, headers })
  if (!response.ok) {
    let detail = `Request failed (${response.status})`
    try {
      const body = await response.json()
      if (body?.detail) detail = String(body.detail)
    } catch {
      // Non-JSON error body; the status is the message.
    }
    const error = new Error(detail)
    error.status = response.status
    throw error
  }
  return response.json()
}

async function sessionRequest(path, init) {
  const requireToken = async () => {
    const token = await ensureSession()
    if (!token) throw new Error('CropScan could not start a browser session. Check the connection and try again.')
    return token
  }
  const token = await requireToken()
  try {
    return await request(path, init, token)
  } catch (error) {
    if (error.status !== 401) throw error
    // A reset API database may no longer know a token saved by this browser.
    // These endpoints reject it before doing the upload, pairing or launch.
    forgetSession(token)
    return request(path, init, await requireToken())
  }
}

/** New ScanOut -> the shape Result.jsx already expects, plus the new fields. */
export function adaptScan(scan) {
  return {
    // what the existing page reads
    class_name: scan.disease_id || scan.top3?.[0]?.disease_id || '',
    confidence: (scan.confidence ?? 0) * 100,
    top3: (scan.top3 || []).map((t) => ({
      class_name: t.disease_id,
      name: t.name, // already in the requested language
      confidence: t.confidence * 100,
    })),
    disease_info: scan.info,
    disease_info_te: null, // the API already resolves language server-side

    // new
    id: scan.id,
    status: scan.status,
    modelVersion: scan.model_version,
    gradcamUrl: scan.gradcam_url,
    storedImageUrl: scan.image_url,
    hasCycle: scan.has_cycle,
    canEnterField: scan.can_enter_field,
    fieldBlockedReason: scan.field_blocked_reason,
    canEnterVr: scan.can_enter_vr, // the installed Windows VR app (Enter the field)
    vrBlockedReason: scan.vr_blocked_reason,
    looksLikeCrop: scan.looks_like_crop, // set when the photo plainly is not the picked crop
  }
}

export const api = {
  async createScan(file, lang = 'en', crop = '') {
    // The scan is owned by this browser session, so only it (and its paired VR player) may open it in VR.
    const form = new FormData()
    form.append('file', file)
    // crop: the plant the farmer picked; '' lets every crop compete
    const q = crop ? `&crop=${encodeURIComponent(crop)}` : ''
    const scan = await sessionRequest(`/api/scans?lang=${lang}${q}`, { method: 'POST', body: form })
    return adaptScan(scan)
  },

  async getScan(id, lang = 'en') {
    return adaptScan(await request(`/api/scans/${id}?lang=${lang}`))
  },

  listDiseases: (params = {}) => {
    const q = new URLSearchParams()
    if (params.lang) q.set('lang', params.lang)
    if (params.crop) q.set('crop', params.crop)
    if (params.hasCycle !== undefined) q.set('has_cycle', String(params.hasCycle))
    return request(`/api/diseases?${q}`)
  },

  getDisease: (id, lang = 'en') =>
    request(`/api/diseases/${encodeURIComponent(id)}?lang=${lang}`),

  getCycle: (id, lang = 'en') =>
    request(`/api/diseases/${encodeURIComponent(id)}/cycle?lang=${lang}`),

  listVideos: (diseaseId, lang = 'en') =>
    request(`/api/videos?disease_id=${encodeURIComponent(diseaseId)}&lang=${lang}`),

  health: () => request('/healthz'),

  xr: {
    /** A one-time code to type into the VR player's pairing window. */
    pair: () => sessionRequest('/api/xr/pairings', { method: 'POST' }),
    devices: () => sessionRequest('/api/xr/devices'),
    /** See VR Demo on the laptop: asks the API on this computer to open CropScan XR with no scan, starting Unity if it is closed. */
    localDemo: () => request('/api/xr/local-demo', { method: 'POST' }),
    /** Whether this API runs on this computer and can open CropScan XR itself (the laptop demo). */
    async localLaunchInfo() {
      try {
        return await request('/api/xr/local-launch')
      } catch {
        return { available: false }
      }
    },
    /** Asks the API on this computer to open the scan in CropScan XR (running app, or start the installed one). */
    localLaunch(scanId) {
      return sessionRequest('/api/xr/local-launch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ scanId }),
      })
    },
    /** This browser's VR run for a scan, with its latest launch; null before the scan is opened in VR. */
    async sessionForScan(scanId) {
      try {
        return await request(`/api/xr/sessions?scanId=${encodeURIComponent(scanId)}`)
      } catch (error) {
        if (error.status === 404 || error.status === 401) return null
        throw error
      }
    },
  },

  /** Deployed model's measured metrics, from its meta.json. 503 when none. */
  model: () => request('/api/model'),
}
