const menuButton = document.querySelector('.menu-button');
const nav = document.querySelector('.site-nav');

const setMenu = open => {
  nav?.classList.toggle('open', open);
  menuButton?.setAttribute('aria-expanded', String(open));
};
menuButton?.addEventListener('click', () => setMenu(menuButton.getAttribute('aria-expanded') !== 'true'));
nav?.querySelectorAll('a').forEach(link => link.addEventListener('click', () => setMenu(false)));
document.addEventListener('keydown', event => {
  if (event.key === 'Escape') setMenu(false);
});

const copyText = async text => {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
};
document.querySelectorAll('.copy-button').forEach(button => button.addEventListener('click', async () => {
  const label = button.textContent;
  button.textContent = (await copyText(button.dataset.copy)) ? 'Copied' : 'Copy failed';
  setTimeout(() => { button.textContent = label; }, 1400);
}));

const meta = document.getElementById('download-meta');
if (meta && 'fetch' in window) {
  fetch('https://api.github.com/repos/tochi-mba/Android_Headless_Mirror/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
    .then(response => (response.ok ? response.json() : null))
    .then(release => {
      const asset = release?.assets?.find(a => a.name === 'AndroidHeadlessMirror-Setup.exe');
      if (asset) meta.textContent = `${release.tag_name} · ${Math.round(asset.size / 1048576)} MB · Windows 10 or 11 · 64-bit`;
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

document.getElementById('year').textContent = String(new Date().getFullYear());
