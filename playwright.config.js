const { defineConfig, devices } = require('@playwright/test');
const sitePort = process.env.REX_SITE_TEST_PORT || '4173';
const siteUrl = `http://127.0.0.1:${sitePort}`;

module.exports = defineConfig({
  testDir: './tests',
  testMatch: 'pages.spec.js',
  timeout: 30000,
  expect: { timeout: 5000 },
  fullyParallel: true,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI
    ? [['line'], ['html', { outputFolder: 'playwright-report', open: 'never' }]]
    : 'list',
  use: {
    baseURL: siteUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    permissions: ['clipboard-read', 'clipboard-write'],
  },
  projects: [
    {
      name: 'desktop-chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'mobile-chromium',
      use: { ...devices['Pixel 7'] },
    },
  ],
  webServer: {
    command: `python3 -m http.server ${sitePort} --directory docs --bind 127.0.0.1`,
    url: siteUrl,
    // Reusing an arbitrary process on this port can make every assertion inspect the wrong site.
    reuseExistingServer: false,
    timeout: 20000,
  },
});
