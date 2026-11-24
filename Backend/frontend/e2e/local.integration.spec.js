import { fileURLToPath } from 'node:url'
import { expect, test } from '@playwright/test'
import { expectImage, scab, withoutWebGL } from './fixtures'

const scabPhoto = fileURLToPath(new URL('../public/leaves/apple_apple_scab.webp', import.meta.url))

test('reads seeded disease and cycle data from the real API', async ({ page }) => {
  await withoutWebGL(page)
  await page.goto(`/field?disease=${scab}`)
  await expectImage(page.locator('.leafview__poster'))
  await expect(page.getByText(/The cycle completed/)).toBeVisible()
  await page.getByRole('slider', { name: 'Leaf wetness' }).fill('2')
  await expect(page.getByText(/Stopped at Germination/)).toBeVisible()
})

test('diagnoses a stored leaf, reloads its evidence, opens Field and verifies the ledger', async ({ page, request }) => {
  const health = await request.get('/healthz')
  const status = await health.json()
  test.skip(!status.model_loaded, 'Install the ONNX model in Backend/services/api/model to exercise real inference')
  const before = await (await request.get('/api/ledger/stats')).json()
  await withoutWebGL(page)
  await page.goto('/detect')
  await page.getByLabel('Which crop is this?').selectOption('Apple')
  const posted = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/scans')
  await page.locator('#leaf-file').setInputFiles(scabPhoto)
  const response = await posted
  expect(response.status()).toBe(201)
  const scan = await response.json()
  expect(scan).toMatchObject({ status: 'ok', disease_id: scab, can_enter_field: true })
  await page.getByRole('link', { name: 'See the full result' }).click()
  await expect(page).toHaveURL(`/result?scan=${scan.id}`)
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Apple Scab', level: 1 })).toBeVisible()
  const photo = page.getByRole('img', { name: 'Your uploaded Apple leaf photo' })
  expect(new URL(scan.image_url).pathname).toMatch(/^\/media\//)
  await expect(photo).toHaveAttribute('src', scan.image_url)
  await expectImage(photo)
  await expectImage(page.getByRole('img', { name: 'Where the model looked' }))
  await page.getByRole('link', { name: 'View the leaf in 3D' }).click()
  await expect(page).toHaveURL(`/field?scan=${scan.id}`)
  await expect(page.locator('#leaf-name')).toHaveText(/apple scab/i)
  await expect(page.getByText(/The cycle completed/)).toBeVisible()

  const after = await (await request.get('/api/ledger/stats')).json()
  expect(after.entries).toBe(before.entries + 1)
  const entries = await (await request.get(`/api/ledger?since_seq=${before.head_seq}`)).json()
  expect(entries).toHaveLength(1)
  expect(entries[0]).toMatchObject({
    event_type: 'scan.created',
    subject_id: scan.id,
    payload: { scan_id: scan.id, disease_id: scab },
  })
  const verification = await (await request.get('/api/ledger/verify')).json()
  expect(verification.ok).toBe(true)
  expect(verification.first_break).toBeNull()
})
