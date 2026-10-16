// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Home page: recently used and most used tools (kept in this window's local storage only), all tools, and Magestica branding.
"use strict";
TOOL_NAMES.home='Home';

const USAGE_KEY='usage';
function usageLoad(){try{return JSON.parse(localStorage.getItem(USAGE_KEY))||{}}catch(e){return {}}}
function usageSave(u){try{localStorage.setItem(USAGE_KEY,JSON.stringify(u))}catch(e){}}

function homeTools(){return $$('#rail .ri[data-tool]').filter(a=>a.dataset.tool!=='home').map(a=>({id:a.dataset.tool,name:TOOL_NAMES[a.dataset.tool]||a.title,icons:[...a.querySelectorAll('.cicon,.fi')],desc:a.title}))}

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

function renderHome(){
  const root=$('#homebody');if(!root)return;
  const tools=homeTools(),byId=Object.fromEntries(tools.map(t=>[t.id,t])),u=usageLoad();
  const used=Object.entries(u).filter(([id])=>byId[id]);
  const recent=used.slice().sort((a,b)=>b[1].t-a[1].t).slice(0,6).map(([id,e])=>homeCard(byId[id],ago(e.t)));
  const most=used.slice().sort((a,b)=>b[1].n-a[1].n||b[1].t-a[1].t).slice(0,6).map(([id,e])=>homeCard(byId[id],e.n+(e.n===1?' use':' uses')));
  root.replaceChildren(
    homeSection('Recent',recent,'Tools you open will show up here.'),
    homeSection('Most used',most,'Your most used tools will show up here.'),
    homeSection('All tools',tools.map(t=>homeCard(t)),''));
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
