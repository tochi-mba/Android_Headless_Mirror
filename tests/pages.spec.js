const { test, expect } = require('@playwright/test');
const AxeBuilder = require('@axe-core/playwright').default;

const REX = {
  ink: '#080A09',
  panel: '#111512',
  raised: '#181E19',
  line: '#29302A',
  text: '#F2F5EE',
  muted: '#858D83',
  signal: '#D7FF3F',
  live: '#FF774D',
};

const REMOVED_FEATURES = [/root v1/i, /privileged/i, /miracast/i, /wireless display/i, /control center/i, /stop\.flag/i, /START_NOW/i, /supervisor/i];

test.describe('Android Headless Mirror site', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
  });

  test('loads without page errors or failed local requests', async ({ page }) => {
    const pageErrors = [];
    const failedLocal = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    page.on('response', response => {
      const url = new URL(response.url());
      if (url.origin === 'http://127.0.0.1:4173' && response.status() >= 400) {
        failedLocal.push(`${response.status()} ${url.pathname}`);
      }
    });

    await page.reload();
    await page.waitForLoadState('networkidle');

    expect(pageErrors).toEqual([]);
    expect(failedLocal).toEqual([]);
    await expect(page).toHaveTitle(/Android Headless Mirror.*REX Technologies/);
  });

  test('shows the REX Technologies lockup and the one-window promise', async ({ page }) => {
    await expect(page.locator('.brand-company')).toHaveText('REX Technologies');
    await expect(page.locator('.brand-name')).toHaveText('Android Headless Mirror');
    await expect(page.locator('h1')).toContainText('One window');
    await expect(page.locator('.app-window .app-tabs span')).toHaveText(['Controls', 'Phone', 'Settings', 'Info']);
  });

  test('uses the REX palette at runtime', async ({ page }) => {
    const values = await page.evaluate(() => {
      const styles = getComputedStyle(document.documentElement);
      const read = name => styles.getPropertyValue(name).trim().toUpperCase();
      return {
        ink: read('--ink'), panel: read('--panel'), raised: read('--raised'), line: read('--line'),
        text: read('--text'), muted: read('--muted'), signal: read('--signal'), live: read('--live'),
      };
    });
    expect(values).toEqual(REX);
    await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(8, 10, 9)');
  });

  test('has product metadata for search and sharing', async ({ page }) => {
    await expect(page.locator('meta[name="description"]')).toHaveAttribute('content', /one window/i);
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', 'https://tochi-mba.github.io/Android_Headless_Mirror/');
    await expect(page.locator('meta[property="og:title"]')).toHaveAttribute('content', /REX Technologies/);
    await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', 'site.webmanifest');
  });

  test('does not advertise removed features', async ({ page }) => {
    const text = await page.locator('body').innerText();
    for (const pattern of REMOVED_FEATURES) {
      expect(text, `page mentions ${pattern}`).not.toMatch(pattern);
    }
  });

  test('every navigation anchor resolves to a section', async ({ page }) => {
    const anchors = await page.locator('.site-nav a[href^="#"]').evaluateAll(links => links.map(link => link.getAttribute('href')));
    expect(anchors.length).toBeGreaterThanOrEqual(6);
    for (const anchor of anchors) {
      await expect(page.locator(anchor)).toHaveCount(1);
    }
  });

  test('external links open safely in a new tab', async ({ page }) => {
    const external = page.locator('a[target="_blank"]');
    const count = await external.count();
    expect(count).toBeGreaterThan(0);
    for (let i = 0; i < count; i++) {
      await expect(external.nth(i)).toHaveAttribute('rel', /noopener/);
      await expect(external.nth(i)).toHaveAttribute('href', /^https:\/\//);
    }
  });

  test('gestures section separates phone pinch from Alt host zoom', async ({ page }) => {
    const section = page.locator('#gestures');
    await expect(section.locator('.gesture-kicker').nth(0)).toHaveText(/no modifier/i);
    await expect(section.locator('.gesture-kicker').nth(1)).toHaveText(/hold alt/i);
    await expect(section).toContainText(/phone receives nothing/i);
    await expect(section.locator('.shortcut-table kbd', { hasText: 'F11' })).toBeVisible();
  });

  test('the download button links straight to the installer without any script', async ({ page }) => {
    await page.route('https://api.github.com/**', route => route.abort());
    await page.reload();
    const button = page.locator('#download');
    await expect(button).toHaveAttribute('href', 'https://github.com/tochi-mba/Android_Headless_Mirror/releases/latest/download/AndroidHeadlessMirror-Setup.exe');
    await expect(button).toHaveAttribute('download', '');
    await expect(page.locator('#download-meta')).toContainText(/Windows 10 or 11/);
    await expect(page.locator('a[data-latest-installer][href="https://github.com/tochi-mba/Android_Headless_Mirror/releases/latest/download/AndroidHeadlessMirror-Setup.exe"]')).toHaveCount(2);
  });

  test('the release details decorate the download button when GitHub answers', async ({ page }) => {
    await page.route('https://api.github.com/repos/tochi-mba/Android_Headless_Mirror/releases/latest', route => route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ tag_name: 'v2.0.1', assets: [{
        name: 'AndroidHeadlessMirror-Setup.exe',
        browser_download_url: 'https://github.com/tochi-mba/Android_Headless_Mirror/releases/download/v2.0.1/AndroidHeadlessMirror-Setup.exe',
        size: 58720256,
      }] }),
    }));
    await page.reload();
    const button = page.locator('#download');
    await expect(button).toBeVisible();
    await expect(button).toHaveAttribute('href', /releases\/download\/v2\.0\.1\/AndroidHeadlessMirror-Setup\.exe$/);
    await expect(button).toHaveAttribute('download', 'AndroidHeadlessMirror-Setup.exe');
    await expect(page.locator('#download-meta')).toContainText(/v2\.0\.1.*Windows 10 or 11/);
    await expect(page.locator('a[data-latest-installer][href$="/v2.0.1/AndroidHeadlessMirror-Setup.exe"]')).toHaveCount(2);
  });

  test('install steps are ordered and the clone command copies', async ({ page, context, browserName }) => {
    test.skip(browserName !== 'chromium', 'clipboard permissions are only granted on Chromium');
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    const steps = page.locator('.install-steps li');
    await expect(steps).toHaveCount(4);
    await expect(steps.nth(0)).toContainText('SmartScreen');
    await expect(steps.nth(3)).toContainText('Allow');

    await page.locator('.source-note summary').click();
    await page.locator('.copy-button').click();
    await expect(page.locator('.copy-button')).toHaveText('Copied');
    const clipboard = await page.evaluate(() => navigator.clipboard.readText());
    expect(clipboard).toBe('git clone https://github.com/tochi-mba/Android_Headless_Mirror.git');
  });

  test('CLI section shows the agent contract and JSON mode', async ({ page }) => {
    const section = page.locator('#cli');
    await expect(section).toContainText('rex agent capabilities');
    await expect(section).toContainText('--json');
    await expect(section).toContainText('rex config restore');
  });

  test('states compatibility and privacy without absolute claims', async ({ page }) => {
    const body = await page.locator('body').textContent();
    expect(body).toContain('No telemetry');
    expect(body).toContain('designed for Android devices supported by ADB and scrcpy');
    expect(body).not.toMatch(/Any Android phone with USB debugging works/i);
    expect(body).not.toMatch(/Nothing leaves your PC/i);
  });

  test('FAQ disclosures open and expose their answers', async ({ page }) => {
    const items = page.locator('#faq details');
    await expect(items).toHaveCount(7);
    await items.nth(1).locator('summary').click();
    await expect(items.nth(1)).toHaveAttribute('open', '');
    await expect(items.nth(1).locator('p')).toBeVisible();
    await expect(items.nth(1)).toContainText(/never stored/i);
  });

  test('desktop layout has no horizontal overflow', async ({ page }) => {
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });

  test('reduced motion shows every reveal block immediately', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.reload();
    const hidden = await page.locator('.reveal').evaluateAll(nodes => nodes.filter(node => getComputedStyle(node).opacity !== '1').length);
    expect(hidden).toBe(0);
  });

  test('has no serious or critical accessibility violations', async ({ page }) => {
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
    const serious = results.violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
    expect(serious.map(v => `${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(', ')}`)).toEqual([]);
  });
});

test.describe('mobile behaviour', () => {
  test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

  test('menu opens, closes and reports its state', async ({ page }) => {
    await page.goto('/');
    const button = page.locator('.menu-button');
    const nav = page.locator('#site-nav');
    await expect(button).toBeVisible();
    await expect(nav).toBeHidden();

    await button.click();
    await expect(button).toHaveAttribute('aria-expanded', 'true');
    await expect(nav).toBeVisible();

    await nav.locator('a[href="#faq"]').click();
    await expect(button).toHaveAttribute('aria-expanded', 'false');
    await expect(nav).toBeHidden();

    await button.click();
    await page.keyboard.press('Escape');
    await expect(nav).toBeHidden();
  });

  test('mobile layout has no horizontal overflow', async ({ page }) => {
    await page.goto('/');
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });

  test('small phone width keeps the hero controls in view', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 640 });
    await page.goto('/');
    const box = await page.locator('.hero-actions .button-primary').boundingBox();
    expect(box).not.toBeNull();
    expect(box.x + box.width).toBeLessThanOrEqual(320);
  });
});

test('no-JavaScript fallback keeps content and navigation available', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto('/');
  await expect(page.locator('h1')).toBeVisible();
  await expect(page.locator('#install')).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('#site-nav a[href="#faq"]')).toBeVisible();
  await context.close();
});
