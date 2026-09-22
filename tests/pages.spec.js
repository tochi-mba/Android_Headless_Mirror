const { test, expect } = require('@playwright/test');
const AxeBuilder = require('@axe-core/playwright').default;
const fs = require('fs');
const path = require('path');
const { PNG } = require('pngjs');

const visualThresholds = JSON.parse(
  fs.readFileSync(path.join(__dirname, 'visual-thresholds.json'), 'utf8')
);

function analyzePng(buffer) {
  const png = PNG.sync.read(buffer);
  let total = 0;
  let dark = 0;
  let signal = 0;
  let text = 0;
  let orange = 0;
  const colors = new Set();

  for (let y = 0; y < png.height; y += 2) {
    for (let x = 0; x < png.width; x += 2) {
      const i = (png.width * y + x) << 2;
      const r = png.data[i];
      const g = png.data[i + 1];
      const b = png.data[i + 2];
      const a = png.data[i + 3];

      if (a < 200) continue;
      total += 1;
      colors.add((r << 16) | (g << 8) | b);

      if (r <= 45 && g <= 55 && b <= 48) dark += 1;
      if (Math.abs(r - 215) <= 35 && Math.abs(g - 255) <= 25 && Math.abs(b - 63) <= 45) signal += 1;
      if (r >= 210 && g >= 210 && b >= 205) text += 1;
      if (Math.abs(r - 255) <= 25 && Math.abs(g - 119) <= 35 && Math.abs(b - 77) <= 35) orange += 1;
    }
  }

  return {
    width: png.width,
    height: png.height,
    total,
    darkRatio: total ? dark / total : 0,
    signalPixels: signal,
    textPixels: text,
    orangePixels: orange,
    uniqueColors: colors.size,
  };
}

async function assertVisualThreshold(target, name) {
  const dir = path.join(process.cwd(), 'test-results', 'visual');
  fs.mkdirSync(dir, { recursive: true });
  const screenshotPath = path.join(dir, name + '.png');

  const buffer = await target.screenshot({ path: screenshotPath });
  const metrics = analyzePng(buffer);
  const threshold = visualThresholds.pages;

  expect(metrics.darkRatio, name + ' dark ratio').toBeGreaterThanOrEqual(threshold.darkRatioMin);
  expect(metrics.darkRatio, name + ' dark ratio').toBeLessThanOrEqual(threshold.darkRatioMax);
  expect(metrics.signalPixels, name + ' signal pixels').toBeGreaterThanOrEqual(threshold.signalPixelsMin);
  expect(metrics.textPixels, name + ' text pixels').toBeGreaterThanOrEqual(threshold.textPixelsMin);
  expect(metrics.uniqueColors, name + ' unique colors').toBeGreaterThanOrEqual(threshold.uniqueColorsMin);

  fs.writeFileSync(
    path.join(dir, name + '.metrics.json'),
    JSON.stringify(metrics, null, 2)
  );

  return metrics;
}

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

  test('Quick Start opens GitHub in a separate tab safely', async ({ page }) => {
    const quickStart = page.getByRole('link', { name: /Read quick start/ });
    await expect(quickStart).toHaveAttribute('target', '_blank');
    await expect(quickStart).toHaveAttribute('rel', /noopener/);
    await expect(quickStart).toHaveAttribute('rel', /noreferrer/);
    await expect(quickStart).toHaveAttribute('href', 'https://github.com/tochi-mba/Android_Headless_Mirror#quick-start');
  });

  test('View source opens GitHub in a separate tab safely', async ({ page }) => {
    const viewSource = page.getByRole('link', { name: /View source/ });
    await expect(viewSource).toHaveAttribute('target', '_blank');
    await expect(viewSource).toHaveAttribute('rel', /noopener/);
    await expect(viewSource).toHaveAttribute('rel', /noreferrer/);
  });

  test('all same-page navigation anchors resolve and scroll', async ({ page }) => {
    const hrefs = await page.locator('a[href^="#"]').evaluateAll(nodes => nodes.map(node => node.getAttribute('href')));
    for (const href of hrefs) {
      const id = href.slice(1);
      await expect(page.locator('#' + id)).toHaveCount(1);
    }

    const menu = page.getByRole('button', { name: 'Toggle navigation' });
    if (await menu.isVisible()) {
      await menu.click();
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

    await offline.press('Home');
    const authorized = page.getByRole('tab', { name: 'Authorized', exact: true });
    await expect(authorized).toBeFocused();
    await expect(authorized).toHaveAttribute('aria-selected', 'true');

    await authorized.press('ArrowRight');
    await expect(unauthorized).toBeFocused();
    await expect(unauthorized).toHaveAttribute('aria-selected', 'true');
  });

  test('headless FAQ states the secure-lock boundary', async ({ page }) => {
    const summary = page.getByText('Can it unlock the phone for me?');
    await summary.click();
    await expect(page.getByText(/does not bypass it/)).toBeVisible();
    await expect(page.getByText(/authenticate from the PC/)).toBeVisible();
  });

  test('pattern guide documents the geometry accuracy hierarchy and calibration fallback', async ({ page }) => {
    const section = page.locator('#pattern-guide');
    await expect(section).toBeVisible();
    await expect(section).toContainText('Exact pattern-cell bounds');
    await expect(section).toContainText('Runtime LockPatternView bounds');
    await expect(section).toContainText('Saved per-device calibration');
    await expect(section).toContainText('Estimated fallback');
    await expect(section).toContainText('Ctrl+Alt+C');
    await expect(section).toContainText('Shift+arrows');
    await expect(section).toContainText('Ctrl+arrows');
  });

  test('pattern guide privacy distinguishes geometry from the unlock credential', async ({ page }) => {
    const section = page.locator('#pattern-guide');
    await expect(section).toContainText('The unlock path is never stored');
    await expect(section).toContainText('four normalized bounds');
    await expect(section).toContainText('geometry, not the credential');
  });

  test('display section explains independent control and transport verification', async ({ page }) => {
    const section = page.locator('#display');
    await expect(section).toBeVisible();
    await expect(section).toContainText('Control stays on ADB');
    await expect(section).toContainText('scrcpy by default');
    await expect(section).toContainText('Verify protected playback');
  });

  test('privileged section presents capability state, explicit authorization, toolkit, and safety boundary', async ({ page }) => {
    const section = page.locator('#privileged');
    await expect(section).toBeVisible();
    await expect(section).toContainText('Root is a capability. Not a boolean.');
    await expect(section).toContainText('rex root status');
    await expect(section).toContainText('never invokes');
    await expect(section).toContainText('rex root request');
    await expect(section).toContainText('VERIFIED THIS BOOT');
    await expect(section).toContainText('granted');
    await expect(section).toContainText('Magisk');
    await expect(section).toContainText('kernel logs');
    await expect(section).toContainText('RESTRICTED');
    await expect(section).toContainText('READ-ONLY TOOLKIT');
    await expect(section).toContainText('no root installation');
    await expect(section).toContainText('arbitrary root shell');
    await expect(section).toContainText('DRM bypass');
    await expect(section.getByRole('link', { name: /Architecture/ })).toHaveAttribute('href', /ROOT_ARCHITECTURE\.md/);
    await expect(section.getByRole('link', { name: /Security model/ })).toHaveAttribute('href', /ROOT_SECURITY\.md/);
    await expect(section.getByRole('link', { name: /Compatibility ledger/ })).toHaveAttribute('href', /ROOT_COMPATIBILITY\.md/);
  });

  test('root FAQ explains existing-root-only, explicit authorization, and protected-video boundaries', async ({ page }) => {
    const install = page.getByText('Does REX root my phone?');
    await install.click();
    await expect(page.getByText(/Root v1 only uses an existing root environment/)).toBeVisible();
    await expect(page.getByText(/does not unlock the bootloader/)).toBeVisible();

    const auth = page.getByText('Will REX ask for root access automatically?');
    await auth.click();
    await expect(page.getByText(/only happens when you explicitly run/)).toBeVisible();

    const drm = page.getByText('Does root let REX capture Netflix or other protected video?');
    await drm.click();
    await expect(page.getByText(/does not bypass DRM/)).toBeVisible();
    await expect(page.getByText(/protected display support are separate/)).toBeVisible();
  });
  test('controls separate native Android pinch from PC-only host zoom', async ({ page }) => {
    const section = page.locator('#controls');
    await expect(section).toBeVisible();
    await expect(section).toContainText('Pinch naturally → Android pinches.');
    await expect(section).toContainText('No keyboard modifier is required');
    await expect(section).toContainText('Hold Alt + pinch → magnify the mirror.');
    await expect(section).toContainText('Alt + two-finger slide pans the zoomed viewport');
    await expect(section).toContainText('Alt + wheel controls host zoom');
    await expect(section).toContainText('The phone receives no pinch');
    await expect(section).toContainText('Reset zoom');
    await expect(section).toContainText('Windows 11');
    await expect(section).toContainText('Precision Touchpad');
  });

  test('controls expose the always-available sleep action', async ({ page }) => {
    const section = page.locator('#controls');
    await expect(section.locator('.toolbar-demo-button', { hasText: 'Sleep phone' })).toBeVisible();
    await expect(section).toContainText('re-sends scrcpy’s screen-off command');
    await expect(section).toContainText('PC mirror continues');
  });

  test('Control Center documents the PC/device/advanced separation', async ({ page }) => {
    const section = page.locator('#control-center');
    await expect(section).toBeVisible();
    await expect(section).toContainText('Privileged');
    await expect(section).toContainText('PC / mirror settings');
    await expect(section).toContainText('Samsung Galaxy S21 Ultra settings');
    await expect(section).toContainText('Advanced Android');
    await expect(section).toContainText('Diagnostics');
    await expect(section).toContainText('Runtime Settings Provider browser');
    await expect(section).toContainText('system');
    await expect(section).toContainText('secure');
    await expect(section).toContainText('global');
    await expect(section).toContainText('adb_enabled');
    await expect(section).toContainText('protected');
  });


  test('REX CLI documents wizard, parity, rollback, and power-user commands', async ({ page }) => {
    const section = page.locator('#cli');
    await expect(section).toBeVisible();
    await expect(section).toContainText('One terminal app');
    await expect(section).toContainText('Detect before asking');
    await expect(section).toContainText('Everything from the keyboard');
    await expect(section).toContainText('The CLI and Control Center cannot drift');
    await expect(section).toContainText('rex smart');
    await expect(section).toContainText('rex agent status');
    await expect(section).toContainText('rex status --json');
    await expect(section).toContainText('REX.lnk');
    await expect(section).toContainText('rex action sleep');
    await expect(section).toContainText('rex mirror zoom-in');
    await expect(section).toContainText('rex root status');
    await expect(section).toContainText('rex android list global');
    await expect(section).toContainText('rex config restore');
    await expect(section).toContainText('SHA-256');
  });

  test('FAQ separates Android pinch from Alt host-only zoom', async ({ page }) => {
    const summary = page.getByText('Can I pinch TikTok with my laptop touchpad like I would on the phone?');
    await summary.click();
    await expect(page.getByText(/pinch or spread with two fingers/)).toBeVisible();
    await expect(page.getByText(/No modifier is required/)).toBeVisible();

    const hostZoom = page.getByText('How do I zoom only the PC mirror without zooming the Android app?');
    await hostZoom.click();
    await expect(page.getByText(/Hold Alt/)).toBeVisible();
    await expect(page.getByText(/Alt \+ mouse wheel/)).toBeVisible();
    await expect(page.getByText(/Alt \+ two-finger slide pans/)).toBeVisible();
  });

  test('FAQ covers no-lock devices and lock-type changes', async ({ page }) => {
    await page.getByText('What if my phone has no password or screen lock?').click();
    await expect(page.getByText(/no pattern-overlay process is started/)).toBeVisible();

    await page.getByText('What if I change my phone from pattern to PIN, or remove the lock?').click();
    await expect(page.getByText(/RESET_LOCK_SCREEN_CHOICES\.bat/)).toBeVisible();
  });

  test('FAQ documents PC-only pattern calibration', async ({ page }) => {
    const summary = page.getByText('What if the pattern dots are not perfectly aligned on my phone?');
    const details = summary.locator('..');
    await summary.click();
    await expect(details.getByText(/Ctrl\+Alt\+C/)).toBeVisible();
    await expect(details.getByText(/no physical phone interaction is required/)).toBeVisible();
  });

  test('FAQ disclosures open and expose their answers', async ({ page }) => {
    const summary = page.getByText('Does STOP really keep it stopped?');
    await summary.click();
    await expect(page.getByText(/STOP creates a persistent state file/)).toBeVisible();
  });

  test('copy control copies the clone command and confirms success', async ({ page }) => {
    const copy = page.locator('.copy-button');
    await expect(copy).toHaveText('Copy');
    await copy.click();
    await expect(copy).toHaveText('Copied');

    const clipboard = await page.evaluate(() => navigator.clipboard.readText());
    expect(clipboard).toBe('git clone https://github.com/tochi-mba/Android_Headless_Mirror.git');
  });

  test('trust strip rows align at the reviewed two-column desktop width', async ({ page }) => {
    await page.setViewportSize({ width: 887, height: 900 });
    await page.reload();

    const boxes = await page.locator('.trust-grid > div').evaluateAll(nodes =>
      nodes.map(node => {
        const rect = node.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
      })
    );

    expect(boxes).toHaveLength(4);
    expect(Math.abs(boxes[0].x - boxes[2].x)).toBeLessThanOrEqual(1);
    expect(Math.abs(boxes[1].x - boxes[3].x)).toBeLessThanOrEqual(1);
    expect(Math.abs(boxes[0].width - boxes[2].width)).toBeLessThanOrEqual(1);
    expect(Math.abs(boxes[1].width - boxes[3].width)).toBeLessThanOrEqual(1);
  });

  test('reviewed USB and wireless icons use proper inline SVGs', async ({ page }) => {
    await expect(page.locator('#how .step').first().locator('.step-icon svg')).toHaveCount(1);
    const wireless = page.getByRole('heading', { name: 'Wireless, when you want it' }).locator('..');
    await expect(wireless.locator('.feature-symbol svg')).toHaveCount(1);
    await expect(page.locator('body')).not.toContainText('⌁');
  });

  test('lifecycle card contains useful state transitions instead of decorative track', async ({ page }) => {
    const lifecycle = page.locator('.feature-large');
    await expect(lifecycle.locator('.lifecycle-contract')).toBeVisible();
    await expect(lifecycle).toContainText('STOP.bat');
    await expect(lifecycle).toContainText('stop.flag created');
    await expect(lifecycle).toContainText('autostart blocked');
    await expect(lifecycle).toContainText('START_NOW.bat');
    await expect(lifecycle.locator('.state-demo')).toHaveCount(0);
  });

  test('tested hardware uses a content-sized grid card with centered icon', async ({ page }) => {
    await page.setViewportSize({ width: 887, height: 900 });
    await page.reload();

    const grid = page.locator('.tested-devices-grid');
    const card = page.locator('.tested-device').first();
    const icon = card.locator('.tested-device-icon');

    const [gridBox, cardBox, iconBox] = await Promise.all([
      grid.boundingBox(),
      card.boundingBox(),
      icon.boundingBox(),
    ]);

    expect(gridBox).not.toBeNull();
    expect(cardBox).not.toBeNull();
    expect(iconBox).not.toBeNull();
    expect(cardBox.width).toBeLessThan(gridBox.width * 0.7);

    const cardCenter = cardBox.x + cardBox.width / 2;
    const iconCenter = iconBox.x + iconBox.width / 2;
    expect(Math.abs(cardCenter - iconCenter)).toBeLessThanOrEqual(2);
  });

  test('hero preview is a flat product session view, not the old decorative collage', async ({ page }) => {
    await expect(page.locator('.session-preview')).toBeVisible();
    await expect(page.locator('.session-details')).toContainText('CURRENT SESSION');
    await expect(page.locator('.terminal-card')).toHaveCount(0);
    await expect(page.locator('.connection-line')).toHaveCount(0);
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

  test('reduced motion removes reveal transforms', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.reload();

    const transform = await page.locator('.reveal').first().evaluate(
      element => getComputedStyle(element).transform
    );
    expect(transform).toBe('none');
  });

  test('desktop hero screenshot passes visual thresholds', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.reload();
    await assertVisualThreshold(page, 'pages-desktop-hero');
  });

  test('desktop privileged section screenshot passes visual thresholds', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1100 });
    await page.reload();
    const section = page.locator('#privileged');
    await section.scrollIntoViewIfNeeded();
    await assertVisualThreshold(section, 'pages-desktop-privileged');
  });

  test('desktop controls section screenshot passes visual thresholds', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.reload();
    const section = page.locator('#controls');
    await section.scrollIntoViewIfNeeded();
    await assertVisualThreshold(section, 'pages-desktop-controls');
  });

  test('desktop pattern guide screenshot passes visual thresholds', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.reload();
    const section = page.locator('#pattern-guide');
    await section.scrollIntoViewIfNeeded();
    await assertVisualThreshold(section, 'pages-desktop-pattern-guide');
  });

  test('desktop Control Center screenshot passes visual thresholds', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.reload();
    const section = page.locator('#control-center');
    await section.scrollIntoViewIfNeeded();
    await assertVisualThreshold(section, 'pages-desktop-control-center');
  });

  test('desktop REX CLI screenshot passes visual thresholds', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.reload();
    const section = page.locator('#cli');
    await section.scrollIntoViewIfNeeded();
    await assertVisualThreshold(section, 'pages-desktop-rex-cli');
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

    await page.keyboard.press('Escape');
    await expect(menu).toHaveAttribute('aria-expanded', 'false');
    await expect(nav).not.toHaveClass(/open/);

    await menu.click();
    await nav.getByRole('link', { name: 'Features' }).click();
    await expect(menu).toHaveAttribute('aria-expanded', 'false');
    await expect(nav).not.toHaveClass(/open/);
  });

  test('mobile layout has no horizontal overflow', async ({ page }) => {
    await page.goto('/');
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
  });

  test('mobile privileged section screenshot passes visual thresholds', async ({ page }) => {
    await page.goto('/');
    const section = page.locator('#privileged');
    await section.scrollIntoViewIfNeeded();
    await assertVisualThreshold(section, 'pages-mobile-privileged');
  });

  test('mobile controls screenshot passes visual thresholds', async ({ page }) => {
    await page.goto('/');
    const section = page.locator('#controls');
    await section.scrollIntoViewIfNeeded();
    await assertVisualThreshold(section, 'pages-mobile-controls');
  });

  test('mobile Control Center screenshot passes visual thresholds', async ({ page }) => {
    await page.goto('/');
    const section = page.locator('#control-center');
    await section.scrollIntoViewIfNeeded();
    await assertVisualThreshold(section, 'pages-mobile-control-center');
  });

  test('mobile REX CLI screenshot passes visual thresholds', async ({ page }) => {
    await page.goto('/');
    const section = page.locator('#cli');
    await section.scrollIntoViewIfNeeded();
    await assertVisualThreshold(section, 'pages-mobile-rex-cli');
  });

  test('small phone width remains usable', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 740 });
    await page.goto('/');

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
