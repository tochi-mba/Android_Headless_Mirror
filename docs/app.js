const menuButton=document.querySelector('.menu-button');
const nav=document.querySelector('.site-nav');
menuButton?.addEventListener('click',()=>{const open=menuButton.getAttribute('aria-expanded')==='true';menuButton.setAttribute('aria-expanded',String(!open));nav.classList.toggle('open',!open)});
const closeMenu=()=>{nav?.classList.remove('open');menuButton?.setAttribute('aria-expanded','false')};
nav?.querySelectorAll('a').forEach(a=>a.addEventListener('click',closeMenu));
document.addEventListener('keydown',event=>{if(event.key==='Escape') closeMenu()});

const outputs={
  authorized:'<span class="console-head">=== Android Headless Mirror Diagnostics ===</span>\nADB:            <b>OK</b>\nscrcpy:         <b>OK</b>\nPersistent OFF: no\nSupervisor:     <b>RUNNING</b>\n\nSerial:         R3CRB04F7PP\nADB state:      <b>device</b>\nDevice:         Samsung SM-G998B\nADB auth:       <span class="success">AUTHORIZED</span>',
  unauthorized:'<span class="console-head">=== Android Headless Mirror Diagnostics ===</span>\nADB:            <b>OK</b>\nscrcpy:         <b>OK</b>\nPersistent OFF: no\nSupervisor:     <b>RUNNING</b>\n\nSerial:         R3CRB04F7PP\nADB state:      <b>unauthorized</b>\nADB auth:       <span style="color:var(--live)">UNAUTHORIZED</span>\n\nAction: unlock Android and approve\n"Allow USB debugging".',
  offline:'<span class="console-head">=== Android Headless Mirror Diagnostics ===</span>\nADB:            <b>OK</b>\nscrcpy:         <b>OK</b>\nPersistent OFF: no\nSupervisor:     <b>RUNNING</b>\n\nSerial:         R3CRB04F7PP\nADB state:      <b>offline</b>\nADB auth:       <span style="color:var(--live)">OFFLINE</span>\n\nAction: reconnect USB, then restart\nADB or the device if needed.'
};

const diagnosticTabs=[...document.querySelectorAll('.diag-tab')];
const diagnosticPanel=document.getElementById('diagnostic-panel');
const activateDiagnosticTab=(tab,focus=false)=>{
  diagnosticTabs.forEach(item=>{
    const active=item===tab;
    item.classList.toggle('active',active);
    item.setAttribute('aria-selected',String(active));
    item.tabIndex=active?0:-1;
  });
  diagnosticPanel?.setAttribute('aria-labelledby',tab.id);
  document.getElementById('diagnostic-output').innerHTML=outputs[tab.dataset.state];
  if(focus) tab.focus();
};
diagnosticTabs.forEach((tab,index)=>{
  tab.addEventListener('click',()=>activateDiagnosticTab(tab));
  tab.addEventListener('keydown',event=>{
    let next=index;
    if(event.key==='ArrowRight') next=(index+1)%diagnosticTabs.length;
    else if(event.key==='ArrowLeft') next=(index-1+diagnosticTabs.length)%diagnosticTabs.length;
    else if(event.key==='Home') next=0;
    else if(event.key==='End') next=diagnosticTabs.length-1;
    else return;
    event.preventDefault();
    activateDiagnosticTab(diagnosticTabs[next],true);
  });
});

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
