import { expect } from '@playwright/test'
import { expectImage, scab, test, withoutWebGL } from './fixtures'

// Compare rendered pixels with a tolerance for WebGL antialiasing and image encoding.
async function imageDifference(page, a, b) {
  return page.evaluate(async ([before, after]) => {
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = 96
    const ctx = canvas.getContext('2d', { willReadFrequently: true })
    const raster = async (encoded) => {
      const image = new Image()
      image.src = 'data:image/png;base64,' + encoded
      await image.decode()
      ctx.clearRect(0, 0, 96, 96)
      ctx.drawImage(image, 0, 0, 96, 96)
      return ctx.getImageData(0, 0, 96, 96).data
    }
    const left = await raster(before)
    const right = await raster(after)
    return left.reduce((sum, value, index) => sum + Math.abs(value - right[index]), 0) / (left.length * 255)
  }, [a.toString('base64'), b.toString('base64')])
}

test('filters by crop, opens a disease link and reloads its notes', async ({ page }) => {
  await withoutWebGL(page)
  await page.goto('/field')
  const list = page.getByRole('navigation', { name: 'Leaf library' }).locator('.leaflist')
  const original = await list.getByRole('link').count()
  await page.getByRole('combobox', { name: 'Crop', exact: true }).selectOption('Apple')
  await expect(list.getByRole('link')).toHaveCount(3)
  expect(original).toBeGreaterThan(3)
  await list.getByRole('link', { name: /Apple scab/i }).click()
  await expect(page).toHaveURL('/field?leaf=apple_apple_scab')
  await expect(page.locator('#leaf-name')).toHaveText(/apple scab/i)
  await expect(page.locator('.leaflist [aria-current="true"]')).toContainText(/apple scab/i)
  await expect(page.getByRole('heading', { name: 'Symptoms' })).toBeVisible()
  await page.reload()
  await expect(page.locator('#leaf-name')).toHaveText(/apple scab/i)
})

test('renders the 3D leaf and changes its view with turn, zoom and recenter', async ({ page }) => {
  await page.goto(`/field?disease=${scab}`)
  const viewer = page.locator('.leafview')
  await expect(viewer).toHaveAttribute('data-state', 'ready', { timeout: 30_000 })
  const autoRotate = page.getByRole('button', { name: 'Auto-rotate' })
  await autoRotate.click()
  await expect(autoRotate).toHaveAttribute('aria-pressed', 'false')
  const canvas = viewer.locator('canvas')
  await expect(canvas).toHaveCSS('opacity', '1')
  const reset = page.getByRole('button', { name: 'Recenter the view' })
  await reset.click()
  const snapshot = () => canvas.screenshot({ animations: 'disabled' })
  const difference = async (other) => imageDifference(page, other, await snapshot())
  const baseline = await snapshot()
  await page.getByRole('button', { name: 'Turn right' }).click()
  await expect.poll(() => difference(baseline)).toBeGreaterThan(0.005)
  const turned = await snapshot()
  await page.getByRole('button', { name: 'Zoom in' }).click()
  await expect.poll(() => difference(turned)).toBeGreaterThan(0.005)
  await reset.click()
  await expect.poll(() => difference(baseline)).toBeLessThan(0.005)
  // An unengaged viewer preserves document wheel scrolling.
  await canvas.hover()
  const y = await page.evaluate(() => scrollY)
  await page.mouse.wheel(0, 260)
  await expect.poll(() => page.evaluate(() => scrollY)).toBeGreaterThan(y)
})

test('shows the static leaf and still lets the user stall and reset the cycle', async ({ page }) => {
  await withoutWebGL(page)
  await page.goto(`/field?disease=${scab}`)
  await expect(page.locator('.leafview')).toHaveAttribute('data-state', 'still')
  await expectImage(page.locator('.leafview__poster'))
  await expect(page.getByText(/The cycle completed/)).toBeVisible()
  await page.getByRole('slider', { name: 'Leaf wetness' }).fill('2')
  await expect(page.getByText(/Stopped at Germination/)).toBeVisible()
  await expect(page.getByText(/below the 9 h this stage needs/)).toBeVisible()
  await expect(page.locator('.timeline [aria-current="step"]')).toContainText('Germination')
  await expect(page.getByText('not reached').first()).toBeVisible()
  await page.getByRole('button', { name: 'Reset to ideal' }).click()
  await expect(page.getByText(/The cycle completed/)).toBeVisible()
  await expect(page.locator('.timeline [aria-current="step"]')).toHaveCount(0)
})

test('loads an available leaf without a cycle and keeps the viewer usable when notes fail', async ({ page, api }) => {
  await withoutWebGL(page)
  let finish
  const waiting = new Promise(resolve => { finish = resolve })
  api.disease = async route => {
    await waiting
    await route.fulfill({ json: { name: 'Apple scab', symptoms: 'Olive spots.' } })
  }
  await page.goto(`/field?disease=${scab}`)
  try {
    await expect(page.getByText('Loading the disease notes…')).toBeVisible()
    await expectImage(page.locator('.leafview__poster'))
  } finally {
    finish()
  }
  await expect(page.getByText('Olive spots.')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Infection cycle' })).toHaveCount(0)
  api.disease = route => route.fulfill({ status: 503, json: { detail: 'Fixture unavailable' } })
  await page.reload()
  await expect(page.getByText('Could not load the notes for this disease. The leaf still works.')).toBeVisible()
  await expectImage(page.locator('.leafview__poster'))
  await expect(page.getByRole('combobox', { name: 'Crop', exact: true })).toBeEnabled()
})
