import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const header = 'X-CropScan-Session'
const storageKey = 'cropscan.session'
const json = (body, status = 200) => new Response(JSON.stringify(body), { status })

describe('CropScan browser sessions', () => {
  let api

  beforeEach(async () => {
    vi.resetModules()
    localStorage.clear()
    sessionStorage.clear()
    ;({ api } = await import('./api'))
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('refreshes a stale session and reuses the new token for local launch', async () => {
    localStorage.setItem(storageKey, 'stale')
    const fetch = vi.fn()
      .mockResolvedValueOnce(json({ detail: 'Session expired' }, 401))
      .mockResolvedValueOnce(json({ sessionToken: 'fresh' }, 201))
      .mockResolvedValueOnce(json({ code: 'PAIR-CODE' }, 201))
      .mockResolvedValueOnce(json({ status: 'unity-start' }))
    vi.stubGlobal('fetch', fetch)

    await expect(api.xr.pair()).resolves.toEqual({ code: 'PAIR-CODE' })
    await expect(api.xr.localLaunch('scan-id')).resolves.toEqual({ status: 'unity-start' })

    expect(fetch.mock.calls.map(([url]) => url)).toEqual([
      '/api/xr/pairings', '/api/xr/browser-sessions', '/api/xr/pairings', '/api/xr/local-launch',
    ])
    expect(fetch.mock.calls[0][1].headers.get(header)).toBe('stale')
    expect(fetch.mock.calls[2][1].headers.get(header)).toBe('fresh')
    expect(fetch.mock.calls[3][1].headers.get(header)).toBe('fresh')
    expect(localStorage.getItem(storageKey)).toBe('fresh')
  })

  it('preserves scan ownership when browser storage is blocked', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('storage blocked') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage blocked') })
    const fetch = vi.fn()
      .mockResolvedValueOnce(json({ sessionToken: 'memory' }, 201))
      .mockResolvedValueOnce(json({ id: 'scan-id' }, 201))
      .mockResolvedValueOnce(json({ status: 'unity-start' }))
    vi.stubGlobal('fetch', fetch)

    const scan = await api.createScan(new File(['leaf'], 'leaf.png', { type: 'image/png' }))
    await api.xr.localLaunch(scan.id)

    expect(fetch).toHaveBeenCalledTimes(3)
    expect(fetch.mock.calls[1][1].headers.get(header)).toBe('memory')
    expect(fetch.mock.calls[2][1].headers.get(header)).toBe('memory')
  })

  it('retries a stale upload with the same file and a new owner', async () => {
    localStorage.setItem(storageKey, 'stale')
    const fetch = vi.fn()
      .mockResolvedValueOnce(json({ detail: 'Session expired' }, 401))
      .mockResolvedValueOnce(json({ sessionToken: 'fresh' }, 201))
      .mockResolvedValueOnce(json({ id: 'scan-id' }, 201))
    vi.stubGlobal('fetch', fetch)

    await expect(api.createScan(new File(['leaf'], 'leaf.png'))).resolves.toMatchObject({ id: 'scan-id' })

    expect(fetch.mock.calls[2][1].body).toBe(fetch.mock.calls[0][1].body)
    expect(fetch.mock.calls[2][1].headers.get(header)).toBe('fresh')
  })
})
