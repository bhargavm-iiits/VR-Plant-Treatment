import { expect } from '@playwright/test'
import { expectNoOverflow, test, withoutWebGL } from './fixtures'

test('navbar, heading focus, titles and browser history follow the page', async ({ page }) => {
  await withoutWebGL(page)
  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main' })
  await expect(nav.getByRole('link', { name: 'Home' })).toHaveAttribute('aria-current', 'page')
  for (const [label, path, title] of [
    ['Detect', '/detect', 'Detect Crop Disease'],
    ['Field', '/field', 'Leaf library'],
    ['About', '/about', 'About CropScan XR'],
  ]) {
    await nav.getByRole('link', { name: label }).click()
    await expect(page).toHaveURL(path)
    await expect(nav.getByRole('link', { name: label })).toHaveAttribute('aria-current', 'page')
    await expect(page.getByRole('heading', { name: title, level: 1 })).toBeFocused()
    await expect(page).toHaveTitle(new RegExp(title))
    await expectNoOverflow(page)
  }
  await page.goBack()
  await expect(page.getByRole('heading', { name: 'Leaf library', level: 1 })).toBeVisible()
  await page.goForward()
  await expect(page.getByRole('heading', { name: 'About CropScan XR', level: 1 })).toBeVisible()
})

test('skip link and first navigation controls work from the keyboard', async ({ page }) => {
  await page.goto('/detect')
  await page.keyboard.press('Tab')
  const skip = page.getByRole('link', { name: 'Skip to main content' })
  await expect(skip).toBeFocused()
  await expect(skip).toHaveCSS('outline-style', 'solid')
  await page.keyboard.press('Enter')
  await expect(page.getByRole('main')).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('navigation', { name: 'Breadcrumb' }).getByRole('link', { name: 'Home' })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByLabel('Which crop is this?')).toBeFocused()
})

test('an unknown URL offers working Home and Detect recovery links', async ({ page }) => {
  await withoutWebGL(page)
  await page.goto('/unknown-route')
  await expect(page.getByRole('heading', { name: 'That page does not exist.' })).toBeVisible()
  await expect(page.getByRole('main')).toContainText('/unknown-route')
  await page.getByRole('link', { name: 'Back to home' }).click()
  await expect(page).toHaveURL('/')
  await page.goto('/unknown-route')
  await page.getByRole('link', { name: 'Scan a leaf' }).click()
  await expect(page).toHaveURL('/detect')
})

test('About reveals a section on wheel scroll and shows the deployed model metrics', async ({ page }) => {
  await page.goto('/about')
  await expect(page.getByText('81.0%')).toBeVisible()
  const section = page.locator('[aria-labelledby="about-data"]')
  await expect(section).toHaveAttribute('data-reveal', 'waiting')
  await page.mouse.wheel(0, 4000)
  await expect(section).toHaveAttribute('data-reveal', 'seen')
  await expect(section).toHaveCSS('opacity', '1')
  await expect(page).toHaveURL('/about')
})
