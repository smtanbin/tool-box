// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Hosts file editor: entries table, raw text view, save with backup, flush DNS. Shares the helpers from index.html / tools.js.
"use strict";
TOOL_NAMES.hosts='Hosts file';

/* ---------- parsing (pure functions, also used by the tests) ---------- */
const hsIPv4=/^(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}$/;
const hsValidIP=s=>hsIPv4.test(s)||(s.includes(':')&&/^[0-9a-f:.%a-z]+$/i.test(s)&&s.length<=50);
const hsValidHost=s=>/^[A-Za-z0-9_]([A-Za-z0-9_.-]*[A-Za-z0-9_])?$/.test(s)&&s.length<=253;
// "10.0.0.1 a b # note" -> {ip,hosts,c}; null when the text is not an entry
function hsEntry(body){const m=body.match(/^\s*(\S+)\s+([^#]*?)\s*(?:#\s?(.*))?$/);if(!m||!hsValidIP(m[1]))return null;
  const names=m[2].split(/\s+/).filter(Boolean);if(!names.length||!names.every(hsValidHost))return null;return{ip:m[1],hosts:names.join(' '),c:(m[3]||'').trim()}}
function parseHosts(text){return text.replace(/^\uFEFF/,'').split(/\r\n|\n|\r/).filter((l,i,a)=>i<a.length-1||l!=='').map(raw=>{
  const t=raw.trim();if(t==='')return{t:'text',raw:''};
  if(t[0]==='#'){const e=hsEntry(t.replace(/^#+\s*/,''));return e?{t:'entry',on:false,...e}:{t:'text',raw}}
  const e=hsEntry(t);return e?{t:'entry',on:true,...e}:{t:'text',raw}})}
function serializeHosts(lines){return lines.map(l=>l.t==='text'?l.raw:`${l.on?'':'# '}${l.ip}\t${l.hosts}${l.c?'\t# '+l.c:''}`).join('\r\n')+'\r\n'}
const hsProblem=l=>l.t!=='entry'?'':!hsValidIP(l.ip)?'Not a valid IP address':!l.hosts.trim()?'Add at least one host name':l.hosts.trim().split(/\s+/).some(h=>!hsValidHost(h))?'Host names may only use letters, digits, dot, dash and underscore':'';

/* ---------- page ---------- */
const hs={lines:[],orig:'',view:'list',q:'',admin:true,path:'',loaded:false};

toolInit.hosts=()=>{
  segInit('#hstabs',b=>{if(hs.view==='raw'&&b.dataset.v==='list')hs.lines=parseHosts($('#hsraw').value);hs.view=b.dataset.v;if(hs.view==='raw')$('#hsraw').value=serializeHosts(hs.lines).replace(/\r\n/g,'\n');hsRender()});
  $('#hsq').addEventListener('input',debounce(e=>{hs.q=e.target.value.trim().toLowerCase();hsRender()},120));
  $('#hsadd').onclick=()=>{hs.view='list';$$('#hstabs button').forEach(x=>x.classList.toggle('on',x.dataset.v==='list'));hs.lines.push({t:'entry',on:true,ip:'127.0.0.1',hosts:'',c:''});hs.q='';$('#hsq').value='';hsRender();const r=$$('#hsrows input[data-f=hosts]');r[r.length-1]?.focus()};
  $('#hssave').onclick=hsSave;$('#hsrevert').onclick=()=>{hs.lines=parseHosts(hs.orig);hsRender();toast('Changes discarded')};
  $('#hsflush').onclick=async()=>{try{await hsPost('/api/hosts/flush',{});toast('DNS cache flushed')}catch(e){toast(e.message)}};
  $('#hsreload').onclick=()=>hsLoad(true);
  $('#hselev').onclick=async()=>{try{await api('/api/elevate?tool=hosts');document.body.innerHTML='<div style="padding:60px;text-align:center">My Tool Box is restarting as administrator in a new window. You can close this one.</div>';setTimeout(()=>window.close(),800)}catch(e){toast(e.message)}};
  $('#hsraw').addEventListener('input',hsDirty);
  $('#hsrows').addEventListener('input',e=>{const el=e.target,i=+el.closest('tr').dataset.i,f=el.dataset.f,l=hs.lines[i];if(!l)return;
    if(f==='on')l.on=el.checked;else l[f]=el.value;el.closest('tr').classList.toggle('bad',!!hsProblem(l));el.closest('tr').title=hsProblem(l);hsDirty()});
  $('#hsrows').addEventListener('click',e=>{const b=e.target.closest('[data-del]');if(b){hs.lines.splice(+b.dataset.del,1);hsRender()}});
  addEventListener('keydown',e=>{if(curTool==='hosts'&&e.ctrlKey&&e.key==='s'){e.preventDefault();hsSave()}});
  hsLoad()};

const hsText=()=>hs.view==='raw'?$('#hsraw').value:serializeHosts(hs.lines);
const hsNorm=t=>serializeHosts(parseHosts(t));
const hsIsDirty=()=>hsNorm(hsText())!==hsNorm(hs.orig);
function hsDirty(){const d=hsIsDirty();$('#hssave').disabled=!d||!hs.loaded;$('#hsrevert').disabled=!d;$('#s3').textContent=d?'Unsaved changes':''}
const hsPost=(url,body)=>api(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});

async function hsLoad(again){
  try{const r=await api('/api/hosts');hs.orig=r.text;hs.admin=r.admin;hs.path=r.path;hs.lines=parseHosts(r.text);hs.loaded=true;if(again)toast('Reloaded from disk');hsRender()}
  catch(e){$('#hsrows').innerHTML=`<tr><td class="empty" colspan="5">${esc(e.message)}</td></tr>`}}

function hsRender(){
  $('#hsadmin').style.display=hs.admin?'none':'flex';
  $('#hspath').textContent=hs.path;
  $('#hslist').style.display=hs.view==='list'?'':'none';$('#hsraw').style.display=hs.view==='raw'?'':'none';$('#hsq').parentElement.style.display=hs.view==='list'?'':'none';
  const q=hs.q;
  const rows=hs.lines.map((l,i)=>[l,i]).filter(([l])=>l.t==='entry'&&(!q||l.ip.includes(q)||l.hosts.toLowerCase().includes(q)||l.c.toLowerCase().includes(q)));
  $('#hsrows').innerHTML=rows.map(([l,i])=>`<tr data-i="${i}" class="${hsProblem(l)?'bad':''}" title="${esc(hsProblem(l))}"><td><input type="checkbox" data-f="on"${l.on?' checked':''} title="Enabled"></td>`+
    `<td><input class="mono" data-f="ip" value="${esc(l.ip)}" spellcheck="false" style="width:150px"></td><td><input class="mono" data-f="hosts" value="${esc(l.hosts)}" spellcheck="false" placeholder="example.com www.example.com" style="width:100%"></td>`+
    `<td><input data-f="c" value="${esc(l.c)}" spellcheck="false" placeholder="note" style="width:100%"></td><td><button class="ib" data-del="${i}" title="Delete entry"><i class="fi">&#xE74D;</i></button></td></tr>`).join('')
    ||`<tr><td class="empty" colspan="5">${hs.loaded?(q?'Nothing matches the filter':'No entries yet. Click Add entry.'):'Reading hosts file…'}</td></tr>`;
  const n=hs.lines.filter(l=>l.t==='entry');$('#hscount').textContent=`${n.filter(l=>l.on).length} active · ${n.filter(l=>!l.on).length} disabled`;
  hsDirty()}

async function hsSave(){
  const bad=hs.view==='list'?hs.lines.find(l=>hsProblem(l)):null;if(bad)return toast(`Fix first: ${bad.ip} ${bad.hosts} — ${hsProblem(bad)}`);
  const text=hsText();
  try{const r=await hsPost('/api/hosts/save',{text});hs.orig=text;if(hs.view==='raw')hs.lines=parseHosts(text);
    toast(r.backup?'Saved. Backup: '+r.backup.split('\\').pop():'Saved');hsRender();hsPost('/api/hosts/flush',{}).catch(()=>{})}
  catch(e){toast(e.message);if(/administrator/i.test(e.message)){hs.admin=false;hsRender()}}}
