import { expect } from '@playwright/test'
import { LANGS, translate } from '../src/i18n'
import { expectImage, expectNoOverflow, leafFile, test, withoutWebGL } from './fixtures'

test('all installed languages survive reload and fit the phone form', async ({ page }) => {
  await page.goto('/detect')
  const locale = page.locator('#site-lang')
  for (const { code } of LANGS) {
    await locale.selectOption(code)
    await expect(page.locator('html')).toHaveAttribute('lang', code)
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(translate(code, 'detect.title'))
    await expect(page.getByRole('navigation', { name: translate(code, 'nav.main') }).getByRole('link', { name: translate(code, 'nav.detect') })).toHaveAttribute('aria-current', 'page')
    await expectNoOverflow(page)
    await page.reload()
    await expect(locale).toHaveValue(code)
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(translate(code, 'detect.title'))
  }
})

test('touch scrolls into the live greenhouse without overflowing or leaving Home', async ({ page, context }) => {
  await page.goto('/')
  const journey = page.locator('.intro-journey')
  await expect(journey).toHaveAttribute('data-mode', 'live')
  await expect(page.locator('.intro')).toHaveAttribute('data-canvas', 'true', { timeout: 35_000 })
  await expect(page.getByRole('button', { name: 'Explore the greenhouse' })).toBeVisible()
  await expectNoOverflow(page)
  const touch = await context.newCDPSession(page)
  // Dispatch a native swipe to verify the sticky scene follows touch scrolling.
  for (let swipe = 0; swipe < 10; swipe++) {
    if (await journey.evaluate(el => Number(el.dataset.progress) > 0)) break
    await touch.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: 320, y: 690 }] })
    for (let y = 650; y >= 250; y -= 40) {
      await touch.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: 320, y }] })
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve)))
    }
    await touch.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
  }
  await expect.poll(() => journey.evaluate(el => Number(el.dataset.progress))).toBeGreaterThan(0)
  await expect(page.getByRole('link', { name: 'Skip the greenhouse tour' })).toBeInViewport()
  await expectNoOverflow(page)
  await expect(page).toHaveURL('/')
})

test('uses the portrait poster and fits the upload, result and Field pages', async ({ page }) => {
  await withoutWebGL(page)
  await page.goto('/')
  await expect(page.locator('.intro-journey')).toHaveAttribute('data-mode', 'still')
  await expectImage(page.locator('.intro__poster'))
  expect(await page.locator('.intro__poster').evaluate(el => el.currentSrc)).toContain('portrait')
  await expectNoOverflow(page)
  await page.getByRole('link', { name: 'Detect Disease' }).tap()
  await page.locator('#leaf-file').setInputFiles(leafFile)
  await page.getByRole('link', { name: 'See the full result' }).tap()
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Early blight')
  await expectNoOverflow(page)
  await page.getByRole('link', { name: 'View the leaf in 3D' }).tap()
  await expectImage(page.locator('.leafview__poster'))
  await expectNoOverflow(page)
})
