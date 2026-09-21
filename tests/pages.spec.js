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

test.describe('Android Headless Mirror GitHub Pages', () => {
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
        failedLocal.push(String(response.status()) + ' ' + url.pathname);
      }
    });

    await page.reload();
    await page.waitForLoadState('networkidle');

    expect(pageErrors).toEqual([]);
    expect(failedLocal).toEqual([]);
    await expect(page).toHaveTitle(/Android Headless Mirror.*REX Technologies/);
  });

  test('renders the REX Technologies product lockup', async ({ page }) => {
    await expect(page.locator('.brand-company').first()).toHaveText('REX Technologies');
    await expect(page.locator('.brand-name').first()).toHaveText('Android Headless Mirror');
    await expect(page.getByText('A REX Technologies product').first()).toBeVisible();
  });

  test('uses the canonical REX ink/signal palette at runtime', async ({ page }) => {
    const values = await page.evaluate(() => {
      const styles = getComputedStyle(document.documentElement);
      return {
        ink: styles.getPropertyValue('--ink').trim(),
        panel: styles.getPropertyValue('--panel').trim(),
        raised: styles.getPropertyValue('--raised').trim(),
        line: styles.getPropertyValue('--line').trim(),
        text: styles.getPropertyValue('--text').trim(),
        muted: styles.getPropertyValue('--muted').trim(),
        signal: styles.getPropertyValue('--signal').trim(),
        live: styles.getPropertyValue('--live').trim(),
      };
    });
    expect(values).toEqual(REX);
  });

  test('has complete SEO and product metadata', async ({ page }) => {
    await expect(page.locator('meta[name="description"]')).toHaveAttribute('content', /Android Headless Mirror/);
    await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute('content', REX.ink);
    await expect(page.locator('meta[property="og:title"]')).toHaveAttribute('content', /REX Technologies/);
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', 'https://tochi-mba.github.io/Android_Headless_Mirror/');
    await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', 'site.webmanifest');
  });

  test('all same-page navigation anchors resolve and scroll', async ({ page }) => {
    const hrefs = await page.locator('a[href^="#"]').evaluateAll(nodes => nodes.map(node => node.getAttribute('href')));
    for (const href of hrefs) {
      const id = href.slice(1);
      await expect(page.locator('#' + id)).toHaveCount(1);
    }

    await page.getByRole('link', { name: 'Features' }).click();
    await expect(page.locator('#features')).toBeInViewport();
  });

  test('diagnostic state tabs switch content and ARIA selection', async ({ page }) => {
    const output = page.locator('#diagnostic-output');

    await expect(output).toContainText('AUTHORIZED');

    const unauthorized = page.getByRole('tab', { name: 'Unauthorized' });
    await unauthorized.click();
    await expect(unauthorized).toHaveAttribute('aria-selected', 'true');
    await expect(output).toContainText('UNAUTHORIZED');
    await expect(output).toContainText('Allow USB debugging');

    const offline = page.getByRole('tab', { name: 'Offline' });
    await offline.click();
    await expect(offline).toHaveAttribute('aria-selected', 'true');
    await expect(output).toContainText('OFFLINE');
  });

  test('FAQ disclosures open and expose their answers', async ({ page }) => {
    const summary = page.getByText('Does STOP really keep it stopped?');
    await summary.click();
    await expect(page.getByText(/STOP creates a persistent state file/)).toBeVisible();
  });

  test('copy control copies the clone command and confirms success', async ({ page }) => {
    const copy = page.getByRole('button', { name: 'Copy' });
    await copy.click();
    await expect(copy).toHaveText('Copied');

    const clipboard = await page.evaluate(() => navigator.clipboard.readText());
    expect(clipboard).toBe('git clone https://github.com/tochi-mba/Android_Headless_Mirror.git');
  });

  test('desktop layout has no horizontal overflow', async ({ page }) => {
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
  });

  test('key hero controls remain inside the viewport', async ({ page }) => {
    const viewport = page.viewportSize();
    const button = page.getByRole('link', { name: /Get started/ });
    const box = await button.boundingBox();

    expect(box).not.toBeNull();
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(viewport.width + 1);
  });

  test('reduced motion disables the travelling connection animation', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.reload();

    const animationName = await page.locator('.connection-line span').evaluate(
      element => getComputedStyle(element).animationName
    );
    expect(animationName).toBe('none');
  });

  test('has no serious or critical WCAG 2.x axe violations', async ({ page }) => {
    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();

    const blocking = results.violations.filter(
      violation => violation.impact === 'serious' || violation.impact === 'critical'
    );
    expect(blocking).toEqual([]);
  });
});

test.describe('mobile behavior', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('mobile menu opens, closes, and updates aria-expanded', async ({ page }) => {
    await page.goto('/');
    const menu = page.getByRole('button', { name: 'Toggle navigation' });
    const nav = page.locator('#site-nav');

    await expect(menu).toBeVisible();
    await expect(menu).toHaveAttribute('aria-expanded', 'false');

    await menu.click();
    await expect(menu).toHaveAttribute('aria-expanded', 'true');
    await expect(nav).toHaveClass(/open/);

    await nav.getByRole('link', { name: 'Features' }).click();
    await expect(menu).toHaveAttribute('aria-expanded', 'false');
    await expect(nav).not.toHaveClass(/open/);
  });

  test('mobile layout has no horizontal overflow', async ({ page }) => {
    await page.goto('/');
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
  });

  test('small phone width remains usable', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 740 });
    await page.reload();

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
    await expect(page.getByRole('link', { name: /Get started/ })).toBeVisible();
  });
});

test('no-JavaScript fallback keeps primary content and navigation available', async ({ browser }) => {
  const context = await browser.newContext({
    javaScriptEnabled: false,
    viewport: { width: 390, height: 844 },
  });
  const page = await context.newPage();
  await page.goto('http://127.0.0.1:4173/');

  await expect(page.getByRole('heading', { name: /Your Android/ })).toBeVisible();
  await expect(page.locator('#site-nav')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Features' })).toBeVisible();
  await expect(page.locator('.reveal').first()).toBeVisible();

  await context.close();
});
