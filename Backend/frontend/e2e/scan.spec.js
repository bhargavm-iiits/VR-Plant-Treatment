import { readFile } from 'node:fs/promises'
import { expect } from '@playwright/test'
import { diagnosis, expectImage, imagePath, leafFile, test, withoutWebGL } from './fixtures'

const picker = (page) => page.locator('#leaf-file')
const openResult = (page) => page.getByRole('link', { name: 'See the full result' }).click()
const respond = (route, data) => route.fulfill({ status: 201, json: data })
const deferred = () => {
  let release
  const promise = new Promise(resolve => { release = resolve })
  return { promise, release }
}

test('chooses a crop and photo, shows progress, then persists the result and opens the leaf', async ({ page, api }) => {
  await withoutWebGL(page)
  const response = deferred()
  api.scan = async route => {
    await response.promise
    await respond(route, diagnosis)
  }
  await page.goto('/detect')
  await page.getByLabel('Which crop is this?').selectOption('Tomato')
  const fileChooser = page.waitForEvent('filechooser')
  await page.getByText('Choose a leaf photo', { exact: true }).click()
  await (await fileChooser).setFiles(leafFile)
  try {
    await expectImage(page.getByRole('img', { name: 'Leaf preview' }))
    await expect(page.getByText(/Analysing your photo/)).toBeVisible()
    await expect.poll(() => api.uploads.length).toBe(1)
    expect(new URL(api.uploads[0].url()).searchParams.get('crop')).toBe('Tomato')
    expect(api.uploads[0].headers()['content-type']).toContain('multipart/form-data')
    expect(api.uploads[0].headers()['x-cropscan-session']).toBe('browser-test')
  } finally {
    response.release()
  }
  await expect(page.getByRole('heading', { name: 'Early blight' })).toBeVisible()
  await openResult(page)
  await expect(page).toHaveURL(/\/result\?scan=scan-tested$/)
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Early blight', level: 1 })).toBeVisible()
  const photo = page.getByRole('img', { name: 'Your uploaded Tomato leaf photo' })
  await expect(photo).toHaveAttribute('src', imagePath)
  await expectImage(photo)
  await expectImage(page.getByRole('img', { name: 'Where the model looked' }))
  await expect(page.getByRole('meter', { name: 'Confidence' })).toHaveAttribute('aria-valuenow', '94')
  await page.getByRole('link', { name: 'View the leaf in 3D' }).click()
  await expect(page).toHaveURL(/\/field\?scan=scan-tested$/)
  await expect(page.locator('#leaf-name')).toContainText(/early blight/i)
})

test('drops a photo and resubmits the same file when the crop changes', async ({ page, api }) => {
  await page.goto('/detect')
  const data = await page.evaluateHandle(async () => {
    const photo = await fetch('/__e2e__/leaf.webp').then(r => r.blob())
    const transfer = new DataTransfer()
    transfer.items.add(new File([photo], 'dropped.webp', { type: 'image/webp' }))
    return transfer
  })
  await page.locator('.detect__photo').dispatchEvent('dragover', { dataTransfer: data })
  await expect(page.getByText('Drop the photo to add it')).toBeVisible()
  await page.locator('.detect__photo').dispatchEvent('drop', { dataTransfer: data })
  await expect(page.getByRole('heading', { name: 'Early blight' })).toBeVisible()
  await page.getByLabel('Which crop is this?').selectOption('Apple')
  await expect.poll(() => api.uploads.length).toBe(2)
  expect(new URL(api.uploads[1].url()).searchParams.get('crop')).toBe('Apple')
  expect(api.uploads[1].postData()).toContain('dropped.webp')
})

test('rejects unsupported and oversized files before submitting', async ({ page, api }) => {
  await page.goto('/detect')
  await picker(page).setInputFiles({ name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('not a photo') })
  await expect(page.getByRole('alert')).toContainText('Please upload an image file')
  await picker(page).setInputFiles({ name: 'large.png', mimeType: 'image/png', buffer: Buffer.alloc(10 * 1024 * 1024 + 1) })
  await expect(page.getByRole('alert')).toContainText('larger than 10 MB')
  await expect(picker(page)).toHaveAttribute('aria-invalid', 'true')
  expect(api.uploads).toHaveLength(0)
})

for (const [code, message] of [
  [400, 'could not be read as an image'],
  [502, 'could not save the photo'],
  [503, 'no model is deployed'],
]) {
  test(`preserves the photo and retries an upload after HTTP ${code}`, async ({ page, api }) => {
    api.scan = (route, index) => index === 1
      ? route.fulfill({ status: code, json: { detail: 'Fixture error' } })
      : respond(route, diagnosis)
    await page.goto('/detect')
    await picker(page).setInputFiles(leafFile)
    await expect(page.getByRole('alert')).toContainText(message)
    await expectImage(page.getByRole('img', { name: 'Leaf preview' }))
    await page.getByRole('button', { name: 'Try again' }).click()
    await expect(page.getByRole('heading', { name: 'Early blight' })).toBeVisible()
    expect(api.uploads).toHaveLength(2)
  })
}

test('keeps the newer photo and result when the first upload finishes late', async ({ page, api }) => {
  const first = deferred()
  const older = { ...diagnosis, id: 'old-scan', info: { ...diagnosis.info, name: 'Old result' } }
  api.scan = async (route, index) => {
    if (index === 1) await first.promise
    await respond(route, index === 1 ? older : diagnosis)
  }
  await page.goto('/detect')
  await picker(page).setInputFiles(leafFile)
  await expect.poll(() => api.uploads.length).toBe(1)
  try {
    await picker(page).setInputFiles({ name: 'replacement.webp', mimeType: 'image/webp', buffer: await readFile(leafFile) })
    await expect(page.getByRole('heading', { name: 'Early blight' })).toBeVisible()
    const completed = page.waitForResponse(async r => r.url().includes('/api/scans?') && (await r.json()).id === older.id)
    first.release()
    await completed
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    await expect(page.getByRole('link', { name: 'See the full result' })).toHaveAttribute('href', '/result?scan=scan-tested')
    await expect(page.getByText('Old result')).toHaveCount(0)
  } finally {
    first.release()
  }
})

for (const [label, data, heading] of [
  ['uncertain', {}, 'We could not find a crop leaf we recognise in this photo'],
  ['not a leaf', { top3: [{ disease_id: 'Not_a_leaf', confidence: 0.63 }] }, 'This does not look like a crop leaf'],
  ['wrong crop', { looks_like_crop: 'Tomato' }, 'This looks like Tomato, not the crop you picked'],
]) {
  test(`offers a retake for ${label} without a diagnosis meter`, async ({ page, api }) => {
    const scan = { ...diagnosis, status: 'uncertain', disease_id: null, info: null, has_cycle: false, can_enter_field: false, ...data }
    api.scans.set(scan.id, scan)
    api.scan = route => respond(route, scan)
    await page.goto('/detect')
    await picker(page).setInputFiles(leafFile)
    await expect(page.getByRole('heading', { name: heading })).toBeVisible()
    await openResult(page)
    await expect(page.getByRole('heading', { name: heading, level: 1 })).toBeVisible()
    await expect(page.getByRole('meter')).toHaveCount(0)
    await page.getByRole('link', { name: 'Take another photo' }).click()
    await expect(page).toHaveURL('/detect')
  })
}

test('shows healthy guidance for a confirmed healthy leaf', async ({ page, api }) => {
  const scan = {
    ...diagnosis,
    disease_id: 'Tomato___healthy',
    info: { name: 'Healthy tomato', plant: 'Tomato', is_healthy: true },
    gradcam_url: null, has_cycle: false, can_enter_field: false,
  }
  api.scans.set(scan.id, scan)
  api.scan = route => respond(route, scan)
  await page.goto('/detect')
  await picker(page).setInputFiles(leafFile)
  await expect(page.getByText('Healthy Plant', { exact: true })).toBeVisible()
  await openResult(page)
  await expect(page.getByRole('heading', { name: 'Your plant looks healthy!' })).toBeVisible()
  await expect(page.getByText('Model attention is not available for this scan.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'View the leaf in 3D' })).toHaveCount(0)
})

test('recovers from missing and unknown scan IDs', async ({ page }) => {
  for (const [url, heading] of [
    ['/result', 'No result found'],
    ['/result?scan=unknown', 'That scan could not be found'],
  ]) {
    await page.goto(url)
    await expect(page.getByRole('heading', { name: heading })).toBeVisible()
    await page.getByRole('link', { name: 'Go to Detect' }).click()
    await expect(page).toHaveURL('/detect')
  }
})
