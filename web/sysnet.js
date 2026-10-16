// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// DNS lookup, Traceroute and Network adapters. Shares the helpers from index.html / tools.js.
"use strict";
Object.assign(TOOL_NAMES,{dns:'DNS lookup',trace:'Traceroute',adapters:'Network adapters'});

/* ================= DNS lookup ================= */
toolInit.dns=()=>{
  $('#dngo').onclick=dnsRun;
  $('#dnname').addEventListener('keydown',e=>{if(e.key==='Enter')dnsRun()});
  $('#dncustom').addEventListener('keydown',e=>{if(e.key==='Enter')dnsRun()});
  $('#dnsrv').onchange=()=>{$('#dncustomw').style.display=$('#dnsrv').value==='custom'?'':'none'};
  $('#dntype').onchange=()=>{const n=$('#dnname').value.trim();if($('#dntype').value==='PTR'&&!ip2u(n))$('#dnerr').textContent='PTR looks up a name for an IP address, so enter an IP.';else $('#dnerr').textContent=''};
  dnsRun()};
async function dnsRun(){
  const name=$('#dnname').value.trim(),type=$('#dntype').value;let server=$('#dnsrv').value;if(server==='custom')server=$('#dncustom').value.trim();
  $('#dnerr').textContent='';if(!name)return $('#dnerr').textContent='Enter a name or an IP address';
  $('#dngo').disabled=true;$('#dnout').innerHTML='<div class="card"><div class="empty">Looking up…</div></div>';
  try{const r=await api(`/api/net/dns?name=${encodeURIComponent(name)}&type=${type}&server=${encodeURIComponent(server)}`);
    const found=r.results.reduce((a,x)=>a+x.records.length,0);
    $('#dnout').innerHTML=`<div class="hint" style="margin:0 4px 8px">${esc(r.name)} · ${server?'via '+esc(server):'system DNS'} · ${found} record${found===1?'':'s'}</div>`+
      r.results.map(x=>`<div class="card flush"><div class="chhead" style="padding:10px 16px 0"><h3>${esc(x.type)}</h3><span class="hint" style="margin:0">${x.ms} ms</span></div>`+
        (x.records.length?`<div class="tblwrap"><table class="tbl hov"><tbody>${x.records.map(c=>`<tr><td class="mono" style="word-break:break-all">${esc(c.value)}</td><td class="pp">${esc(c.extra||'')}</td><td style="width:36px"><button class="ib" title="Copy" data-copytext="${esc(c.value)}">${CP}</button></td></tr>`).join('')}</tbody></table></div>`
          :`<div class="empty" style="padding:12px 16px">${esc(x.error||'No records')}</div>`)+'</div>').join('')}
  catch(e){$('#dnout').innerHTML='';$('#dnerr').textContent=e.message}
  $('#dngo').disabled=false}

/* ================= Traceroute ================= */
const tr={running:false,hops:[],run:0};
toolInit.trace=()=>{
  $('#trgo').onclick=traceStart;$('#trstop').onclick=()=>{tr.running=false};
  $('#trhost').addEventListener('keydown',e=>{if(e.key==='Enter'&&!tr.running)traceStart()});
  $('#trcopy').onclick=()=>{if(!tr.hops.length)return toast('Nothing to copy');copyText(tr.hops.map(traceLine).join('\n'))};
  traceRender()};
const traceMs=p=>p.from?(p.ms<1?'<1 ms':p.ms+' ms'):'*';
function traceLine(h){return`${String(h.ttl).padStart(2)}  ${h.probes.map(traceMs).join('  ')}  ${h.name?h.name+' ['+h.hop+']':h.hop||'Request timed out'}`}
async function traceStart(){
  const host=$('#trhost').value.trim();if(!host)return toast('Enter a host');
  const run=++tr.run,max=+$('#trmax').value,to=+$('#trto').value,names=$('#trnames').checked?1:0;
  tr.hops=[];tr.running=true;tr.target=host;tr.ip='';tr.error='';tr.done=false;traceRender();
  let silent=0;
  for(let ttl=1;ttl<=max&&tr.running&&run===tr.run;ttl++){
    try{const h=await api(`/api/net/hop?host=${encodeURIComponent(host)}&ttl=${ttl}&timeout=${to}&names=${names}`);
      if(run!==tr.run)return;tr.ip=h.ip;tr.hops.push(h);traceRender();
      silent=h.hop?0:silent+1;if(h.done){tr.done=true;break}
      if(silent>=8){tr.error='Eight hops in a row did not answer, so the trace was stopped.';break}}
    catch(e){tr.error=e.message;break}}
  if(run===tr.run){tr.running=false;traceRender()}}
function traceRender(){
  $('#trgo').style.display=tr.running?'none':'';$('#trstop').style.display=tr.running?'':'none';
  const last=tr.hops[tr.hops.length-1],ms=tr.hops.flatMap(h=>h.probes).filter(p=>p.from).map(p=>p.ms);
  $('#trtiles').innerHTML=[['Target',tr.ip?`${esc(tr.target)}${tr.ip!==tr.target?' ('+tr.ip+')':''}`:'—'],['Hops',tr.hops.length||'—'],['Result',tr.done?'<span class="st ok">Reached</span>':tr.running?'Tracing…':tr.hops.length?'<span class="st mut">Not reached</span>':'—'],
    ['Last hop',last&&last.probes.some(p=>p.from)?fms(Math.min(...last.probes.filter(p=>p.from).map(p=>p.ms))):'—']].map(([k,v])=>`<div class="tile"><small style="color:var(--muted)">${k}</small><div style="font-size:18px;font-weight:600;overflow-wrap:anywhere">${v}</div></div>`).join('');
  $('#trtbl').innerHTML=`<thead><tr><th class="r">Hop</th><th class="r">Probe 1</th><th class="r">Probe 2</th><th class="r">Probe 3</th><th>Router</th></tr></thead><tbody>`+
    (tr.hops.length?tr.hops.map(h=>`<tr><td class="r mono">${h.ttl}</td>${h.probes.map(p=>`<td class="r mono">${p.from?(p.ms<1?'&lt;1 ms':p.ms+' ms'):'<span class="pp">*</span>'}</td>`).join('')}<td>${h.hop?`${h.name?`<b>${esc(h.name)}</b> `:''}<span class="mono pp">${h.name?'['+h.hop+']':h.hop}</span>`:'<span class="pp">Request timed out</span>'}</td></tr>`).join(''):'<tr><td class="empty" colspan="5">Press Trace to follow the route to the host.</td></tr>')+'</tbody>';
  if(tr.error)$('#trtbl').insertAdjacentHTML('beforeend',`<tbody><tr><td colspan="5" class="err">${esc(tr.error)}</td></tr></tbody>`)}

/* ================= Network adapters ================= */
const ad={list:[],q:'',onlyUp:true};
toolInit.adapters=()=>{
  $('#adref').onclick=adLoad;$('#adup').onchange=e=>{ad.onlyUp=e.target.checked;adRender()};
  $('#adq').addEventListener('input',debounce(e=>{ad.q=e.target.value.trim().toLowerCase();adRender()},120));
  $('#adcopy').onclick=()=>{const t=adFiltered().map(adText).join('\n\n');if(t)copyText(t);else toast('Nothing to copy')};
  addEventListener('keydown',e=>{if(e.key==='F5'&&curTool==='adapters'){e.preventDefault();adLoad()}});
  adLoad()};
async function adLoad(){$('#s3').textContent='Reading adapters…';
  try{ad.list=(await api('/api/net/adapters')).adapters;adRender();$('#s3').textContent=`${ad.list.length} adapters · read ${new Date().toLocaleTimeString()}`}
  catch(e){$('#adbody').innerHTML=`<div class="card"><div class="err">${esc(e.message)}</div></div>`}}
const adSpeed=b=>!b?'—':b>=1e9?(b/1e9).toFixed(b%1e9?1:0).replace(/\.0$/,'')+' Gbps':Math.round(b/1e6)+' Mbps';
const adMask=p=>u2ip(maskOf(p));
function adFiltered(){return ad.list.filter(a=>(!ad.onlyUp||a.status==='Up'&&a.type!=='Loopback')&&(!ad.q||[a.name,a.description,a.mac,...a.addrs.map(x=>x.ip)].some(s=>String(s||'').toLowerCase().includes(ad.q))))}
function adText(a){const v4=a.addrs.filter(x=>!x.v6),v6=a.addrs.filter(x=>x.v6);
  return[`${a.name} (${a.description})`,`  Status: ${a.status}  Type: ${a.type}  Speed: ${adSpeed(a.speed)}  MTU: ${a.mtu}`,`  MAC: ${a.mac||'—'}`,...v4.map(x=>`  IPv4: ${x.ip}/${x.prefix} (${adMask(x.prefix)})`),...v6.map(x=>`  IPv6: ${x.ip}/${x.prefix}`),
    `  Gateway: ${a.gateways.join(', ')||'—'}`,`  DNS: ${a.dns.join(', ')||'—'}`,`  DHCP: ${a.dhcp?'on'+(a.dhcpServer?' ('+a.dhcpServer+')':''):'off'}`].join('\n')}
function adRender(){const L=adFiltered();
  if(!L.length){$('#adbody').innerHTML='<div class="card"><div class="empty">No adapters match.</div></div>';return}
  $('#adbody').innerHTML=L.map(a=>{const v4=a.addrs.filter(x=>!x.v6),v6=a.addrs.filter(x=>x.v6&&!/^fe80/i.test(x.ip)),ll=a.addrs.filter(x=>x.v6&&/^fe80/i.test(x.ip));
    const row=(k,v,copy)=>v?`<div><span class="k">${k}</span><span class="v">${v}${copy?` <button class="ib" title="Copy" data-copytext="${esc(copy)}">${CP}</button>`:''}</span></div>`:'';
    return`<div class="card"><div class="dkhead"><h2>${esc(a.name)}</h2><span>${esc(a.description)}</span><span class="st ${a.status==='Up'?'ok':'mut'}">${esc(a.status)}</span></div>
    <div class="dkkv">${row('Type',esc(a.type))}${row('Speed',adSpeed(a.speed))}${row('MAC address',esc(a.mac),a.mac)}${row('MTU',String(a.mtu||'—'))}
      ${v4.map(x=>row('IPv4 address',`${esc(x.ip)}/${x.prefix}`,x.ip)+row('Subnet mask',adMask(x.prefix),adMask(x.prefix))).join('')}
      ${row('Default gateway',a.gateways.map(esc).join(', '),a.gateways[0])}${row('DNS servers',a.dns.map(esc).join(', '),a.dns.join(', '))}
      ${row('DHCP',a.dhcp?'Enabled'+(a.dhcpServer?` (${esc(a.dhcpServer)})`:''):'Static (off)')}${row('DNS suffix',esc(a.dnsSuffix))}
      ${v6.map(x=>row('IPv6 address',`${esc(x.ip)}/${x.prefix}`,x.ip)).join('')}${ll.slice(0,1).map(x=>row('IPv6 link-local',esc(x.ip),x.ip)).join('')}${row('Interface index',String(a.index))}</div></div>`}).join('')}
