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

const SITE_PAGES = ['index.html', 'features.html', 'guide.html', 'shortcuts.html', 'settings.html', 'cli.html', 'help.html', 'changelog.html'];
const ALL_PAGES = [...SITE_PAGES, '404.html'];

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

  test('every header link leads to a page, and every in-page link to a section', async ({ page, request }) => {
    const pages = await page.locator('.site-nav a:not([target])').evaluateAll(links => links.map(link => link.getAttribute('href')));
    expect(pages).toEqual(SITE_PAGES.filter(name => name !== 'index.html'));
    for (const href of pages) {
      expect((await request.get('/' + href)).status(), href).toBe(200);
    }

    const anchors = await page.locator('.page-index a[href^="#"]').evaluateAll(links => links.map(link => link.getAttribute('href')));
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

    await nav.locator('a[href="help.html"]').click();
    await expect(page).toHaveURL(/help\.html$/);
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

test.describe('the rest of the site', () => {
  test('a mistyped address lands on a 404 that looks like the site', async ({ page }) => {
    await page.goto('/404.html');
    await expect(page.locator('h1')).toHaveText(/Nothing is mirrored here/);
    await expect(page.locator('.brand-name')).toHaveText('Android Headless Mirror');
    await expect(page.locator('.not-found .button-primary')).toHaveAttribute('href', './');
    await expect(page).toHaveTitle(/Page not found/);
    // A 404 that search engines index is a 404 that shows up in results.
    await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', /noindex/);
  });

  test('links preview with a picture and describe the app to search engines', async ({ page, request }) => {
    await page.goto('/');
    const image = await page.locator('meta[property="og:image"]').getAttribute('content');
    expect(image).toMatch(/og\.png$/);
    await expect(page.locator('meta[name="twitter:card"]')).toHaveAttribute('content', 'summary_large_image');

    const response = await request.get('/og.png');
    expect(response.status()).toBe(200);
    expect(Number(response.headers()['content-length'] ?? 1)).toBeGreaterThan(1000);

    const blocks = await page.locator('script[type="application/ld+json"]').allTextContents();
    const types = blocks.map(block => JSON.parse(block)['@type']);
    expect(types).toContain('SoftwareApplication');
    expect(types).toContain('FAQPage');
  });

  test('copying the clone command says so out loud', async ({ page, context, browserName }) => {
    test.skip(browserName !== 'chromium', 'clipboard permissions are only granted on Chromium');
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.goto('/');
    await page.locator('.source-note summary').click();
    await page.locator('.copy-button').click();
    await expect(page.locator('#copy-status')).toHaveText(/copied/i);
  });

  test('the navigation marks the section being read', async ({ page }) => {
    await page.goto('/');
    await page.locator('#faq').scrollIntoViewIfNeeded();
    await expect(page.locator('.page-index a[href="#faq"]')).toHaveAttribute('aria-current', 'true');
  });
});

test('closing the menu with Escape puts focus back on the button', async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  await page.goto('/');
  await page.locator('.menu-button').click();
  await expect(page.locator('.site-nav')).toHaveClass(/open/);
  await page.keyboard.press('Escape');
  await expect(page.locator('.site-nav')).not.toHaveClass(/open/);
  await expect(page.locator('.menu-button')).toBeFocused();
  await context.close();
});

test('no-JavaScript fallback keeps content and navigation available', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto('/');
  await expect(page.locator('h1')).toBeVisible();
  await expect(page.locator('#install')).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('#site-nav a[href="help.html"]')).toBeVisible();
  await context.close();
});

test.describe('every page of the site', () => {
  for (const name of ALL_PAGES) {
    test(`${name} loads without errors, has one header and passes axe`, async ({ page }) => {
      const pageErrors = [];
      const failedLocal = [];
      page.on('pageerror', error => pageErrors.push(error.message));
      page.on('response', response => {
        const url = new URL(response.url());
        if (url.origin === 'http://127.0.0.1:4173' && response.status() >= 400) failedLocal.push(`${response.status()} ${url.pathname}`);
      });
      await page.goto('/' + name);
      await page.waitForLoadState('networkidle');
      expect(pageErrors).toEqual([]);
      expect(failedLocal).toEqual([]);
      await expect(page).toHaveTitle(/Android Headless Mirror/);
      await expect(page.locator('h1')).toHaveCount(1);

      // The current page is marked in the header, and only the current page.
      const current = await page.locator('.site-nav a[aria-current="page"]').evaluateAll(links => links.map(link => link.getAttribute('href')));
      expect(current).toEqual(SITE_PAGES.includes(name) && name !== 'index.html' ? [name] : []);

      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
      const serious = results.violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
      expect(serious.map(v => `${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(', ')}`)).toEqual([]);
    });

    test(`${name} never scrolls sideways at 320, 390 or 1280 pixels`, async ({ page }) => {
      for (const width of [320, 390, 1280]) {
        await page.setViewportSize({ width, height: 800 });
        await page.goto('/' + name);
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow, `${name} at ${width}px`).toBeLessThanOrEqual(0);
      }
    });
  }

  test('pictures say what they show, have a size, and wait until they are needed', async ({ page }) => {
    for (const name of ALL_PAGES) {
      await page.goto('/' + name);
      const images = await page.locator('main img').evaluateAll(nodes => nodes.map(img => ({
        alt: img.getAttribute('alt'), width: img.getAttribute('width'), height: img.getAttribute('height'), loading: img.getAttribute('loading'),
      })));
      images.forEach((image, i) => {
        expect(image.alt, name).not.toBeNull();
        expect(image.width && image.height, name).toBeTruthy();
        if (i > 0) expect(image.loading, name).toBe('lazy');
      });
    }
  });

  test('the checker that runs before publishing finds nothing wrong', async () => {
    const { checkSite } = await import('../scripts/check-site.mjs');
    expect(checkSite(require('node:path').join(__dirname, '..', 'docs'))).toEqual([]);
  });
});

test.describe('the reference pages', () => {
  test('the settings filter narrows the rows, counts them, and Escape clears it', async ({ page }) => {
    await page.goto('/settings.html');
    const rows = page.locator('.ref-row');
    const total = await rows.count();
    expect(total).toBeGreaterThan(100);
    const filter = page.locator('#settings-filter');

    await filter.fill('Mirror.MaxFps');
    await expect(page.locator('#settings-filter-count')).toHaveText('1 setting matches');
    await expect(page.locator('[id="Mirror.MaxFps"]')).toBeVisible();
    await filter.fill('maxfps');
    await expect(page.locator('#settings-filter-count')).toHaveText('1 setting matches');
    await filter.fill('copies');
    await expect(page.locator('#settings-filter-count')).toHaveText(/settings match$/);
    await expect(page.locator('#group-display')).toBeHidden();
    await filter.fill('nothing at all mentions this');
    await expect(page.locator('#settings-filter-count')).toHaveText('No setting mentions that.');

    await filter.press('Escape');
    await expect(filter).toHaveValue('');
    await expect(page.locator('.ref-row:visible')).toHaveCount(total);
  });

  test('a link to one setting lands on it', async ({ page }) => {
    await page.goto('/settings.html#Session.TurnScreenOff');
    await expect(page.locator('[id="Session.TurnScreenOff"] h3')).toHaveText('Phone screen off while mirroring');
    await expect(page.locator('[id="Session.TurnScreenOff"]')).toBeInViewport();
  });

  test('the shortcuts filter finds a key by what it does', async ({ page }) => {
    await page.goto('/shortcuts.html');
    await page.locator('#shortcuts-filter').fill('screenshot');
    await expect(page.locator('.key-row:visible')).toHaveCount(1);
    await expect(page.locator('.key-row:visible kbd')).toHaveText('Ctrl+Alt+S');
  });

  test('every command can be copied', async ({ page, context, browserName }) => {
    test.skip(browserName !== 'chromium', 'clipboard permissions are only granted on Chromium');
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.goto('/cli.html');
    const row = page.locator('#cmd-config-list-get-set-restore-path-value');
    await row.locator('.copy-button').click();
    await expect(page.locator('#copy-status')).toHaveText(/copied/i);
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('rex config set Mirror.MaxFps 90');
  });

  test('the changelog lists versions newest first, and the download stays the stable asset', async ({ page }) => {
    await page.goto('/changelog.html');
    const versions = await page.locator('.changelog h2').evaluateAll(nodes => nodes.map(node => node.id));
    expect(versions.length).toBeGreaterThan(10);
    const numbers = versions.map(id => id.slice(1).split('-').map(Number));
    for (let i = 1; i < numbers.length; i++) {
      const [a, b] = [numbers[i - 1], numbers[i]];
      expect(a[0] * 1e6 + a[1] * 1e3 + a[2]).toBeGreaterThan(b[0] * 1e6 + b[1] * 1e3 + b[2]);
    }

    // Without GitHub's answer, the static link is the stable asset name (the page decorates it when GitHub answers).
    await page.route('https://api.github.com/**', route => route.abort());
    await page.goto('/');
    await expect(page.locator('#download')).toHaveAttribute('href', 'https://github.com/tochi-mba/Android_Headless_Mirror/releases/latest/download/AndroidHeadlessMirror-Setup.exe');
  });

  test('without JavaScript every setting, every answer and every page is reachable', async ({ browser }) => {
    const context = await browser.newContext({ javaScriptEnabled: false });
    const page = await context.newPage();
    await page.goto('/settings.html');
    await expect(page.locator('.filter')).toBeHidden();
    const rows = page.locator('.ref-row');
    expect(await rows.count()).toBeGreaterThan(100);
    await expect(rows.last()).toBeAttached();
    expect(await page.locator('.ref-row[hidden]').count()).toBe(0);
    await page.goto('/');
    await page.locator('#faq details').first().locator('summary').click();
    await expect(page.locator('#faq details').first().locator('p')).toBeVisible();
    await context.close();
  });
});
