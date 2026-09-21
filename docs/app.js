const menuButton=document.querySelector('.menu-button');
const nav=document.querySelector('.site-nav');
menuButton?.addEventListener('click',()=>{const open=menuButton.getAttribute('aria-expanded')==='true';menuButton.setAttribute('aria-expanded',String(!open));nav.classList.toggle('open',!open)});
nav?.querySelectorAll('a').forEach(a=>a.addEventListener('click',()=>{nav.classList.remove('open');menuButton?.setAttribute('aria-expanded','false')}));

const outputs={
  authorized:'<span class="console-head">=== Android Headless Mirror Diagnostics ===</span>\nADB:            <b>OK</b>\nscrcpy:         <b>OK</b>\nPersistent OFF: no\nSupervisor:     <b>RUNNING</b>\n\nSerial:         R3CRB04F7PP\nADB state:      <b>device</b>\nDevice:         Samsung SM-G998B\nADB auth:       <span class="success">AUTHORIZED</span>',
  unauthorized:'<span class="console-head">=== Android Headless Mirror Diagnostics ===</span>\nADB:            <b>OK</b>\nscrcpy:         <b>OK</b>\nPersistent OFF: no\nSupervisor:     <b>RUNNING</b>\n\nSerial:         R3CRB04F7PP\nADB state:      <b>unauthorized</b>\nADB auth:       <span style="color:var(--warn)">UNAUTHORIZED</span>\n\nAction: unlock Android and approve\n"Allow USB debugging".',
  offline:'<span class="console-head">=== Android Headless Mirror Diagnostics ===</span>\nADB:            <b>OK</b>\nscrcpy:         <b>OK</b>\nPersistent OFF: no\nSupervisor:     <b>RUNNING</b>\n\nSerial:         R3CRB04F7PP\nADB state:      <b>offline</b>\nADB auth:       <span style="color:var(--danger)">OFFLINE</span>\n\nAction: reconnect USB, then restart\nADB or the device if needed.'
};

document.querySelectorAll('.diag-tab').forEach(tab=>tab.addEventListener('click',()=>{
  document.querySelectorAll('.diag-tab').forEach(t=>{t.classList.remove('active');t.setAttribute('aria-selected','false')});
  tab.classList.add('active');tab.setAttribute('aria-selected','true');
  document.getElementById('diagnostic-output').innerHTML=outputs[tab.dataset.state];
}));

document.querySelectorAll('.copy-button').forEach(button=>button.addEventListener('click',async()=>{
  try{await navigator.clipboard.writeText(button.dataset.copy);const old=button.textContent;button.textContent='Copied';setTimeout(()=>button.textContent=old,1400)}
  catch{button.textContent='Copy failed'}
}));

const reduced=window.matchMedia('(prefers-reduced-motion: reduce)').matches;
if(!reduced&&'IntersectionObserver'in window){\n  document.documentElement.classList.add('motion-ready');
  const observer=new IntersectionObserver(entries=>entries.forEach(entry=>{if(entry.isIntersecting){entry.target.classList.add('visible');observer.unobserve(entry.target)}}),{threshold:.12});
  document.querySelectorAll('.reveal').forEach(el=>observer.observe(el));
}else{document.querySelectorAll('.reveal').forEach(el=>el.classList.add('visible'))}

document.getElementById('year').textContent=new Date().getFullYear();
