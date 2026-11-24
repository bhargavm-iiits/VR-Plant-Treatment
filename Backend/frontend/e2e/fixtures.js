import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test as base } from '@playwright/test'

export const leafFile = fileURLToPath(new URL('../src/assets/art/leaf-example-tomato.webp', import.meta.url))
export const imagePath = '/__e2e__/leaf.webp'
export const scab = 'Apple___Apple_scab'
export const earlyBlight = 'Tomato___Early_blight'
const cycles = JSON.parse(readFileSync(new URL('../../data/disease_cycle.json', import.meta.url), 'utf8'))
const diseases = JSON.parse(readFileSync(new URL('../../data/disease_info.json', import.meta.url), 'utf8'))

export const diagnosis = {
  id: 'scan-tested',
  status: 'ok',
  disease_id: earlyBlight,
  confidence: 0.94,
  info: { name: 'Early blight', plant: 'Tomato', symptoms: 'Dark concentric rings.', prevention: 'Inspect leaves regularly.' },
  image_url: imagePath,
  gradcam_url: imagePath,
  can_enter_field: true,
  has_cycle: true,
}

// Deliberately cover API responses only. Real page state and assets still load from Vite.
export const test = base.extend({
  api: [async ({ page }, use) => {
    const scans = new Map([[diagnosis.id, diagnosis]])
    const api = {
      uploads: [],
      scan: async (route) => {
        scans.set(diagnosis.id, diagnosis)
        await route.fulfill({ status: 201, json: diagnosis })
      },
      disease: async (route, id) => {
        const info = diseases[id]
        return info
          ? route.fulfill({ json: { ...info, cycle: cycles[id] } })
          : route.fulfill({ status: 404, json: { detail: 'Unknown disease' } })
      },
      scans,
    }
    await page.route(`**${imagePath}`, route => route.fulfill({ path: leafFile, contentType: 'image/webp' }))
    await page.route('**/api/**', async (route) => {
      const path = decodeURIComponent(new URL(route.request().url()).pathname)
      if (path === '/api/xr/browser-sessions') return route.fulfill({ json: { sessionToken: 'browser-test' } })
      if (path === '/api/scans' && route.request().method() === 'POST') {
        api.uploads.push(route.request())
        return api.scan(route, api.uploads.length)
      }
      if (path.startsWith('/api/scans/')) {
        const result = scans.get(path.split('/').at(-1))
        return result ? route.fulfill({ json: result }) : route.fulfill({ status: 404, json: { detail: 'Unknown scan' } })
      }
      if (path === '/api/diseases') {
        return route.fulfill({ json: Object.entries(diseases).map(([id, info]) => ({ id, name: info.name })) })
      }
      if (path.startsWith('/api/diseases/')) return api.disease(route, path.split('/').at(-1))
      if (path === '/api/model') return route.fulfill({ json: { available: true, model_version: 'e2e-example', metrics: { field_acc: 0.81, lab_acc: 0.96 } } })
      return route.fulfill({ status: 404, json: { detail: 'Outside this test' } })
    })
    await use(api)
  }, { auto: true }],
})

export function withoutWebGL(page) {
  return page.addInitScript(() => {
    const original = HTMLCanvasElement.prototype.getContext
    HTMLCanvasElement.prototype.getContext = function (type, ...args) {
      return /^webgl/.test(type) ? null : original.call(this, type, ...args)
    }
  })
}

export async function expectImage(image) {
  await expect(image).toBeVisible()
  await expect.poll(() => image.evaluate(el => el.complete && el.naturalWidth > 0)).toBe(true)
}

export async function expectNoOverflow(page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
}
