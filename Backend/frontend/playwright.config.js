import { existsSync } from 'node:fs'
import { defineConfig, devices } from '@playwright/test'

const frontend = 'http://127.0.0.1:5299'
const api = 'http://127.0.0.1:8299'
const integration = process.env.E2E_INTEGRATION === '1'
const venvPython = process.platform === 'win32'
  ? '../services/api/.venv/Scripts/python.exe'
  : '../services/api/.venv/bin/python'
const python = process.env.E2E_PYTHON || (existsSync(venvPython) ? venvPython : 'python')
const browser = {
  browserName: 'chromium',
  channel: process.env.E2E_BROWSER_CHANNEL,
  launchOptions: { args: ['--enable-unsafe-swiftshader'] },
}

export default defineConfig({
  testDir: './e2e',
  outputDir: '../.local/playwright-results',
  reporter: [['list'], ['html', { open: 'never', outputFolder: '../.local/playwright-report' }]],
  timeout: 75_000,
  expect: { timeout: 10_000 },
  workers: 2,
  use: {
    baseURL: frontend,
    serviceWorkers: 'block',
    locale: 'en-US',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: [
    ...(integration ? [{
      command: `"${python}" e2e/serve_api.py`,
      url: `${api}/docs`,
      timeout: 120_000,
    }] : []),
    {
      command: 'node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 5299 --strictPort',
      url: frontend,
      env: { API_URL: api, VITE_API_URL: '', VITE_ACTIVITY: 'off' },
      timeout: 60_000,
    },
  ],
  projects: [
    {
      name: 'chromium',
      testIgnore: ['**/*.mobile.spec.js', '**/*.integration.spec.js'],
      use: { ...browser, viewport: { width: 1280, height: 720 } },
    },
    {
      name: 'mobile',
      testMatch: '**/*.mobile.spec.js',
      use: { ...devices['Pixel 7'], ...browser, viewport: { width: 390, height: 844 }, deviceScaleFactor: 1 },
    },
    ...(integration ? [{
      name: 'local-api',
      testMatch: '**/*.integration.spec.js',
      use: { ...browser, viewport: { width: 1280, height: 720 }, reducedMotion: 'reduce' },
    }] : []),
  ],
})
