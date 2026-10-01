const menuButton = document.querySelector('.menu-button');
const nav = document.querySelector('.site-nav');

const setMenu = (open, returnFocus = false) => {
  nav?.classList.toggle('open', open);
  menuButton?.setAttribute('aria-expanded', String(open));
  // Closing with the keyboard has to put focus somewhere sensible, or it falls back to the page.
  if (!open && returnFocus) menuButton?.focus();
};
menuButton?.addEventListener('click', () => setMenu(menuButton.getAttribute('aria-expanded') !== 'true'));
nav?.querySelectorAll('a').forEach(link => link.addEventListener('click', () => setMenu(false)));
document.addEventListener('keydown', event => {
  if (event.key === 'Escape') setMenu(false, true);
});

// While the menu is open it is the whole page as far as the keyboard is concerned: tabbing out of
// the bottom of a menu that covers everything behind it just loses people.
document.addEventListener('focusin', event => {
  if (menuButton?.getAttribute('aria-expanded') !== 'true') return;
  if (nav?.contains(event.target) || event.target === menuButton) return;
  nav?.querySelector('a')?.focus();
});

const copyText = async text => {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
};
const copyStatus = document.getElementById('copy-status');
document.querySelectorAll('.copy-button').forEach(button => button.addEventListener('click', async () => {
  const label = button.textContent;
  const done = await copyText(button.dataset.copy);
  button.textContent = done ? 'Copied' : 'Copy failed';
  // The button changing its own text says nothing to a screen reader; this does.
  if (copyStatus) copyStatus.textContent = done ? 'Command copied to the clipboard' : 'Could not copy the command';
  setTimeout(() => { button.textContent = label; }, 1400);
}));

const meta = document.getElementById('download-meta');
if (meta && 'fetch' in window) {
  fetch('https://api.github.com/repos/tochi-mba/Android_Headless_Mirror/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
    .then(response => (response.ok ? response.json() : null))
    .then(release => {
      // The static link already points at the stable asset name; the API only adds the version
      // and size, and covers older releases that used a versioned file name.
      const asset = release?.assets?.find(a => /^AndroidHeadlessMirror-Setup.*\.exe$/.test(a.name));
      if (asset) {
        document.querySelectorAll('[data-latest-installer]').forEach(link => {
          link.href = asset.browser_download_url;
          link.setAttribute('download', asset.name);
        });
        meta.textContent = `${release.tag_name} · ${Math.round(asset.size / 1048576)} MB · Windows 10 or 11 · 64-bit`;
      }
    })
    .catch(() => {});
}

const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
if (!reduced && 'IntersectionObserver' in window) {
  document.documentElement.classList.add('motion-ready');
  const observer = new IntersectionObserver(entries => entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
      observer.unobserve(entry.target);
    }
  }), { threshold: 0.12 });
  document.querySelectorAll('.reveal').forEach(el => observer.observe(el));
}

// Mark the section being read in the page's own index, so long scrolls keep their place.
const indexLinks = new Map([...document.querySelectorAll('.page-index a[href^="#"], .doc-contents a[href^="#"]')]
  .map(a => [a.getAttribute('href').slice(1), a]));
const indexed = [...indexLinks.keys()].map(id => document.getElementById(id)).filter(Boolean);
if (indexed.length && 'IntersectionObserver' in window) {
  const spy = new IntersectionObserver(entries => {
    entries.filter(entry => entry.isIntersecting).forEach(entry => {
      indexLinks.forEach(link => link.removeAttribute('aria-current'));
      indexLinks.get(entry.target.id)?.setAttribute('aria-current', 'true');
    });
  }, { rootMargin: '-45% 0px -50% 0px' });
  indexed.forEach(section => spy.observe(section));
}

// The filter on the settings and shortcuts pages. Without JavaScript every row simply shows.
const squash = text => text.toLowerCase().replace(/[^a-z0-9]/g, '');
document.querySelectorAll('.filter-input').forEach(input => {
  const rows = [...document.querySelectorAll(input.dataset.filter)];
  const groups = [...document.querySelectorAll(input.dataset.filterGroups)];
  const count = document.getElementById(input.dataset.filterCount);
  const noun = input.dataset.filterNoun;
  // A row matches by its words, or by its name in config.json typed any way (maxfps, Mirror.MaxFps).
  const words = rows.map(row => [row.textContent.toLowerCase(), squash(row.textContent + ' ' + row.id)]);
  const apply = () => {
    const query = input.value.trim().toLowerCase();
    const squashed = squash(query);
    let shown = 0;
    rows.forEach((row, i) => {
      const hit = !query || words[i][0].includes(query) || (squashed.length > 0 && words[i][1].includes(squashed));
      row.hidden = !hit;
      if (hit) shown++;
    });
    groups.forEach(group => {
      group.hidden = query.length > 0 && !rows.some(row => !row.hidden && group.contains(row));
    });
    if (count) {
      count.textContent = !query ? '' : shown === 0 ? `No ${noun} mentions that.` : shown === 1 ? `1 ${noun} matches` : `${shown} ${noun}s match`;
    }
  };
  input.addEventListener('input', apply);
  input.addEventListener('keydown', event => {
    if (event.key === 'Escape' && input.value) {
      input.value = '';
      apply();
      event.stopPropagation();
    }
  });
});

const year = document.getElementById('year');
if (year) year.textContent = String(new Date().getFullYear());
