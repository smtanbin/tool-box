// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Menus that follow the open tool: File lists the current tool's own actions plus app-wide ones (administrator,
// data folder, exit); View adds zoom and full screen; Ctrl+K opens a command palette over everything.
"use strict";
(function(){
// [label, button selector to click, shortcut hint]; a function may stand in for the selector
const TOOL_ACTIONS={
  foldersize:[['Scan folder…','#scan','F5'],['Browse for folder…','#browse'],['Reset','#reset','Ctrl+R'],['Export to CSV…','#export'],['Open selected in Explorer','#openx']],
  disk:[['Refresh drives','#dkref','F5'],['Raw values: hex / decimal','#dkraw']],
  proc:[['Show processes','#pmtabs [data-v=procs]'],['Show ports','#pmtabs [data-v=ports]'],['End selected process','#pmkill','Del'],['End selected process tree','#pmtree','Shift+Del'],['Free a port…',()=>$('#pmkp').focus()]],
  hosts:[['Save','#hssave','Ctrl+S'],['Add entry','#hsadd'],['Reload from disk','#hsreload'],['Discard changes','#hsrevert'],['Flush DNS cache','#hsflush'],['Edit as raw text','#hstabs [data-v=raw]']],
  env:[['New variable','#evnew'],['User variables','#evscope [data-s=user]'],['System variables','#evscope [data-s=system]'],['Reload','#evreload']],
  adapters:[['Refresh adapters','#adref','F5'],['Copy all details','#adcopy']],
  dns:[['Look up','#dngo','Enter'],['Edit name…',()=>$('#dnname').focus()]],
  trace:[['Start trace','#trgo'],['Stop','#trstop'],['Copy result','#trcopy']],
  ping:[['Start','#pggo'],['Stop','#pgstop'],['Clear','#pgclear']],
  ipscan:[['My networks','#islocal'],['Scan','#isgo'],['Cancel scan','#isstop'],['Export CSV','#iscsv']],
  telnet:[['Test ports','#ptgo'],['Connect console','#tngo'],['Disconnect','#tnstop']],
  subnet:[['Copy subnets as CSV','#sncsv']],
  hash:[['Hash a file…','#hsfile']],
  crypt:[['Run','#crgo'],['Use result as input','#crswap']],
  token:[['Generate UUIDs','#uugo'],['Generate tokens','#tkgo']],
  encode:[['Swap input and output','#enswap']],
  json:[['Format','#jsfmt'],['Minify','#jsmin']],
  time:[['Now','#tmnowbtn']],
  img64:[['Choose image…','#i64pick'],['Save Base64 as .txt','#i64save'],['Save image','#i64dl']],
  ico:[['Choose image…','#icopick'],['Download .ico','#icodl']],
};
const visible=el=>el&&el.offsetParent!==null||el&&el.getClientRects().length>0;
function actionsFor(t){return(TOOL_ACTIONS[t]||[]).map(([label,sel,kbd])=>{
  if(typeof sel==='function')return{label,kbd,run:sel,ok:true};
  const el=document.querySelector(sel);return{label,kbd,run:()=>el&&el.click(),ok:!!el&&!el.disabled&&(visible(el)||sel.includes('['))}})}

let info={admin:false,dataDir:''};
api('/api/info').then(r=>{info=r}).catch(()=>{});

/* ---------- zoom and full screen ---------- */
let zoom=1;try{zoom=+localStorage.getItem('zoom')||1}catch(e){}
function setZoom(z){zoom=Math.min(1.6,Math.max(.7,Math.round(z*10)/10));document.documentElement.style.zoom=zoom===1?'':zoom;
  try{localStorage.setItem('zoom',zoom)}catch(e){}}
setZoom(zoom);
const fullscreen=()=>{if(document.fullscreenElement)document.exitFullscreen();else document.documentElement.requestFullscreen?.().catch(()=>{})};

/* ---------- app-wide actions ---------- */
async function elevate(){try{await api('/api/elevate?tool='+encodeURIComponent(curTool));document.body.innerHTML='<div style="padding:60px;text-align:center">My Tool Box is restarting as administrator in a new window. You can close this one.</div>';setTimeout(()=>window.close(),800)}catch(e){toast(e.message)}}
async function openData(){try{await api('/api/opendata',{method:'POST'});toast('Opened '+(info.dataDir||'the data folder'))}catch(e){toast(e.message)}}
Object.assign(CMD,{palette:()=>openPalette(),elevate,opendata:openData,'zoom-in':()=>setZoom(zoom+.1),'zoom-out':()=>setZoom(zoom-.1),'zoom-reset':()=>setZoom(1),fullscreen});

/* ---------- File menu: rebuilt each time it opens, for the tool on screen ---------- */
const fileDD=document.querySelector('#menubar .menu .dd');
function renderFile(){
  const acts=actionsFor(curTool);acts.forEach((a,i)=>{CMD['act'+i]=a.run});
  fileDD.innerHTML=`<div data-cmd="home">Home<kbd>Alt+Home</kbd></div><div data-cmd="palette">Go to tool or command…<kbd>Ctrl+K</kbd></div><hr>`+
    (acts.length?`<div class="mh">${esc(TOOL_NAMES[curTool]||'')}</div>`+acts.map((a,i)=>`<div data-cmd="act${i}" class="${a.ok?'':'off'}">${esc(a.label)}<kbd>${esc(a.kbd||'')}</kbd></div>`).join('')+'<hr>':'')+
    (info.admin?`<div class="off">Running as administrator<kbd>✓</kbd></div>`:`<div data-cmd="elevate">Restart as administrator</div>`)+
    `<div data-cmd="opendata" title="${esc(info.dataDir||'')}">Open app data folder</div><hr><div data-cmd="exit">Exit<kbd>Alt+F4</kbd></div>`}
const fileMenu=fileDD.parentElement;
fileMenu.addEventListener('click',renderFile,true);fileMenu.addEventListener('mouseenter',renderFile);

/* ---------- View menu: zoom and full screen ---------- */
const viewDD=document.querySelectorAll('#menubar .menu .dd')[1];
if(viewDD)viewDD.insertAdjacentHTML('beforeend',`<hr><div data-cmd="zoom-in">Zoom in<kbd>Ctrl+=</kbd></div><div data-cmd="zoom-out">Zoom out<kbd>Ctrl+-</kbd></div><div data-cmd="zoom-reset">Actual size<kbd>Ctrl+0</kbd></div><div data-cmd="fullscreen">Full screen<kbd>F11</kbd></div>`);

/* ---------- command palette (Ctrl+K) ---------- */
const pal=document.createElement('div');pal.id='palette';pal.setAttribute('role','dialog');pal.setAttribute('aria-label','Go to tool or command');
pal.innerHTML='<div class="pcard"><input id="palq" placeholder="Type a tool or command…" spellcheck="false" autocomplete="off"><div id="pallist" role="listbox"></div></div>';
document.body.appendChild(pal);
let items=[],sel=0;
function allItems(){
  const L=[];
  for(const a of document.querySelectorAll('#rail .ri[data-tool]'))L.push({label:(a.querySelector('.lbl')||a).textContent.trim(),hint:'Tool',run:()=>showTool(a.dataset.tool)});
  for(const a of actionsFor(curTool))if(a.ok)L.push({label:a.label,hint:TOOL_NAMES[curTool],run:a.run});
  for(const [k,l] of[['theme-system','Theme: use system setting'],['theme-light','Theme: light'],['theme-dark','Theme: dark'],['skin-default','Skin: Fluent'],['skin-burgundy','Skin: Magestica'],['skin-aero','Skin: Aero'],['skin-black','Skin: Black & White'],
    ['zoom-in','Zoom in'],['zoom-out','Zoom out'],['zoom-reset','Actual size'],['fullscreen','Full screen'],['opendata','Open app data folder'],['keys','Keyboard shortcuts'],['faq','FAQ'],['about','About'],['exit','Exit']])
    if(CMD[k])L.push({label:l,hint:'Command',run:CMD[k]});
  if(!info.admin)L.push({label:'Restart as administrator',hint:'Command',run:elevate});
  return L}
function renderList(){
  const q=$('#palq').value.trim().toLowerCase().split(/\s+/).filter(Boolean);
  items=allItems().filter(it=>q.every(w=>(it.label+' '+it.hint).toLowerCase().includes(w))).slice(0,40);
  sel=Math.min(sel,Math.max(0,items.length-1));
  $('#pallist').innerHTML=items.map((it,i)=>`<div class="pitem${i===sel?' on':''}" data-i="${i}" role="option"><span>${esc(it.label)}</span><small>${esc(it.hint||'')}</small></div>`).join('')||'<div class="pempty">No matches</div>';
  $('#pallist .pitem.on')?.scrollIntoView({block:'nearest'})}
function openPalette(){pal.classList.add('open');$('#palq').value='';sel=0;renderList();$('#palq').focus()}
function closePalette(){pal.classList.remove('open')}
function runSel(){const it=items[sel];closePalette();if(it)it.run()}
$('#palq').addEventListener('input',()=>{sel=0;renderList()});
$('#palq').addEventListener('keydown',e=>{
  if(e.key==='ArrowDown'){e.preventDefault();sel=Math.min(items.length-1,sel+1);renderList()}
  else if(e.key==='ArrowUp'){e.preventDefault();sel=Math.max(0,sel-1);renderList()}
  else if(e.key==='Enter'){e.preventDefault();runSel()}
  else if(e.key==='Escape'){e.preventDefault();e.stopPropagation();closePalette()}});
$('#pallist').addEventListener('click',e=>{const p=e.target.closest('.pitem');if(p){sel=+p.dataset.i;runSel()}});
pal.addEventListener('click',e=>{if(e.target===pal)closePalette()});

/* ---------- shortcuts ---------- */
addEventListener('keydown',e=>{
  const k=e.key.toLowerCase();
  if(e.ctrlKey&&!e.altKey&&k==='k'){e.preventDefault();pal.classList.contains('open')?closePalette():openPalette()}
  else if(e.ctrlKey&&(k==='='||k==='+')){e.preventDefault();setZoom(zoom+.1)}
  else if(e.ctrlKey&&k==='-'){e.preventDefault();setZoom(zoom-.1)}
  else if(e.ctrlKey&&k==='0'){e.preventDefault();setZoom(1)}
  else if(e.key==='F11'){e.preventDefault();fullscreen()}});
window.MenuTest={actionsFor,openPalette,closePalette,renderFile,allItems:()=>allItems(),get zoom(){return zoom},setZoom};
})();
