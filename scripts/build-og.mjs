// Renders docs/_og.html to docs/og.png, the picture link previews show.
//
// It uses the browser the site tests already install, so there is no new dependency and no
// hand-drawn asset to drift from the site it represents. Run it with "npm run build:og".
import { chromium } from '@playwright/test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const source = path.join(here, '..', 'docs', '_og.html');
const target = path.join(here, '..', 'docs', 'og.png');

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
await page.goto(pathToFileURL(source).href);
await page.waitForLoadState('networkidle');
await page.locator('.card').screenshot({ path: target });
await browser.close();
console.log('wrote ' + target);
