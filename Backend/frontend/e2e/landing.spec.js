import { expect } from '@playwright/test'
import { expectImage, expectNoOverflow, test, withoutWebGL } from './fixtures'

const journey = (page) => page.locator('.intro-journey')
const hero = (page) => journey(page).locator('.intro')
const liveCanvas = (page) => hero(page).locator('canvas').first()
const tourButton = (page) => page.getByRole('button', { name: 'Explore the greenhouse' })
const skipLink = (page) => page.getByRole('link', { name: 'Skip the greenhouse tour' })
const settled = (page) => expect(hero(page)).toHaveAttribute('data-phase', 'done', { timeout: 45_000 })
const progress = (page) => journey(page).evaluate(el => Number(el.dataset.progress))

async function journeyGeometry(page) {
  return journey(page).evaluate((track) => {
    const stage = track.querySelector('.intro')
    const stickyTop = parseFloat(getComputedStyle(stage).top) || 0
    return {
      start: track.getBoundingClientRect().top + scrollY - stickyTop,
      travel: track.offsetHeight - stage.offsetHeight,
      stickyTop,
    }
  })
}

const seek = (page, geo, fraction) =>
  page.evaluate(top => scrollTo({ top, behavior: 'instant' }), Math.round(geo.start + geo.travel * fraction))

async function expectLiveJourney(page) {
  await settled(page)
  await expect(journey(page)).toHaveAttribute('data-mode', 'live')
  await expect(hero(page)).toHaveAttribute('data-canvas', 'true', { timeout: 45_000 })
  await expect(liveCanvas(page)).toBeVisible()
}

async function expectPinned(page, geo) {
  await expect.poll(async () => {
    const top = await hero(page).evaluate(el => el.getBoundingClientRect().top)
    return Math.abs(top - geo.stickyTop)
  }).toBeLessThanOrEqual(2)
}

test('plays the original loader on the first visit and replays it with the intro query', async ({ page }) => {
  await page.addInitScript(() => Object.defineProperty(navigator, 'webdriver', { get: () => false }))
  await page.goto('/')
  await expect(page.getByRole('progressbar', { name: 'Loading progress' })).toBeVisible()
  await expectLiveJourney(page)

  await page.getByRole('link', { name: 'Detect Disease', exact: true }).click()
  await expect(page).toHaveURL('/detect')
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Home' }).click()
  await settled(page)
  await expect(page.getByRole('progressbar')).toHaveCount(0)

  await page.goto('/?intro')
  await expect(page.getByRole('progressbar', { name: 'Loading progress' })).toBeVisible()
  await settled(page)
  await hero(page).getByRole('link', { name: 'Enter the field' }).click()
  await expect(page).toHaveURL('/field')
})

test('moves through the live 3D journey on scroll, reverses and releases to the next section', async ({ page }) => {
  await page.goto('/')
  await expectLiveJourney(page)
  await expectNoOverflow(page)
  const geo = await journeyGeometry(page)
  expect(geo.travel).toBeGreaterThan(800)

  await seek(page, geo, 0)
  await expect(journey(page)).toHaveAttribute('data-beat', 'landing')
  await page.mouse.move(640, 400)
  await page.mouse.wheel(0, 500)
  await expect.poll(() => progress(page)).toBeGreaterThan(0.02)

  for (const [fraction, beat] of [[0.08, 'landing'], [0.34, 'threshold'], [0.58, 'greenhouse'], [0.85, 'leaf']]) {
    await seek(page, geo, fraction)
    await expect(journey(page)).toHaveAttribute('data-beat', beat)
    await expect.poll(async () => Math.abs(await progress(page) - fraction)).toBeLessThan(0.06)
    await expect(liveCanvas(page)).toBeVisible()
    await expectPinned(page, geo)
  }

  await seek(page, geo, 0.34)
  await expect(journey(page)).toHaveAttribute('data-beat', 'threshold')
  await seek(page, geo, 0)
  await expect(journey(page)).toHaveAttribute('data-beat', 'landing')
  await expect.poll(() => progress(page)).toBeLessThan(0.02)

  await seek(page, geo, 1)
  await page.evaluate(() => scrollBy({ top: innerHeight * 0.7, behavior: 'instant' }))
  await expect.poll(() => hero(page).evaluate(el => el.getBoundingClientRect().top)).toBeLessThan(geo.stickyTop - 100)
  await expect(page.getByRole('heading', { name: 'How it works' })).toBeInViewport()
  await expect(page).toHaveURL('/')
})

test('the explore button starts the journey and the skip link moves focus to How it works', async ({ page }) => {
  await page.goto('/')
  await expectLiveJourney(page)
  await expect(tourButton(page)).toBeVisible()
  await tourButton(page).focus()
  await page.keyboard.press('Enter')
  await expect(journey(page)).toHaveAttribute('data-beat', 'threshold')
  await expect.poll(() => progress(page)).toBeGreaterThan(0.2)

  const skip = skipLink(page)
  await expect(skip).toBeVisible()
  await skip.focus()
  await page.keyboard.press('Enter')
  const how = page.getByRole('heading', { name: 'How it works' })
  await expect(how).toBeFocused()
  await expect(how).toBeInViewport()
})

test('uses the settled poster and no canvas with reduced motion', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto('/?intro')
  await settled(page)
  await expect(journey(page)).toHaveAttribute('data-mode', 'still')
  await expect(hero(page).locator('canvas')).toHaveCount(0)
  await expect(page.locator('.falling-leaves')).toHaveCSS('display', 'none')
  await expectImage(hero(page).locator('.intro__poster'))
  await expect(page.getByRole('link', { name: 'Detect Disease', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'How it works' })).toHaveCSS('opacity', '1')

  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await expect(journey(page)).toHaveAttribute('data-mode', 'live')
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await expect(journey(page)).toHaveAttribute('data-mode', 'still')
  await expect(hero(page).locator('canvas')).toHaveCount(0)
})

test('uses the still homepage when WebGL is unavailable', async ({ page }) => {
  await withoutWebGL(page)
  await page.goto('/?intro')
  await settled(page)
  await expect(journey(page)).toHaveAttribute('data-mode', 'still')
  await expect(hero(page)).toHaveAttribute('data-scene', 'false')
  await expect(hero(page).locator('canvas')).toHaveCount(0)
  await expectImage(hero(page).locator('.intro__poster'))
  await expect(page.getByRole('link', { name: 'Detect Disease' })).toBeVisible()
})

test('falls back to the static greenhouse when the interior texture fails', async ({ page }) => {
  let blocked = 0
  await page.route('**/journey-greenhouse.webp', route => {
    blocked++
    return route.abort('failed')
  })
  await page.goto('/?intro')
  await expect(hero(page)).toHaveAttribute('data-scene', 'false', { timeout: 45_000 })
  expect(blocked).toBeGreaterThan(0)
  await expectImage(hero(page).locator('.intro__poster'))
  await page.getByRole('link', { name: 'Detect Disease', exact: true }).click()
  await expect(page).toHaveURL('/detect')
})

// 50% browser zoom on a 1280 × 720 window reports a 2560 × 1440 CSS viewport.
test.describe('at 50% zoom', () => {
  test.use({ viewport: { width: 2560, height: 1440 }, deviceScaleFactor: 0.5 })

  test('keeps the journey pinned and its controls reachable', async ({ page }) => {
    await page.goto('/')
    await expectLiveJourney(page)
    expect(await page.evaluate(() => devicePixelRatio)).toBe(0.5)
    await expectNoOverflow(page)
    const geo = await journeyGeometry(page)
    for (const [fraction, beat] of [[0.35, 'threshold'], [0.6, 'greenhouse'], [0.87, 'leaf']]) {
      await seek(page, geo, fraction)
      await expect(journey(page)).toHaveAttribute('data-beat', beat)
      await expectPinned(page, geo)
      await expect(liveCanvas(page)).toBeVisible()
    }
    const skip = skipLink(page)
    await expect(skip).toBeInViewport()
    await skip.click()
    await expect(page.getByRole('heading', { name: 'How it works' })).toBeFocused()
  })
})
