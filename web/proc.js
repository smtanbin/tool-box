// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Process monitor: processes with CPU / memory, open ports with their owner, and kill. Shares the helpers from index.html / tools.js.
"use strict";
TOOL_NAMES.proc='Process monitor';

const pm={view:'procs',procs:[],ports:[],sel:null,q:'',sort:{procs:{k:'cpu',d:-1},ports:{k:'port',d:1}},timer:0,busy:false,admin:true,self:0,info:{}};

toolInit.proc=()=>{
  segInit('#pmtabs',b=>{pm.view=b.dataset.v;pmRender();pmLoad()});
  $('#pmq').addEventListener('input',debounce(e=>{pm.q=e.target.value.trim().toLowerCase();pmRender()},120));
  $('#pmint').onchange=pmSchedule;
  $('#pmkill').onclick=()=>pmKill(false);$('#pmtree').onclick=()=>pmKill(true);
  $('#pmkpgo').onclick=pmKillPort;$('#pmkp').addEventListener('keydown',e=>{if(e.key==='Enter')pmKillPort()});
  $('#pmtbl').addEventListener('click',e=>{
    const th=e.target.closest('th[data-k]');
    if(th){const s=pm.sort[pm.view];s.d=s.k===th.dataset.k?-s.d:(['name','path','local','proto','remote','state'].includes(th.dataset.k)?1:-1);s.k=th.dataset.k;pmRender();return}
    const kb=e.target.closest('[data-kill]');
    if(kb){pmKill(false,+kb.dataset.kill);return}
    const tr=e.target.closest('tr[data-pid]');if(tr){pm.sel=+tr.dataset.pid;pmRender()}});
  addEventListener('keydown',e=>{if(curTool==='proc'&&e.key==='Delete'&&pm.sel!==null&&!/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName))pmKill(e.shiftKey)});
  document.addEventListener('visibilitychange',()=>{if(!document.hidden&&curTool==='proc')pmLoad()});
  pmLoad();pmSchedule()};

function pmSchedule(){clearInterval(pm.timer);const ms=+$('#pmint').value;if(ms)pm.timer=setInterval(()=>{if(curTool==='proc'&&!document.hidden)pmLoad()},ms)}

async function pmLoad(){if(pm.busy)return;pm.busy=true;
  try{
    const r=await api('/api/proc/list');pm.procs=r.procs;pm.info=r;pm.admin=r.admin;pm.self=r.self;
    if(pm.view==='ports'){pm.ports=(await api('/api/proc/ports')).ports}
    pmRender()}
  catch(e){$('#pmtbl').innerHTML=`<tbody><tr><td class="empty">${esc(e.message)}</td></tr></tbody>`}
  pm.busy=false}

function pmCmp(k,d){return(a,b)=>{const x=a[k],y=b[k];const r=typeof x==='string'||typeof y==='string'?String(x??'').localeCompare(String(y??''),undefined,{sensitivity:'base'}):(x??0)-(y??0);return r*d||a.pid-b.pid}}
const pmSel=()=>pm.procs.find(p=>p.pid===pm.sel);
const pmTh=(s,k,l,r)=>`<th data-k="${k}"${r?' class="r"':''}>${l}${s.k===k?(s.d>0?' ▲':' ▼'):''}</th>`;
const pmKillBtn=(p,title)=>p.locked?'<span class="pp" title="Protected system process">protected</span>':`<button class="ib" title="${title}" data-kill="${p.pid}"><i class="fi">&#xE711;</i></button>`;

function pmRender(){
  const I=pm.info,s=pm.sort[pm.view],q=pm.q,ports=pm.ports,listen=ports.filter(p=>p.state==='LISTEN').length;
  const selP=pmSel();
  $('#pmkill').disabled=$('#pmtree').disabled=!selP||selP.locked;
  $('#s3').textContent=selP?`Selected: ${selP.name} (PID ${selP.pid})`:'';
  $('#pmtiles').innerHTML=[['Processes',I.procs?I.procs.length:'—'],['CPU',I.cpu!=null?I.cpu.toFixed(0)+' %':'—'],
    ['Memory',I.memTotal?`${fmt(I.memUsed)} of ${fmt(I.memTotal)}`:'—'],pm.view==='ports'?['Listening ports',listen]:['Logical CPUs',I.cpus||'—']].map(([k,v])=>`<div class="tile"><small style="color:var(--muted)">${k}</small><div style="font-size:20px;font-weight:600">${v}</div></div>`).join('');
  let rows,html;
  if(pm.view==='procs'){
    rows=pm.procs.filter(p=>!q||p.name.toLowerCase().includes(q)||String(p.pid)===q||(p.path||'').toLowerCase().includes(q)||(p.ports||[]).some(x=>String(x)===q)).sort(pmCmp(s.k,s.d));
    html=`<thead><tr>${pmTh(s,'name','Name')}${pmTh(s,'pid','PID',1)}${pmTh(s,'cpu','CPU %',1)}${pmTh(s,'mem','Memory',1)}${pmTh(s,'threads','Threads',1)}${pmTh(s,'ppid','Parent',1)}<th>Listening ports</th>${pmTh(s,'path','Path')}<th></th></tr></thead><tbody>`+
      rows.map(p=>`<tr data-pid="${p.pid}" class="${p.pid===pm.sel?'sel':''}"><td class="nw"><b>${esc(p.name)}</b></td><td class="r mono">${p.pid}</td><td class="r nw"><span class="cpubar"><i style="width:${Math.min(100,p.cpu*3)}%"></i></span>${p.cpu.toFixed(1)}</td><td class="r nw">${fmt(p.mem)}</td><td class="r">${p.threads}</td><td class="r mono pp">${p.ppid}</td>`+
        `<td class="mono">${(p.ports||[]).slice().sort((a,b)=>a-b).slice(0,6).join(', ')}${(p.ports||[]).length>6?' …':''}</td><td class="pp" title="${esc(p.path||'')}" style="max-width:340px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(p.path||'')}</td>`+
        `<td>${pmKillBtn(p,'End process')}</td></tr>`).join('')+'</tbody>';
  }else{
    const names=new Map(pm.procs.map(p=>[p.pid,p.name]));
    rows=ports.map(p=>({...p,name:p.name||names.get(p.pid)||''})).filter(p=>!q||String(p.port)===q||p.name.toLowerCase().includes(q)||String(p.pid)===q||p.local.includes(q)||p.state.toLowerCase().includes(q)||p.proto.toLowerCase().includes(q)).sort(pmCmp(s.k,s.d));
    html=`<thead><tr>${pmTh(s,'proto','Proto')}${pmTh(s,'local','Local address')}${pmTh(s,'port','Port',1)}${pmTh(s,'remote','Remote')}${pmTh(s,'state','State')}${pmTh(s,'pid','PID',1)}${pmTh(s,'name','Process')}<th></th></tr></thead><tbody>`+
      rows.map(p=>`<tr data-pid="${p.pid}" class="${p.pid===pm.sel?'sel':''}"><td>${p.proto}</td><td class="mono">${esc(p.local)}</td><td class="r mono"><b>${p.port}</b></td><td class="mono">${p.remote?esc(p.remote)+':'+p.rport:'—'}</td>`+
        `<td>${p.state==='LISTEN'?'<span class="st ok">Listening</span>':p.state==='ESTABLISHED'?'<span class="st">Established</span>':esc(p.state||'—')}</td><td class="r mono">${p.pid}</td><td><b>${esc(p.name||'—')}</b></td>`+
        `<td>${pmKillBtn(p,'End the process using this port')}</td></tr>`).join('')+'</tbody>';
  }
  if(!rows.length)html+='<tbody><tr><td class="empty" colspan="9">Nothing matches the filter</td></tr></tbody>';
  $('#pmtbl').innerHTML=html}

function pmReport(res){const bad=res.filter(r=>!r.ok);
  if(!bad.length)toast(res.length===1?`Ended ${res[0].name||'process'} (PID ${res[0].pid})`:`Ended ${res.length} processes`);
  else dialog(`<h3 style="margin:0 0 8px">${res.length-bad.length} ended, ${bad.length} could not be ended</h3>`+bad.map(r=>`<div style="padding:4px 0"><b>${esc(r.name||'PID '+r.pid)}</b> (PID ${r.pid}): ${esc(r.error)}</div>`).join('')+(pm.admin?'':'<p class="hint">Tip: some processes can only be ended from an administrator copy of My Tool Box (Disk info has a Restart as administrator button).</p>'));
  pmLoad()}

const pmPost=(url,body)=>api(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});

async function pmKill(tree,pid){pid=pid??pm.sel;const p=pm.procs.find(x=>x.pid===pid);if(!p)return;
  if(p.locked)return toast('That is a protected system process');
  if(!confirm(`End ${p.name} (PID ${p.pid})${tree?' and everything it started':''}?\n\nUnsaved work in that program will be lost.`))return;
  try{pmReport(await pmPost('/api/proc/kill',{pids:[pid],tree}));if(pm.sel===pid)pm.sel=null}catch(e){toast(e.message)}}

async function pmKillPort(){const port=+$('#pmkp').value;if(!(port>=1&&port<=65535))return toast('Enter a port from 1 to 65535');
  let owners;
  try{owners=(await api('/api/proc/ports')).ports.filter(p=>p.port===port&&(p.state==='LISTEN'||p.state==='ESTABLISHED'||p.proto.startsWith('UDP')))}catch(e){return toast(e.message)}
  if(!owners.length)return toast(`Nothing is using port ${port}`);
  const uniq=[...new Map(owners.map(o=>[o.pid,o])).values()];
  if(!confirm(`Port ${port} is used by:\n\n${uniq.map(o=>`  ${o.name||'?'} (PID ${o.pid})`).join('\n')}\n\nEnd ${uniq.length>1?'these processes':'this process'}?`))return;
  try{pmReport(await pmPost('/api/proc/killport',{port,tree:false}));$('#pmkp').value=''}catch(e){toast(e.message)}}
