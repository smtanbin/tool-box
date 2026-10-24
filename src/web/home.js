// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Home page: recently used and most used tools (kept in this window's local storage only), all tools, and Magestica branding.
"use strict";
TOOL_NAMES.home='Home';

const USAGE_KEY='usage';
function usageLoad(){try{return JSON.parse(localStorage.getItem(USAGE_KEY))||{}}catch(e){return {}}}
function usageSave(u){try{localStorage.setItem(USAGE_KEY,JSON.stringify(u))}catch(e){}}

function homeTools(){return $$('#rail .ri[data-tool]').filter(a=>a.dataset.tool!=='home').map(a=>({id:a.dataset.tool,name:TOOL_NAMES[a.dataset.tool]||a.title,icons:[...a.querySelectorAll('img,.cicon,.fi')],desc:a.title}))}

function homeCard(t,sub){
  const a=document.createElement('a');a.className='hcard';a.href='#'+t.id;a.title=t.desc;
  const ic=document.createElement('span');ic.className='hico';t.icons.forEach(i=>ic.appendChild(i.cloneNode(true)));
  const tx=document.createElement('span');tx.className='htxt';
  const nm=document.createElement('b');nm.textContent=t.name;tx.appendChild(nm);
  if(sub){const s=document.createElement('small');s.textContent=sub;tx.appendChild(s)}
  a.append(ic,tx);return a}

function homeSection(title,cards,empty){
  const s=document.createElement('section');s.className='hsec';
  const h=document.createElement('h3');h.textContent=title;s.appendChild(h);
  const g=document.createElement('div');g.className='hgrid';
  if(cards.length)cards.forEach(c=>g.appendChild(c));
  else{const p=document.createElement('p');p.className='hempty';p.textContent=empty;g.appendChild(p)}
  s.appendChild(g);return s}

function ago(ts){const s=Math.max(1,Math.round((Date.now()-ts)/1000));
  if(s<60)return 'just now';const m=Math.round(s/60);if(m<60)return m+' min ago';
  const h=Math.round(m/60);if(h<24)return h+' h ago';const d=Math.round(h/24);return d+(d===1?' day ago':' days ago')}

/* short descriptions shown on the home cards */
const HOME_DESC={foldersize:'See what fills your disks',disk:'Health, SMART, DRAM-less check',hw:'CPU, board, memory, graphics',temps:'Live graphs of every temperature sensor',proc:'Processes, ports, end tasks',hosts:'Edit the hosts file safely',
  env:'Variables and PATH, with backups',adapters:'IP, gateway, DNS, MAC, speed',dns:'Any record, any DNS server',trace:'Follow the route hop by hop',
  subnet:'Networks, masks and ranges',ipscan:'Find devices on a network',ping:'Live latency and packet loss',telnet:'Test ports, raw TCP console',
  hash:'MD5, SHA-1, SHA-256 and more',crypt:'AES-256 with a passphrase',token:'UUIDs, tokens, passwords',encode:'Base64, URL, hex, HTML',
  img64:'Pictures to Base64 and back',ico:'Multi-size Windows icons',jwt:'Read tokens and claims',json:'Format, minify, validate',
  time:'Unix time to dates and back',base:'Binary, octal, decimal, hex'};
const HOME_FEATURED=[['foldersize','#f59e0b'],['hw','#6366f1'],['proc','#10b981'],['disk','#0ea5e9']];

function homeHero(){
  const h=$('#hhero');if(!h)return;const hr=new Date().getHours();
  const greet=hr<5?'Working late':hr<12?'Good morning':hr<18?'Good afternoon':'Good evening';
  const n=homeTools().length;
  h.innerHTML=`<div class="hh-text"><small class="hh-greet">${greet}</small><h2>My Tool Box.<span class="hh-line2">One app for every tool.</span></h2>
    <p>Everyday IT tools for Windows, in one private, offline app.</p>
    <button class="hh-search" id="hhsearch" type="button"><i class="fi">&#xE721;</i><span>Search tools and commands</span><kbd>Ctrl+K</kbd></button>
    <div class="hh-chips"><span><i class="fi">&#xE74C;</i>${n} tools</span><span><i class="fi">&#xE72E;</i>Private and offline</span>${typeof VERSION!=='undefined'?`<span><i class="fi">&#xE946;</i>v${esc(VERSION)}</span>`:''}</div></div>
    <div class="hh-art" aria-hidden="true"><span class="hh-orb o1"></span><span class="hh-orb o2"></span><span class="hh-orb o3"></span><img src="/icon-256.png" alt=""></div>`;
  $('#hhsearch').onclick=()=>CMD.palette?CMD.palette():null}

function homeFeature(t,color){
  const a=homeCard(t,HOME_DESC[t.id]||'');a.classList.add('hfeat');a.style.setProperty('--fc',color);return a}

function renderHome(){
  const root=$('#homebody');if(!root)return;homeHero();
  const tools=homeTools(),byId=Object.fromEntries(tools.map(t=>[t.id,t])),u=usageLoad();
  const used=Object.entries(u).filter(([id])=>byId[id]);
  const recent=used.slice().sort((a,b)=>b[1].t-a[1].t).slice(0,6).map(([id,e])=>homeCard(byId[id],ago(e.t)));
  const most=used.slice().sort((a,b)=>b[1].n-a[1].n||b[1].t-a[1].t).slice(0,6).map(([id,e])=>homeCard(byId[id],e.n+(e.n===1?' use':' uses')));
  const feat=homeSection('Start here',HOME_FEATURED.filter(([id])=>byId[id]).map(([id,c])=>homeFeature(byId[id],c)),'');feat.classList.add('hsec-feat');
  const parts=[feat];
  if(recent.length)parts.push(homeSection('Recent',recent,''));
  if(most.length)parts.push(homeSection('Most used',most,''));
  // all tools, grouped like the sidebar
  let group=null,cards=[];const flush=()=>{if(group&&cards.length)parts.push(homeSection(group,cards,''));cards=[]};
  for(const e of $$('#rail > .rh, #rail > .ri[data-tool]')){
    if(e.classList.contains('rh')){flush();group=e.textContent.trim();continue}
    const t=byId[e.dataset.tool];if(t)cards.push(homeCard(t,HOME_DESC[t.id]||''))}
  flush();
  root.replaceChildren(...parts);
}

/* count every tool the user opens (the home page itself is not counted) and draw the home page when it is shown */
(function(){const inner=showTool;
  showTool=function(t){inner(t);if(window.railOpenFor)railOpenFor(t);
    if(t==='home'){renderHome();return}
    if(!TOOL_NAMES[t]||!document.getElementById('t-'+t))return;
    const u=usageLoad(),e=u[t]||{n:0,t:0};e.n++;e.t=Date.now();u[t]=e;usageSave(u)};
})();

/* branding footer */
(function(){const f=$('#homefoot');if(!f)return;
  const REPO='https://github.com/smtanbin/tool-box',SITE='https://magestica.com/';
  const mk=(href,text)=>{const a=document.createElement('a');a.href=href;a.target='_blank';a.rel='noopener';a.textContent=text;return a};
  const brand=document.createElement('span');brand.className='hbrand';
  const logo=document.createElement('img');logo.src='/icon.svg';logo.width=22;logo.height=22;logo.alt='';
  brand.append(logo,document.createTextNode('A '),mk(SITE,'Magestica'),document.createTextNode(' app'));
  const links=document.createElement('span');links.className='hlinks';
  links.append(mk(SITE,'magestica.com'),mk(REPO,'GitHub'),mk(REPO+'/blob/main/PRIVACY.md','Privacy'));
  const v=document.createElement('span');v.className='hver';v.textContent='© '+new Date().getFullYear()+' Magestica'+(typeof VERSION!=='undefined'?' · v'+VERSION:'');
  f.append(brand,links,v)})();

/* menu bar: Tools menu built from the sidebar, shared Home / sidebar / web-link commands */
(function(){const REPO='https://github.com/smtanbin/tool-box',SITE='https://magestica.com/';
  const open=u=>window.open(u,'_blank','noopener');
  Object.assign(CMD,{home:()=>showTool('home'),rail:()=>$('#railtoggle').click(),details:toggleDetails,
    'web-site':()=>open(SITE),'web-repo':()=>open(REPO),'web-issue':()=>open(REPO+'/issues'),'web-privacy':()=>open(REPO+'/blob/main/PRIVACY.md')});
  const m=$('#toolsmenu');if(m){
    for(const e of $$('#rail > .rh, #rail > .ri[data-tool]')){
      if(e.classList.contains('rh')){if(m.children.length)m.appendChild(document.createElement('hr'));continue}
      const id=e.dataset.tool;CMD['tool-'+id]=()=>showTool(id);
      const d=document.createElement('div');d.dataset.cmd='tool-'+id;d.textContent=TOOL_NAMES[id]||e.title;m.appendChild(d)}}
  addEventListener('keydown',e=>{if(e.altKey&&e.key==='Home'){e.preventDefault();showTool('home')}})})();
