import { defineConfig, devices } from '@playwright/test';

const port = Number(process.env['E2E_PORT'] ?? 5180);

/**
 * End-to-end tests: each game played in Chromium against the real API and the production build,
 * started by e2e/serve.sh on a fresh SQLite database with offline test data.
 *
 *   npm run e2e                       # build the app, start the site, run every test
 *   npx playwright test e2e/spordle   # one file, once the app is built
 */
export default defineConfig({
  testDir: 'e2e',
  outputDir: 'test-results/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: 0,
  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]] : 'list',
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    // Tiles flip instantly, and the production build's service worker cannot cache a stale app.
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        // For machines that cannot download Playwright's own browser.
        launchOptions: process.env['E2E_CHROMIUM'] ? { executablePath: process.env['E2E_CHROMIUM'] } : {},
      },
    },
  ],
  webServer: {
    command: 'bash e2e/serve.sh',
    url: `http://127.0.0.1:${port}/health`,
    env: { E2E_PORT: String(port) },
    // Real-photo review imports drafts; always give it its own disposable fixture database.
    reuseExistingServer: !process.env['CI'] && !process.env['E2E_CURATED_ASSETS'],
    timeout: 240_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
});
