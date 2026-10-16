// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Environment variables and PATH editor. Shares the helpers from index.html / tools.js.
"use strict";
TOOL_NAMES.env='Environment variables';

/* ---------- PATH helpers (pure, also used by the tests) ---------- */
const envSplitPath=v=>String(v).split(';').map(s=>s.trim()).filter(Boolean);
const envKey=p=>p.trim().replace(/^"|"$/g,'').replace(/[\\/]+$/,'').toLowerCase();
function envDupes(list){const seen=new Set(),dup=new Set();list.forEach((p,i)=>{const k=envKey(p);if(seen.has(k))dup.add(i);seen.add(k)});return dup}
const envLooksPath=(name,value)=>/path/i.test(name)&&String(value).includes(';')||/^path$/i.test(name);

const en={scope:'user',data:{user:[],system:[]},admin:true,q:'',sel:null,isNew:false,mode:'text',paths:[],checks:[]};

toolInit.env=()=>{
  segInit('#evscope',b=>{en.scope=b.dataset.s;en.sel=null;enClose();enRender()});
  $('#evq').addEventListener('input',debounce(e=>{en.q=e.target.value.trim().toLowerCase();enRender()},120));
  $('#evnew').onclick=()=>{en.sel=null;en.isNew=true;enOpen({name:'',value:'',expand:false})};
  $('#evreload').onclick=()=>enLoad(true);
  $('#evtbl').addEventListener('click',e=>{const tr=e.target.closest('tr[data-n]');if(!tr)return;const v=en.data[en.scope].find(x=>x.name===tr.dataset.n);if(v){en.sel=v.name;en.isNew=false;enOpen(v)}});
  segInit('#evmode',b=>enMode(b.dataset.m));
  $('#evsave').onclick=enSave;$('#evdel').onclick=enDelete;$('#evcancel').onclick=()=>{enClose();enRender()};
  $('#evelev').onclick=async()=>{try{await api('/api/elevate?tool=env');document.body.innerHTML='<div style="padding:60px;text-align:center">My Tool Box is restarting as administrator in a new window. You can close this one.</div>';setTimeout(()=>window.close(),800)}catch(e){toast(e.message)}};
  $('#evpadd').onclick=()=>{en.paths.push('');enPathRender(true)};
  $('#evpdup').onclick=()=>{const d=envDupes(en.paths);en.paths=en.paths.filter((_,i)=>!d.has(i));enPathRender()};
  $('#evpmiss').onclick=()=>{en.paths=en.paths.filter((_,i)=>en.checks[i]?.exists!==false);enPathRender()};
  $('#evptbl').addEventListener('input',e=>{const el=e.target.closest('input[data-i]');if(el){en.paths[+el.dataset.i]=el.value;enPathCheckSoon()}});
  $('#evptbl').addEventListener('click',e=>{const b=e.target.closest('[data-act]');if(!b)return;const i=+b.dataset.i,a=b.dataset.act,P=en.paths;
    if(a==='del')P.splice(i,1);else if(a==='up'&&i>0)[P[i-1],P[i]]=[P[i],P[i-1]];else if(a==='down'&&i<P.length-1)[P[i+1],P[i]]=[P[i],P[i+1]];enPathRender()});
  enLoad()};

const enPost=(url,body)=>api(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});

async function enLoad(again){
  try{const r=await api('/api/env');en.data={user:r.user,system:r.system};en.admin=r.admin;if(again){enClose();toast('Reloaded')}enRender()}
  catch(e){$('#evtbl').innerHTML=`<tbody><tr><td class="empty">${esc(e.message)}</td></tr></tbody>`}}

function enRender(){
  $('#evadmin').style.display=en.scope==='system'&&!en.admin?'flex':'none';
  const q=en.q,rows=en.data[en.scope].filter(v=>!q||v.name.toLowerCase().includes(q)||v.value.toLowerCase().includes(q));
  $('#evtbl').innerHTML='<thead><tr><th>Name</th><th>Value</th><th>Type</th></tr></thead><tbody>'+(rows.map(v=>`<tr data-n="${esc(v.name)}" class="${v.name===en.sel?'sel':''}"><td class="nw mono"><b>${esc(v.name)}</b></td><td class="mono pp" title="${esc(v.value)}" style="max-width:520px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(v.value)}</td><td class="pp nw">${v.expand?'Expandable':'Text'}</td></tr>`).join('')||`<tr><td class="empty" colspan="3">${q?'Nothing matches the filter':'No variables'}</td></tr>`)+'</tbody>';
  $('#s3').textContent=`${en.data[en.scope].length} ${en.scope} variables`}

function enClose(){$('#evedit').style.display='none';en.sel=null;en.isNew=false}
function enOpen(v){
  $('#evedit').style.display='';$('#everr').textContent='';
  $('#evname').value=v.name;$('#evname').dataset.orig=en.isNew?'':v.name;$('#evexp').checked=!!v.expand;$('#evval').value=v.value;$('#evdel').style.display=en.isNew?'none':'';
  const pathy=envLooksPath(v.name,v.value);$('#evmode').style.display=pathy?'':'none';
  $$('#evmode button').forEach(b=>b.classList.toggle('on',b.dataset.m==='text'));
  enMode(pathy&&/^path$/i.test(v.name)?'list':'text');if(pathy&&/^path$/i.test(v.name))$$('#evmode button').forEach(b=>b.classList.toggle('on',b.dataset.m==='list'));
  enRender();$('#evedit').scrollIntoView({block:'nearest'});if(en.isNew)$('#evname').focus()}

function enMode(m){
  if(en.mode==='list'&&m==='text')$('#evval').value=en.paths.join(';');
  en.mode=m;
  if(m==='list'){en.paths=envSplitPath($('#evval').value);enPathRender()}
  $('#evlist').style.display=m==='list'?'':'none';$('#evval').style.display=m==='list'?'none':''}

let enCheckTimer=0;
const enPathCheckSoon=()=>{clearTimeout(enCheckTimer);enCheckTimer=setTimeout(()=>enPathRender(false,true),400)};
async function enPathRender(focusLast,keepFocus){
  const active=keepFocus?document.activeElement:null,ai=active&&active.dataset?active.dataset.i:null;
  try{en.checks=await enPost('/api/env/pathcheck',{paths:en.paths})}catch(e){en.checks=[]}
  const dup=envDupes(en.paths),miss=en.checks.filter(c=>c&&!c.exists).length;
  $('#evpinfo').textContent=`${en.paths.length} folders · ${dup.size} duplicate${dup.size===1?'':'s'} · ${miss} missing`;
  $('#evptbl').innerHTML='<thead><tr><th style="width:30px"></th><th>Folder</th><th style="width:100px"></th></tr></thead><tbody>'+en.paths.map((p,i)=>{const c=en.checks[i]||{},bad=c.exists===false,d=dup.has(i);
    return`<tr><td><span class="sdot ${bad?'bad':d?'caution':'ok'}" title="${bad?'Folder not found':d?'Duplicate':'OK'}"></span></td><td><input class="mono" data-i="${i}" value="${esc(p)}" spellcheck="false" style="width:100%"></td>`+
      `<td class="nw"><button class="ib" data-act="up" data-i="${i}" title="Move up"><i class="fi">&#xE70E;</i></button><button class="ib" data-act="down" data-i="${i}" title="Move down"><i class="fi">&#xE70D;</i></button><button class="ib" data-act="del" data-i="${i}" title="Remove"><i class="fi">&#xE74D;</i></button></td></tr>`}).join('')+'</tbody>';
  if(focusLast){const a=$$('#evptbl input');a[a.length-1]?.focus()}
  else if(ai!=null){$(`#evptbl input[data-i="${ai}"]`)?.focus()}}

async function enSave(){
  const name=$('#evname').value.trim(),orig=$('#evname').dataset.orig||'';let value=en.mode==='list'?en.paths.map(s=>s.trim()).filter(Boolean).join(';'):$('#evval').value;
  $('#everr').textContent='';
  if(!name||/[=\0]/.test(name))return $('#everr').textContent='A name cannot be empty or contain =';
  if(!orig&&en.data[en.scope].some(v=>v.name.toLowerCase()===name.toLowerCase()))return $('#everr').textContent='A variable with this name already exists';
  if(/^path$/i.test(name)&&!value.trim()&&!await askDialog({title:'Empty PATH',danger:true,okText:'Save empty PATH',body:'<p>PATH would be <b>empty</b>. Most programs and command prompts will stop finding commands.</p>'}))return;
  try{const r=await enPost('/api/env/set',{scope:en.scope,name,value,expand:$('#evexp').checked,rename:orig&&orig!==name?orig:''});
    toast('Saved'+(r.backup?'. Backup written':''));en.sel=name;en.isNew=false;await enLoad();const v=en.data[en.scope].find(x=>x.name===name);if(v)enOpen(v)}
  catch(e){$('#everr').textContent=e.message;if(/administrator/i.test(e.message)){en.admin=false;enRender()}}}

async function enDelete(){const name=$('#evname').dataset.orig;if(!name)return;
  if(!await askDialog({title:'Delete variable?',danger:true,okText:'Delete',body:`<p>Delete <b>${esc(name)}</b> from the ${en.scope} variables?</p><span class="path">${esc($('#evval').value.slice(0,400))}</span><p style="color:var(--muted)">A backup of all variables is saved first.</p>`}))return;
  try{await enPost('/api/env/delete',{scope:en.scope,name});toast('Deleted');enClose();await enLoad()}catch(e){$('#everr').textContent=e.message}}
