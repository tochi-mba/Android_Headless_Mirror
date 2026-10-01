// Checks the GitHub Pages site in docs/ before it is published.
//
// A static site has no compiler, so nothing else catches a broken link, a missing picture, a page
// left out of the sitemap or draft words that escaped. The Pages workflow runs this before it
// uploads anything, and the Playwright suite runs the same function. Standard library only.
//
//   node scripts/check-site.mjs            the site in this repository
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const SITE = 'https://tochi-mba.github.io/Android_Headless_Mirror/';
const PRODUCT = 'Android Headless Mirror';
const OWNER = 'tochi-mba';
// GitHub owners the site may link to besides its own: the project it is built on.
const OTHER_GITHUB = ['Genymobile/scrcpy'];
const MAX_IMAGE_BYTES = 400 * 1024;
const DRAFT = [/\bTODO\b/, /\bFIXME\b/, /\bTBD\b/, /\bLorem ipsum\b/i, /\bXXX\b/, /\bcoming soon\b/i];
const REMOTE = /^(https?:|mailto:|data:|\/\/)/;

const all = (text, pattern) => [...text.matchAll(pattern)].map(match => match[1]);

/** The page with scripts and styles removed, so their contents are not read as text. */
const markup = html => html.replace(/<script[\s\S]*?<\/script>/g, '').replace(/<style[\s\S]*?<\/style>/g, '');

const block = (html, tag) => {
  const match = html.match(new RegExp(`<${tag}[\\s>][\\s\\S]*?</${tag}>`));
  return match ? match[0].replace(/ aria-current="page"/g, '') : null;
};

/** Every problem with the site in this folder, as "file: problem" lines; empty when it is fine. */
export function checkSite(dir) {
  const problems = [];
  const pages = readdirSync(dir).filter(name => name.endsWith('.html') && !name.startsWith('_')).sort();
  const sources = new Map(pages.map(name => [name, readFileSync(path.join(dir, name), 'utf8')]));
  const ids = new Map([...sources].map(([name, html]) => [name, new Set(all(html, /\sid="([^"]+)"/g))]));
  const home = sources.get('index.html');
  if (!home) {
    return ['index.html: missing'];
  }

  const header = block(home, 'header');
  const footer = block(home, 'footer');

  for (const [name, html] of sources) {
    const report = text => problems.push(`${name}: ${text}`);
    const body = markup(html);

    const title = html.match(/<title>([^<]*)<\/title>/)?.[1] ?? '';
    if (!title.includes(PRODUCT)) report(`the title "${title}" does not name ${PRODUCT}`);
    const headings = (body.match(/<h1[\s>]/g) ?? []).length;
    if (headings !== 1) report(`has ${headings} h1 headings instead of one`);
    if (!/<meta name="description" content="[^"]+">/.test(html)) report('has no description');
    if (!/<html lang="[a-z-]+"/.test(html)) report('has no language');
    if (name !== '404.html' && !html.includes(`<link rel="canonical" href="${SITE}`)) report('has no canonical address');
    if (block(html, 'header') !== header) report('its header differs from the home page');
    if (block(html, 'footer') !== footer) report('its footer differs from the home page');

    const seen = new Set();
    for (const id of all(html, /\sid="([^"]+)"/g)) {
      if (seen.has(id)) report(`the id "${id}" is used twice`);
      seen.add(id);
    }

    for (const link of all(body, /\shref="([^"]+)"/g)) {
      if (link.startsWith('http://')) report(`links to an unencrypted address: ${link}`);
      const github = link.match(/^https:\/\/github\.com\/([^/]+)(?:\/([^/#?]+))?/);
      if (github && github[1] !== OWNER && !OTHER_GITHUB.includes(`${github[1]}/${github[2]}`)) {
        report(`links to another owner's GitHub: ${link}`);
      }

      if (REMOTE.test(link)) continue;
      const [file, anchor] = link.split('#');
      const target = file === '' ? name : file === './' ? 'index.html' : file.split('?')[0];
      if (file !== '' && !existsSync(path.join(dir, target))) {
        report(`links to ${link}, which does not exist`);
      } else if (anchor && !ids.get(target)?.has(anchor)) {
        report(`links to ${link}, but ${target} has no element with that id`);
      }
    }

    for (const asset of all(html, /\ssrc="([^"]+)"/g)) {
      if (asset.startsWith('http://')) report(`loads an unencrypted address: ${asset}`);
      if (!REMOTE.test(asset) && !existsSync(path.join(dir, asset.split('?')[0]))) report(`loads ${asset}, which does not exist`);
    }

    for (const image of body.match(/<img\b[^>]*>/g) ?? []) {
      const src = image.match(/\ssrc="([^"]+)"/)?.[1] ?? '(no src)';
      if (!/\salt="/.test(image)) report(`the picture ${src} has no alt text`);
      if (!/\swidth="\d+"/.test(image) || !/\sheight="\d+"/.test(image)) report(`the picture ${src} has no width and height`);
      const file = path.join(dir, src);
      if (!REMOTE.test(src) && existsSync(file) && statSync(file).size > MAX_IMAGE_BYTES) report(`the picture ${src} is over 400 KB`);
    }

    for (const tag of body.match(/<a\b[^>]*target="_blank"[^>]*>/g) ?? []) {
      if (!/rel="noopener noreferrer"/.test(tag)) report(`a link opening a new tab has no rel="noopener noreferrer": ${tag}`);
    }

    if (/<[^>]+\son[a-z]+="/.test(body)) report('has an inline event handler');

    const text = body.replace(/<[^>]+>/g, ' ');
    for (const pattern of DRAFT) {
      if (pattern.test(text)) report(`contains draft words matching ${pattern}`);
    }
  }

  const sitemap = readFileSync(path.join(dir, 'sitemap.xml'), 'utf8');
  const listed = new Set(all(sitemap, /<loc>([^<]+)<\/loc>/g).map(url => url === SITE ? 'index.html' : url.replace(SITE, '')));
  for (const name of pages.filter(page => page !== '404.html')) {
    if (!listed.has(name)) problems.push(`sitemap.xml: ${name} is missing`);
  }

  for (const name of listed) {
    if (!sources.has(name)) problems.push(`sitemap.xml: lists ${name}, which does not exist`);
  }

  return problems;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const dir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'docs');
  const problems = checkSite(dir);
  for (const problem of problems) console.error(problem);
  console.log(problems.length === 0 ? 'The site is ready to publish.' : `${problems.length} problem(s) found.`);
  process.exit(problems.length === 0 ? 0 : 1);
}
