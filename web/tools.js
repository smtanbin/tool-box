// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Disk, network, crypto and developer tool pages. Loaded after the main script in index.html and shares its helpers ($, $$, api, esc, toast, flyout).
"use strict";

/* ---------- tool switching ---------- */
const TOOL_NAMES={foldersize:'Folder Size',disk:'Disk info',subnet:'Subnet calculator',ipscan:'IP scanner',ping:'Ping',telnet:'Telnet / ports',hash:'Hash text',crypt:'Encrypt / decrypt',
  token:'Token & UUID',encode:'Base64 & encoders',jwt:'JWT decoder',json:'JSON formatter',time:'Timestamp converter',base:'Number base'};
const toolInit={},toolInited=new Set();
function showTool(t){
  if(!TOOL_NAMES[t]||!document.getElementById('t-'+t))t='foldersize';
  curTool=t;
  $$('.tool').forEach(s=>s.classList.toggle('on',s.id==='t-'+t));
  $$('#rail .ri[data-tool]').forEach(a=>a.classList.toggle('on',a.dataset.tool===t));
  document.body.dataset.tool=t;$('#s3').textContent='';
  document.title=t==='foldersize'?'My Tool Box':TOOL_NAMES[t]+' — My Tool Box';
  try{localStorage.setItem('tool',t)}catch(e){}
  if(location.hash.slice(1)!==t)history.replaceState(null,'','#'+t);
  if(toolInit[t]&&!toolInited.has(t)){toolInited.add(t);toolInit[t]()}
  if(t==='ping')drawPing();
}
addEventListener('hashchange',()=>showTool(location.hash.slice(1)));

/* ---------- shared helpers ---------- */
const enc=new TextEncoder(),utf8=new TextDecoder('utf-8',{fatal:true});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const toHex=b=>Array.from(b,x=>x.toString(16).padStart(2,'0')).join('');
function b64(bytes){let s='';for(let i=0;i<bytes.length;i+=0x8000)s+=String.fromCharCode.apply(null,bytes.subarray(i,i+0x8000));return btoa(s)}
function unb64(s){s=s.replace(/\s+/g,'').replace(/-/g,'+').replace(/_/g,'/');if(s.length%4===1)throw new Error('Not valid Base64');while(s.length%4)s+='=';
  let bin;try{bin=atob(s)}catch(e){throw new Error('Not valid Base64')}const out=new Uint8Array(bin.length);for(let i=0;i<bin.length;i++)out[i]=bin.charCodeAt(i);return out}
function fromUTF8(b){try{return utf8.decode(b)}catch(e){throw new Error('The decoded bytes are not valid UTF-8 text')}}
function concat(...arrs){const out=new Uint8Array(arrs.reduce((a,x)=>a+x.length,0));let o=0;for(const a of arrs){out.set(a,o);o+=a.length}return out}
const rand=n=>crypto.getRandomValues(new Uint8Array(n));
function randInt(n){const lim=Math.floor(0x100000000/n)*n,u=new Uint32Array(1);do crypto.getRandomValues(u);while(u[0]>=lim);return u[0]%n}
async function copyText(t){try{await navigator.clipboard.writeText(t);toast('Copied to clipboard')}catch(e){toast('Could not copy')}}
document.addEventListener('click',e=>{const b=e.target.closest('[data-copy],[data-copytext]');if(!b)return;
  let t=b.dataset.copytext;if(t===undefined){const el=$(b.dataset.copy);t=el?(el.value!==undefined?el.value:el.textContent):''}
  if(t)copyText(t);else toast('Nothing to copy')});
const CP='<i class="fi">&#xE8C8;</i>';
// rows: [label, value(text), optional display HTML]
function kvHTML(rows){return rows.map(([k,v,h])=>`<div class="k">${esc(k)}</div><div class="v">${h??esc(v)}</div><div>${v!==''&&v!=null?`<button class="ib" title="Copy" data-copytext="${esc(v)}">${CP}</button>`:''}</div>`).join('')}
function downloadText(name,text,type){const a=document.createElement('a');a.href=URL.createObjectURL(new Blob([text],{type}));a.download=name;document.body.appendChild(a);a.click();setTimeout(()=>{URL.revokeObjectURL(a.href);a.remove()},1000)}
const csvCell=v=>/[",\n]/.test(v=String(v??''))?'"'+v.replace(/"/g,'""')+'"':v;
function segInit(sel,cb){$(sel).addEventListener('click',e=>{const b=e.target.closest('button');if(!b)return;$$(sel+' button').forEach(x=>x.classList.toggle('on',x===b));cb(b)})}
function cssColor(v){const p=document.createElement('div');p.style.cssText='position:absolute;visibility:hidden;color:var('+v+')';document.body.appendChild(p);const c=getComputedStyle(p).color;p.remove();return c}
const withAlpha=(rgb,a)=>rgb.replace(/^rgba?\(([^,]+),([^,]+),([^,)]+).*$/,`rgba($1,$2,$3,${a})`);
function debounce(fn,ms){let h;return(...a)=>{clearTimeout(h);h=setTimeout(()=>fn(...a),ms)}}
const fms=ms=>ms==null?'—':ms<1?'<1 ms':(ms<10?ms.toFixed(1).replace(/\.0$/,''):Math.round(ms))+' ms';

/* ---------- IPv4 helpers ---------- */
const ip2u=s=>{const m=String(s).trim().match(/^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/);if(!m)return null;const o=m.slice(1).map(Number);if(o.some(x=>x>255))return null;return((o[0]<<24)|(o[1]<<16)|(o[2]<<8)|o[3])>>>0};
const u2ip=u=>[u>>>24,u>>>16&255,u>>>8&255,u&255].join('.');
const maskOf=p=>p===0?0:(0xffffffff<<(32-p))>>>0;
function prefixOf(mask){const inv=(~mask)>>>0;if(((inv+1)&inv)!==0)return null;let c=0;for(let x=inv;x;x>>>=1)c+=x&1;return 32-c}

/* ================= Disk info ================= */
let dk={disks:[],sel:0,hex:true,admin:true,loading:false};
toolInit.disk=()=>{
  $('#dkref').onclick=diskLoad;
  $('#dkraw').onclick=()=>{dk.hex=!dk.hex;$('#dkraw').innerHTML=`<i class="fi">&#xE8EF;</i>Raw: ${dk.hex?'hex':'decimal'}`;diskRender()};
  $('#dkpills').addEventListener('click',e=>{const b=e.target.closest('[data-i]');if(b){dk.sel=+b.dataset.i;diskRender()}});
  $('#dkelev').onclick=async()=>{try{await api('/api/elevate');document.body.innerHTML='<div style="padding:60px;text-align:center">My Tool Box is restarting as administrator in a new window. You can close this one.</div>';setTimeout(()=>window.close(),800)}catch(e){toast(e.message)}};
  addEventListener('keydown',e=>{if(e.key==='F5'&&curTool==='disk'){e.preventDefault();diskLoad()}});
  diskLoad()};
async function diskLoad(){if(dk.loading)return;dk.loading=true;$('#dkref').disabled=true;$('#s3').textContent='Reading drives…';
  try{const r=await api('/api/disks');dk.disks=r.disks;dk.admin=r.admin;if(dk.sel>=dk.disks.length)dk.sel=0;diskRender();$('#s3').textContent=`${dk.disks.length} drive${dk.disks.length===1?'':'s'} · read ${new Date().toLocaleTimeString()}${r.admin?' · administrator':''}`}
  catch(e){$('#dkbody').innerHTML=`<div class="card"><div class="err">${esc(e.message)}</div></div>`}
  dk.loading=false;$('#dkref').disabled=false}
const tempClass=t=>!t?'Unknown':t>=60?'Bad':t>=50?'Caution':'Good';
function diskRender(){const D=dk.disks;
  if(!D.length){$('#dkpills').innerHTML='';$('#dkbody').innerHTML='<div class="card"><div class="empty">No physical drives found.</div></div>';return}
  $('#dkpills').innerHTML=D.map((d,i)=>`<button class="dkpill${i===dk.sel?' on':''}" data-i="${i}" title="${esc(d.model)}"><b><span class="hdot h-${d.health}"></span>Disk ${d.index}${d.temp?` · ${d.temp} °C`:''}</b><small>${esc(d.letters.join(' ')||'no drive letter')} · ${fmt(d.size)}</small></button>`).join('');
  const d=D[dk.sel];$('#dkadmin').style.display=!dk.admin&&d.needAdmin&&!d.attrs.length?'flex':'none';
  const hlabel={Good:'Good',Caution:'Caution',Bad:'Bad',Unknown:'Unknown'}[d.health],tc=tempClass(d.temp);
  const kv=[['Firmware',d.firmware],['Serial number',d.serial],['Interface',d.bus],['Media type',d.media==='SSD'?'SSD (solid state)':d.media==='HDD'?'HDD (spinning disk)':d.media||'—'],
    ['Capacity',d.size?`${fmt(d.size)} · ${(d.size/1e9).toFixed(1)} GB (decimal)`:'—'],['Partition style',d.style||'—'],['Drive letters',d.letters.join(' ')||'—'],
    ['Power-on count',d.powerCount?d.powerCount.toLocaleString()+' times':'—'],['Power-on hours',d.powerHours?`${d.powerHours.toLocaleString()} h (${(d.powerHours/24).toFixed(0)} days)`:'—'],
    ['Host reads',d.hostReads?fmt(d.hostReads):'—'],['Host writes',d.hostWrites?fmt(d.hostWrites):'—'],['Health source',d.source||'Not available']];
  kv.splice(3,0,['Manufacturer',d.maker],['Controller',d.controller||(d.controllerVendor?d.controllerVendor+' (model unknown)':'')],['Controller maker',d.controllerVendor]);
  if(d.hmbBytes)kv.push(['Host Memory Buffer',fmt(d.hmbBytes)+' requested']);
  if(d.removable)kv.splice(3,0,['Removable','Yes']);
  const dramLbl={dram:'DRAM cache','dram-likely':'Probably DRAM cache',dramless:'DRAM-less','dramless-likely':'Probably DRAM-less',unknown:'DRAM unknown'};
  const dramHTML=dramLbl[d.dram]?`<div class="dramrow"><span class="dbadge ${d.dram}">${dramLbl[d.dram]}</span> <span class="hint" style="margin:0">${esc(d.dramNote)}</span></div>`:(d.dramNote?`<p class="hint">${esc(d.dramNote)}</p>`:'');
  let html=`<div class="card"><div class="dktop"><div class="dkbadges">
      <div class="hbadge h-${d.health}" title="${esc(d.source||'')}"><small>Health status</small><b>${hlabel}</b><span>${d.healthPct>0?d.healthPct+' %':'&nbsp;'}</span></div>
      <div class="hbadge h-${tc}"><small>Temperature</small><b>${d.temp?d.temp+' °C':'— °C'}</b><span>${d.temp?Math.round(d.temp*9/5+32)+' °F':(d.needAdmin&&!dk.admin?'needs administrator':'not reported')}</span></div></div>
    <div><div class="dkhead"><h2>${esc(d.model||'Unknown drive')}</h2><span>${fmt(d.size)} · Disk ${d.index}</span></div>
      ${dramHTML}
      <div class="dkkv">${kv.map(([k,v])=>`<div><span class="k">${k}</span><span class="v">${esc(v||'—')}</span></div>`).join('')}</div>
      ${d.reasons.length?`<div class="reasons h-${d.health}">${d.reasons.map(esc).join('<br>')}</div>`:''}
      ${d.notes.length?`<p class="hint">${d.notes.map(esc).join('<br>')}</p>`:''}</div></div></div>`;
  if(d.attrs.length){const nvme=d.attrs[0].cur<0;
    html+=`<div class="card flush"><div class="chhead" style="padding:12px 16px 0"><h3>S.M.A.R.T. attributes</h3><span class="hint" style="margin:0">${esc(d.source)}</span></div><div class="tblwrap"><table class="tbl hov"><thead><tr><th></th><th>ID</th><th>Attribute name</th>${nvme?'':'<th class="r">Current</th><th class="r">Worst</th><th class="r">Threshold</th>'}<th class="r">Raw value</th></tr></thead><tbody>`+
      d.attrs.map(a=>`<tr class="${a.status==='ok'?'':a.status}"><td><span class="sdot ${a.status}" title="${a.status==='ok'?'Good':a.status==='bad'?'Bad':'Caution'}"></span></td><td class="mono">${a.id.toString(16).toUpperCase().padStart(2,'0')}</td><td>${esc(a.name)}</td>${nvme?'':`<td class="r">${a.cur}</td><td class="r">${a.worst}</td><td class="r">${a.thr}</td>`}<td class="r mono">${dk.hex?a.rawHex:Number(a.raw).toLocaleString()}</td></tr>`).join('')+'</tbody></table></div></div>'}
  else html+=`<div class="card"><h3>S.M.A.R.T. attributes</h3><div class="empty" style="padding:16px">${!dk.admin&&d.needAdmin?'Windows only shares SMART attributes for this drive with administrators. Use <b>Restart as administrator</b> above.':'This drive did not report SMART attributes.'}</div></div>`;
  if(d.parts.length)html+=`<div class="card flush"><div class="chhead" style="padding:12px 16px 0"><h3>Partitions</h3><span class="hint" style="margin:0">${esc(d.style||'')}</span></div><div class="tblwrap"><table class="tbl hov"><thead><tr><th>#</th><th>Drive</th><th>Label</th><th>Type</th><th>File system</th><th class="r">Size</th><th>Used</th><th class="r">Free</th></tr></thead><tbody>`+
    d.parts.map(p=>{const used=p.letter&&p.size?(p.size-p.free)/p.size:0;return`<tr><td>${p.num}</td><td><b>${esc(p.letter)||'—'}</b></td><td>${esc(p.label||p.name)||'—'}</td><td>${esc(p.type)}</td><td>${esc(p.fs)||'—'}</td><td class="r">${fmt(p.size)}</td><td>${p.letter?`<span class="ubar"><i style="width:${(used*100).toFixed(1)}%"></i></span>${Math.round(used*100)}%`:'—'}</td><td class="r">${p.letter?fmt(p.free):'—'}</td></tr>`}).join('')+'</tbody></table></div></div>';
  $('#dkbody').innerHTML=html}

/* ================= Subnet calculator ================= */
toolInit.subnet=()=>{
  const sel=$('#snmask');
  sel.innerHTML=Array.from({length:33},(_,i)=>32-i).map(p=>`<option value="${p}">/${p} — ${u2ip(maskOf(p))}</option>`).join('');
  sel.value='24';
  $('#snip').addEventListener('input',subnetCalc);
  sel.onchange=()=>{const ip=$('#snip').value.trim().split(/[\s/]+/)[0];$('#snip').value=ip+'/'+sel.value;subnetCalc()};
  $('#snsplit').onchange=subnetSplit;
  $('#sncsv').onclick=()=>{const r=subnetRows(100000);if(!r.length)return toast('Nothing to copy');copyText(['Network,First host,Last host,Broadcast,Hosts',...r.map(x=>[x.cidr,x.first,x.last,x.bc,x.hosts].join(','))].join('\n'))};
  subnetCalc();
};
let snCur=null;
function ipType(u){const inR=(b,p)=>((u&maskOf(p))>>>0)===ip2u(b);
  if(inR('10.0.0.0',8)||inR('172.16.0.0',12)||inR('192.168.0.0',16))return'Private (RFC 1918)';
  if(inR('127.0.0.0',8))return'Loopback';if(inR('169.254.0.0',16))return'Link-local (APIPA)';if(inR('100.64.0.0',10))return'Carrier-grade NAT (RFC 6598)';
  if(inR('224.0.0.0',4))return'Multicast';if(u===0xffffffff)return'Limited broadcast';if(inR('240.0.0.0',4))return'Reserved';if(inR('0.0.0.0',8))return'“This network”';
  if(inR('192.0.2.0',24)||inR('198.51.100.0',24)||inR('203.0.113.0',24))return'Documentation (TEST-NET)';return'Public'}
function hostInfo(net,p){const total=2**(32-p),bc=(net+total-1)>>>0;
  if(p===32)return{first:net,last:net,hosts:1,bc};if(p===31)return{first:net,last:bc,hosts:2,bc};return{first:net+1,last:bc-1,hosts:total-2,bc}}
function subnetCalc(){
  const raw=$('#snip').value.trim(),m=raw.match(/^(\S+?)(?:\s*\/\s*(\S+)|\s+(\S+))?$/);
  $('#snerr').textContent='';
  let ip=m?ip2u(m[1]):null,p=+$('#snmask').value;
  if(m&&ip!==null){const x=m[2]??m[3];
    if(x!==undefined){if(/^\d{1,2}$/.test(x)&&+x<=32)p=+x;else{const mk=ip2u(x),pp=mk===null?null:prefixOf(mk);if(pp===null){ip=null;$('#snerr').textContent='The mask must be a prefix like /24 or a valid netmask like 255.255.255.0'}else p=pp}}}
  else if(raw)$('#snerr').textContent='Enter an IPv4 address such as 192.168.1.10/24';
  if(ip===null){snCur=null;$('#snout').innerHTML='';$('#snbin').innerHTML='';$('#sntbl').innerHTML='';$('#sninfo').textContent='';return}
  $('#snmask').value=String(p);
  const mask=maskOf(p),wild=(~mask)>>>0,net=(ip&mask)>>>0,h=hostInfo(net,p),o1=ip>>>24;
  snCur={ip,p,net};
  const cls=o1<128?'A':o1<192?'B':o1<224?'C':o1<240?'D (multicast)':'E (reserved)';
  $('#snout').innerHTML=kvHTML([
    ['Address',u2ip(ip)],['CIDR notation',u2ip(net)+'/'+p],['Netmask',u2ip(mask)],['Wildcard mask',u2ip(wild)],
    ['Network address',u2ip(net)],['Broadcast address',p>=31?'—':u2ip(h.bc)],['First usable host',u2ip(h.first)],['Last usable host',u2ip(h.last)],
    ['Usable hosts',h.hosts.toLocaleString()],['Total addresses',(2**(32-p)).toLocaleString()],['Host range',u2ip(h.first)+' – '+u2ip(h.last)],
    ['IP class',cls],['Address type',ipType(ip)],['Hex',ip.toString(16).toUpperCase().padStart(8,'0').replace(/(..)(?!$)/g,'$1.')],['Integer',String(ip)],
    ['Reverse DNS zone',u2ip(ip).split('.').reverse().join('.')+'.in-addr.arpa']]);
  const bits=u=>{const s=u.toString(2).padStart(32,'0');let out='';for(let i=0;i<32;i++){if(i&&i%8===0)out+='.';out+=i<p?`<b>${s[i]}</b>`:`<i>${s[i]}</i>`}return out};
  $('#snbin').innerHTML=[['Address',ip],['Netmask',mask],['Wildcard',wild],['Network',net],['Broadcast',h.bc]].map(([k,u])=>`<span class="k">${k}</span><span>${bits(u)}</span>`).join('');
  const ss=$('#snsplit'),prev=+ss.value||0;
  ss.innerHTML=p>=32?'<option value="">—</option>':Array.from({length:Math.min(32,p+16)-p},(_,i)=>p+1+i).map(n=>`<option value="${n}">/${n} — ${(2**(n-p)).toLocaleString()} subnets × ${hostInfo(0,n).hosts.toLocaleString()} hosts</option>`).join('');
  ss.disabled=p>=32;ss.value=prev>p&&prev<=Math.min(32,p+16)?String(prev):String(Math.min(32,p+2));
  subnetSplit()}
function subnetRows(limit){if(!snCur||snCur.p>=32)return[];const n=+$('#snsplit').value,size=2**(32-n),count=2**(n-snCur.p),out=[];
  for(let i=0;i<Math.min(count,limit);i++){const net=(snCur.net+i*size)>>>0,h=hostInfo(net,n);
    out.push({cidr:u2ip(net)+'/'+n,first:u2ip(h.first),last:u2ip(h.last),bc:n>=31?'—':u2ip(h.bc),hosts:h.hosts})}return out}
function subnetSplit(){const t=$('#sntbl');if(!snCur||snCur.p>=32){t.innerHTML='';$('#sninfo').textContent=snCur?'A /32 is a single address and cannot be split.':'';return}
  const n=+$('#snsplit').value,count=2**(n-snCur.p),rows=subnetRows(1024);
  $('#sninfo').textContent=count>1024?`Showing the first 1,024 of ${count.toLocaleString()} subnets`:`${count.toLocaleString()} subnets`;
  t.innerHTML='<thead><tr><th>#</th><th>Network</th><th>First host</th><th>Last host</th><th>Broadcast</th><th class="r">Hosts</th></tr></thead><tbody>'+
    rows.map((r,i)=>`<tr><td>${i+1}</td><td class="mono">${r.cidr}</td><td class="mono">${r.first}</td><td class="mono">${r.last}</td><td class="mono">${r.bc}</td><td class="r">${r.hosts.toLocaleString()}</td></tr>`).join('')+'</tbody>'}

/* ================= IP scanner ================= */
let isNets=[],isRes=[],isSort={k:'ip',d:1},isPolling=false;
toolInit.ipscan=async()=>{
  $('#islocal').onclick=e=>{const a=e.currentTarget;if(!isNets.length)return toast('No active IPv4 network found');
    flyout(a,isNets.map(n=>({label:`${n.cidr}   ${n.name} · ${n.ip}`,fn:()=>{$('#isrange').value=n.cidr}})))};
  $('#iscommon').onclick=()=>{$('#isports').value='21,22,23,25,53,80,135,139,443,445,3306,3389,5900,8080'};
  $('#isgo').onclick=ipScanStart;
  $('#isrange').addEventListener('keydown',e=>{if(e.key==='Enter')ipScanStart()});
  $('#isstop').onclick=()=>api('/api/net/scan/cancel').catch(()=>{});
  $('#iscsv').onclick=()=>downloadText('ip-scan.csv','\ufeff'+['IP address,Host name,Latency ms,MAC address,Open ports,Found by',
    ...isRes.map(r=>[r.ip,r.host,r.via==='arp'?'':r.ms,r.mac,r.ports.join(' '),r.via].map(csvCell).join(','))].join('\r\n'),'text/csv');
  $('#istbl thead').addEventListener('click',e=>{const k=e.target.closest('th[data-k]')?.dataset.k;if(!k)return;isSort={k,d:isSort.k===k?-isSort.d:1};ipScanRender()});
  $('#istbl tbody').addEventListener('click',e=>{const b=e.target.closest('button[data-act]');if(!b)return;const ip=b.closest('tr').dataset.ip;
    if(b.dataset.act==='ping'){$('#pghost').value=ip;showTool('ping');pingStart(true)}
    else if(b.dataset.act==='ports'){$('#pthost').value=ip;$('#tnhost').value=ip;showTool('telnet');portTest()}
    else copyText(ip)});
  try{isNets=await api('/api/net/local')}catch(e){}
  if(!$('#isrange').value&&isNets.length)$('#isrange').value=isNets[0].cidr;
  try{const s=await api('/api/net/scan/status');if(s.running||s.results.length){if(s.range)$('#isrange').value=s.range;ipScanPoll()}}catch(e){}
};
async function ipScanStart(){
  const q=new URLSearchParams({range:$('#isrange').value.trim(),ports:$('#isports').value.trim(),timeout:$('#istimeout').value,arp:$('#isarp').checked?'1':'0',dns:$('#isdns').checked?'1':'0'});
  if(!q.get('range'))return toast('Enter a range to scan, e.g. 192.168.1.0/24');
  try{await api('/api/net/scan/start?'+q)}catch(e){return toast(e.message)}
  ipScanPoll()}
async function ipScanPoll(){if(isPolling)return;isPolling=true;
  try{while(true){const s=await api('/api/net/scan/status');isRes=s.results;
    $('#isgo').style.display=s.running?'none':'';$('#isstop').style.display=s.running?'':'none';$('#iscsv').disabled=!isRes.length;
    $('#isprog').style.display='block';const pct=s.total?s.done/s.total*100:0;$('#isbar').style.width=pct+'%';
    $('#isstat').textContent=(s.running?`Scanning ${s.range} … ${s.done.toLocaleString()} / ${s.total.toLocaleString()}`:`${s.cancelled?'Cancelled':'Finished'} — scanned ${s.done.toLocaleString()} of ${s.total.toLocaleString()} addresses in ${s.range}`)+` · ${isRes.length} host${isRes.length===1?'':'s'} found`;
    $('#istime').textContent=s.elapsed.toFixed(1)+' s';$('#s3').textContent=$('#isstat').textContent;
    ipScanRender();if(!s.running)break;await sleep(500)}}catch(e){toast(e.message)}
  isPolling=false}
function ipScanRender(){const tb=$('#istbl tbody');
  if(!isRes.length){tb.innerHTML='<tr><td colspan="7" class="empty">No hosts found yet.</td></tr>';return}
  const {k,d}=isSort,key=r=>k==='ip'?ip2u(r.ip):k==='ms'?(r.via==='arp'?1e9:r.ms):(r[k]||'\uffff').toLowerCase();
  const rows=[...isRes].sort((a,b)=>{const x=key(a),y=key(b);return x<y?-d:x>y?d:0});
  $$('#istbl th[data-k]').forEach(th=>{th.textContent=th.textContent.replace(/ [▲▼]$/,'')+(th.dataset.k===k?(d>0?' ▲':' ▼'):'')});
  tb.innerHTML=rows.map(r=>`<tr data-ip="${esc(r.ip)}"><td class="mono">${esc(r.ip)}</td><td>${esc(r.host)||'<span style="color:var(--muted)">—</span>'}</td><td class="r">${r.via==='arp'?'—':fms(r.ms)}</td><td class="mono">${esc(r.mac)||'—'}</td><td>${r.ports.map(p=>`<span class="port">${p}</span>`).join('')||'—'}</td><td>${{ping:'Ping',port:'Port',arp:'ARP'}[r.via]||esc(r.via)}</td><td class="acts"><button data-act="ping" title="Ping this host">Ping</button><button data-act="ports" title="Test ports on this host">Ports</button><button data-act="copy" title="Copy IP">${CP}</button></td></tr>`).join('')}

/* ================= Ping ================= */
let pg={samples:[],running:false,host:'',wake:null,seq:0};
toolInit.ping=()=>{
  $('#pggo').onclick=()=>pingStart(false);$('#pgstop').onclick=pingStop;
  $('#pgclear').onclick=()=>{pg.samples=[];pg.seq=0;$('#pglog').innerHTML='';pingStats();drawPing()};
  $('#pghost').addEventListener('keydown',e=>{if(e.key==='Enter'&&!pg.running)pingStart(false)});
  const box=$('#pgchart');new ResizeObserver(()=>drawPing()).observe(box);
  box.addEventListener('mousemove',e=>{const r=box.getBoundingClientRect();pg.hover=e.clientX-r.left;drawPing()});
  box.addEventListener('mouseleave',()=>{pg.hover=null;drawPing()});
  new MutationObserver(()=>drawPing()).observe(document.documentElement,{attributes:true,attributeFilter:['data-theme','style']});
  pingStats();
};
function pingLog(text,bad){const l=$('#pglog'),stick=l.scrollTop+l.clientHeight>=l.scrollHeight-4,d=document.createElement('div');d.textContent=text;if(bad)d.className='bad';l.appendChild(d);
  while(l.childNodes.length>500)l.firstChild.remove();if(stick)l.scrollTop=l.scrollHeight}
async function pingStart(fresh){if(!toolInited.has('ping')){toolInited.add('ping');toolInit.ping()}
  if(pg.running){pingStop();await sleep(50)}
  const host=$('#pghost').value.trim();if(!host)return toast('Enter a host name or IPv4 address');
  if(fresh||host!==pg.host){pg.samples=[];pg.seq=0;$('#pglog').innerHTML=''}
  pg.host=host;pg.running=true;$('#pggo').style.display='none';$('#pgstop').style.display='';
  const count=+$('#pgcount').value,interval=+$('#pgint').value,to=+$('#pgto').value,size=Math.max(0,Math.min(65500,+$('#pgsize').value||0));
  $('#pgtitle').textContent='Latency — '+host;
  let n=0,first=true;
  while(pg.running&&(count===0||n<count)){const t0=performance.now();let r;
    try{r=await api(`/api/net/ping?host=${encodeURIComponent(host)}&timeout=${to}&size=${size}`)}
    catch(e){pingLog(e.message,true);toast(e.message);break}
    if(first){pingLog(`Pinging ${host}${r.ip!==host?' ['+r.ip+']':''} with ${size} bytes of data:`);first=false}
    if(!pg.running)break;
    pg.seq++;n++;pg.samples.push({seq:pg.seq,ok:r.ok,ms:r.ms,ttl:r.ttl,status:r.status,t:Date.now(),ip:r.ip});
    if(pg.samples.length>5000)pg.samples.shift();
    pingLog(r.ok?`Reply from ${r.ip}: bytes=${size} time${r.ms<1?'<1ms':'='+r.ms+'ms'} TTL=${r.ttl}`:(r.status==='Request timed out'?'Request timed out.':`${r.status}.`),!r.ok);
    pingStats();drawPing();
    const wait=interval-(performance.now()-t0);
    if(pg.running&&(count===0||n<count)&&wait>0)await new Promise(res=>{pg.wake=res;setTimeout(res,wait)})}
  if(n){const s=pingSummary();pingLog(`Ping statistics for ${host}: sent = ${s.sent}, received = ${s.recv}, lost = ${s.lost} (${s.lossPct}% loss)`+(s.recv?`, min = ${s.min} ms, max = ${s.max} ms, avg = ${s.avg} ms`:''))}
  pg.running=false;$('#pggo').style.display='';$('#pgstop').style.display='none';$('#s3').textContent=''}
function pingStop(){pg.running=false;if(pg.wake)pg.wake()}
function pingSummary(){const s=pg.samples,ok=s.filter(x=>x.ok),ms=ok.map(x=>x.ms);let j=0;for(let i=1;i<ms.length;i++)j+=Math.abs(ms[i]-ms[i-1]);
  return{sent:s.length,recv:ok.length,lost:s.length-ok.length,lossPct:s.length?Math.round((s.length-ok.length)/s.length*100):0,
    min:ms.length?Math.min(...ms):0,max:ms.length?Math.max(...ms):0,avg:ms.length?Math.round(ms.reduce((a,b)=>a+b,0)/ms.length):0,jit:ms.length>1?j/(ms.length-1):0,last:s[s.length-1]}}
function pingStats(){const s=pingSummary(),has=s.recv>0;
  $('#pgtiles').innerHTML=[['Sent',s.sent],['Received',s.recv],['Lost',`${s.lost} (${s.lossPct}%)`,s.lost>0],['Minimum',has?fms(s.min):'—'],['Average',has?fms(s.avg):'—'],['Maximum',has?fms(s.max):'—'],['Jitter',s.recv>1?fms(s.jit):'—'],
    ['Last',s.last?(s.last.ok?fms(s.last.ms):'Lost'):'—',s.last&&!s.last.ok]].map(([k,v,bad])=>`<div class="tile${bad?' bad':''}"><small>${k}</small><b>${v}</b></div>`).join('');
  if(pg.running)$('#s3').textContent=`Pinging ${pg.host} — ${s.sent} sent, ${s.lossPct}% loss${has?', avg '+fms(s.avg):''}`}
function drawPing(){const box=$('#pgchart');if(!box||!box.clientWidth)return;const cv=box.querySelector('canvas'),tip=box.querySelector('.ctip');
  const W=box.clientWidth,H=box.clientHeight,dpr=devicePixelRatio||1;cv.width=W*dpr;cv.height=H*dpr;const g=cv.getContext('2d');g.setTransform(dpr,0,0,dpr,0,0);g.clearRect(0,0,W,H);
  const acc=cssColor('--accent'),bad=cssColor('--bad'),mut=cssColor('--muted'),grid=cssColor('--border');
  const N=120,data=pg.samples.slice(-N),L=44,R=10,T=10,B=22,pw=W-L-R,ph=H-T-B;
  const okMax=Math.max(0,...data.filter(x=>x.ok).map(x=>x.ms));const steps=[4,8,12,20,40,60,80,100,200,400,600,800,1000,2000,4000,6000,8000,10000]; // all divide evenly into 4 gridlines
  const top=steps.find(v=>v>=okMax*1.15)||Math.ceil(okMax/4000)*4000;
  g.font='11px "Segoe UI",sans-serif';g.textBaseline='middle';g.textAlign='right';
  for(let i=0;i<=4;i++){const y=T+ph-ph*i/4;g.strokeStyle=grid;g.lineWidth=1;g.beginPath();g.moveTo(L,Math.round(y)+.5);g.lineTo(W-R,Math.round(y)+.5);g.stroke();g.fillStyle=mut;g.fillText(Math.round(top*i/4)+' ms',L-6,y)}
  if(!data.length){g.textAlign='center';g.fillStyle=mut;g.font='13px "Segoe UI",sans-serif';g.fillText('Press Start to begin pinging',L+pw/2,T+ph/2);tip.style.display='none';return}
  const slots=Math.max(30,data.length),x=i=>L+pw*(i+(slots-data.length))/(slots-1),y=v=>T+ph-ph*Math.min(v,top)/top;
  g.fillStyle=withAlpha(bad,.85);data.forEach((d,i)=>{if(!d.ok)g.fillRect(Math.round(x(i))-1,T,2,ph)});
  // area + line through successful replies, broken at losses
  const segs=[];let cur=[];data.forEach((d,i)=>{if(d.ok)cur.push([x(i),y(d.ms)]);else if(cur.length){segs.push(cur);cur=[]}});if(cur.length)segs.push(cur);
  for(const s of segs){g.beginPath();g.moveTo(s[0][0],T+ph);s.forEach(p=>g.lineTo(p[0],p[1]));g.lineTo(s[s.length-1][0],T+ph);g.closePath();g.fillStyle=withAlpha(acc,.12);g.fill();
    g.beginPath();s.forEach((p,i)=>i?g.lineTo(p[0],p[1]):g.moveTo(p[0],p[1]));g.strokeStyle=acc;g.lineWidth=2;g.lineJoin='round';g.stroke();
    if(s.length===1){g.fillStyle=acc;g.beginPath();g.arc(s[0][0],s[0][1],4,0,7);g.fill()}}
  g.textAlign='left';g.fillStyle=mut;g.fillText('#'+data[0].seq,x(0),H-9);g.textAlign='right';g.fillText('#'+data[data.length-1].seq,W-R,H-9);
  if(pg.hover==null||pg.hover<L-6){tip.style.display='none';return}
  let bi=0,bd=1e9;data.forEach((d,i)=>{const dd=Math.abs(x(i)-pg.hover);if(dd<bd){bd=dd;bi=i}});const d=data[bi],hx=x(bi);
  g.strokeStyle=mut;g.lineWidth=1;g.setLineDash([3,3]);g.beginPath();g.moveTo(Math.round(hx)+.5,T);g.lineTo(Math.round(hx)+.5,T+ph);g.stroke();g.setLineDash([]);
  if(d.ok){g.fillStyle=acc;g.strokeStyle=cssColor('--card');g.lineWidth=2;g.beginPath();g.arc(hx,y(d.ms),5,0,7);g.fill();g.stroke()}
  tip.innerHTML=`<b>${d.ok?fms(d.ms):'Lost'}</b>#${d.seq} · ${new Date(d.t).toLocaleTimeString()}${d.ok?' · TTL '+d.ttl:'<br>'+esc(d.status)}`;
  tip.style.display='block';const tw=tip.offsetWidth;tip.style.left=(hx+12+tw>W?hx-12-tw:hx+12)+'px';tip.style.top=(d.ok?Math.max(0,Math.min(H-50,y(d.ms)-20)):T+10)+'px'}

/* ================= Telnet / ports ================= */
const SERVICES={20:'FTP data',21:'FTP',22:'SSH',23:'Telnet',25:'SMTP',53:'DNS',67:'DHCP',69:'TFTP',80:'HTTP',88:'Kerberos',110:'POP3',123:'NTP',135:'MS RPC',137:'NetBIOS',139:'NetBIOS / SMB',143:'IMAP',161:'SNMP',389:'LDAP',443:'HTTPS',445:'SMB',465:'SMTPS',514:'Syslog',587:'SMTP submission',636:'LDAPS',993:'IMAPS',995:'POP3S',1433:'SQL Server',1521:'Oracle',1883:'MQTT',2049:'NFS',3306:'MySQL',3389:'Remote Desktop',5060:'SIP',5432:'PostgreSQL',5900:'VNC',5985:'WinRM',5986:'WinRM HTTPS',6379:'Redis',8080:'HTTP alt',8443:'HTTPS alt',9200:'Elasticsearch',27017:'MongoDB'};
let tn={id:null};
toolInit.telnet=()=>{
  $('#ptgo').onclick=portTest;
  for(const id of['#pthost','#ptports'])$(id).addEventListener('keydown',e=>{if(e.key==='Enter')portTest()});
  $('#tngo').onclick=tnConnect;$('#tnstop').onclick=tnClose;$('#tnclr').onclick=()=>{$('#tnout').innerHTML=''};
  for(const id of['#tnhost','#tnport'])$(id).addEventListener('keydown',e=>{if(e.key==='Enter'&&tn.id===null)tnConnect()});
  $('#tnsend').onclick=tnSend;$('#tnin').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();tnSend()}});
};
async function portTest(){if(!toolInited.has('telnet')){toolInited.add('telnet');toolInit.telnet()}
  const host=$('#pthost').value.trim(),ports=$('#ptports').value.trim();$('#pterr').textContent='';
  if(!host)return $('#pterr').textContent='Enter a host name or IP address';
  $('#ptgo').disabled=true;$('#pttbl').innerHTML='<tbody><tr><td class="empty">Testing…</td></tr></tbody>';
  try{const r=await api(`/api/net/ports?host=${encodeURIComponent(host)}&ports=${encodeURIComponent(ports)}&timeout=${$('#ptto').value}`);
    const open=r.results.filter(x=>x.open).length;
    $('#pttbl').innerHTML=`<thead><tr><th>Port</th><th>Service</th><th>State</th><th class="r">Time</th><th>Details</th></tr></thead><tbody>`+
      r.results.map(x=>`<tr><td class="mono">${x.port}</td><td>${SERVICES[x.port]||'<span style="color:var(--muted)">—</span>'}</td><td>${x.open?'<span class="st ok">Open</span>':/timed out/.test(x.err)?'<span class="st mut">Filtered</span>':'<span class="st bad">Closed</span>'}</td><td class="r">${fms(x.ms)}</td><td style="color:var(--muted)">${x.open?'Connection accepted':esc(x.err||'')}</td></tr>`).join('')+
      `</tbody>`;
    $('#pterr').innerHTML=`<span style="color:var(--muted)">${esc(host)}${r.ip!==host?' ('+esc(r.ip)+')':''}: ${open} of ${r.results.length} port${r.results.length===1?'':'s'} open</span>`;
  }catch(e){$('#pttbl').innerHTML='';$('#pterr').textContent=e.message}
  $('#ptgo').disabled=false}
function tnWrite(text,cls){const o=$('#tnout'),stick=o.scrollTop+o.clientHeight>=o.scrollHeight-4;
  const clean=text.replace(/\x1b\[[0-9;?]*[ -\/]*[@-~]/g,'').replace(/\x1b[()][0-9A-Za-z]/g,'').replace(/\r\n?/g,'\n').replace(/[\x00-\x08\x0b-\x1f\x7f]/g,'');
  if(!clean)return;const s=document.createElement('span');if(cls)s.className=cls;s.textContent=clean;o.appendChild(s);
  while(o.textContent.length>300000&&o.firstChild)o.firstChild.remove();if(stick)o.scrollTop=o.scrollHeight}
function tnUI(on){$('#tngo').style.display=on?'none':'';$('#tnstop').style.display=on?'':'none';$('#tnin').disabled=!on;$('#tnsend').disabled=!on;
  for(const id of['#tnhost','#tnport','#tntel'])$(id).disabled=on;if(on)$('#tnin').focus()}
async function tnConnect(){const host=$('#tnhost').value.trim(),port=+$('#tnport').value;if(!host||!port)return toast('Enter a host and port');
  if($('#tnout').querySelector('.sys')&&$('#tnout').childNodes.length===1)$('#tnout').innerHTML='';
  tnWrite(`Connecting to ${host}:${port} …\n`,'sys');$('#tngo').disabled=true;
  let r;try{r=await api(`/api/net/tcp/open?host=${encodeURIComponent(host)}&port=${port}&telnet=${$('#tntel').checked?1:0}`)}catch(e){r={ok:false,error:e.message}}
  $('#tngo').disabled=false;
  if(!r.ok){tnWrite(`Could not connect (${fms(r.ms)}): ${r.error}\n`,'er');return}
  tnWrite(`Connected to ${r.remote} in ${fms(r.ms)} (local ${r.local})\n`,'sys');tn.id=r.id;tnUI(true);
  const id=r.id;
  while(tn.id===id){let d;try{d=await api('/api/net/tcp/read?id='+id)}catch(e){tnWrite(`\n[${e.message}]\n`,'er');break}
    if(d.data)tnWrite(d.data);if(d.closed){tnWrite(`\n[Connection closed${d.error?': '+d.error:''}]\n`,'sys');break}}
  if(tn.id===id){tn.id=null;tnUI(false)}}
async function tnSend(){if(tn.id===null)return;const line=$('#tnin').value,eol=$('#tneol').value.replace(/\\r/g,'\r').replace(/\\n/g,'\n');
  tnWrite(line+'\n','me');$('#tnin').value='';
  try{await api('/api/net/tcp/send?id='+tn.id,{method:'POST',body:JSON.stringify({data:line+eol})})}catch(e){tnWrite(`[send failed: ${e.message}]\n`,'er')}}
function tnClose(){const id=tn.id;if(id===null)return;tn.id=null;tnUI(false);tnWrite('[Disconnected]\n','sys');api('/api/net/tcp/close?id='+id).catch(()=>{})}

/* ================= Hash ================= */
function md5(bytes){
  const K=md5.K||(md5.K=Uint32Array.from({length:64},(_,i)=>Math.floor(Math.abs(Math.sin(i+1))*2**32))),S=[7,12,17,22,5,9,14,20,4,11,16,23,6,10,15,21];
  const n=bytes.length,len=(((n+8)>>>6)+1)*64,m=new Uint8Array(len);m.set(bytes);m[n]=0x80;
  const dv=new DataView(m.buffer);dv.setUint32(len-8,(n*8)>>>0,true);dv.setUint32(len-4,Math.floor(n/0x20000000),true);
  let a0=0x67452301,b0=0xefcdab89,c0=0x98badcfe,d0=0x10325476;const M=new Uint32Array(16);
  for(let o=0;o<len;o+=64){for(let i=0;i<16;i++)M[i]=dv.getUint32(o+i*4,true);let A=a0,B=b0,C=c0,D=d0;
    for(let i=0;i<64;i++){const r=i>>4;let F,g;
      if(r===0){F=(B&C)|(~B&D);g=i}else if(r===1){F=(D&B)|(~D&C);g=(5*i+1)&15}else if(r===2){F=B^C^D;g=(3*i+5)&15}else{F=C^(B|~D);g=(7*i)&15}
      F=(F+A+K[i]+M[g])>>>0;A=D;D=C;C=B;const s=S[r*4+(i&3)];B=(B+((F<<s)|(F>>>(32-s))))>>>0}
    a0=(a0+A)>>>0;b0=(b0+B)>>>0;c0=(c0+C)>>>0;d0=(d0+D)>>>0}
  const out=new Uint8Array(16),ov=new DataView(out.buffer);[a0,b0,c0,d0].forEach((v,i)=>ov.setUint32(i*4,v,true));return out}
const subtleHash=alg=>async b=>new Uint8Array(await crypto.subtle.digest(alg,b));
const HASHES=[['MD5',md5,64],['SHA-1',subtleHash('SHA-1'),64],['SHA-256',subtleHash('SHA-256'),64],['SHA-384',subtleHash('SHA-384'),128],['SHA-512',subtleHash('SHA-512'),128]];
async function hmac(fn,block,key,msg){if(key.length>block)key=await fn(key);const k=new Uint8Array(block);k.set(key);
  return fn(concat(k.map(x=>x^0x5c),await fn(concat(k.map(x=>x^0x36),msg))))}
let hsFile=null,hsRun=0;
toolInit.hash=()=>{
  const run=debounce(hashRun,120);
  $('#hsin').addEventListener('input',()=>{hsFile=null;run()});$('#hskey').addEventListener('input',run);$('#hsenc').onchange=hashRun;
  $('#hsfile').onclick=()=>$('#hsfilein').click();
  $('#hsfilein').onchange=async e=>{const f=e.target.files[0];e.target.value='';if(!f)return;
    if(f.size>1024**3)return toast('Files over 1 GB are too large to hash here');
    $('#hsinfo').textContent=`Reading ${f.name} …`;hsFile={name:f.name,bytes:new Uint8Array(await f.arrayBuffer())};$('#hsin').value='';hashRun()};
  hashRun()};
async function hashRun(){const id=++hsRun,bytes=hsFile?hsFile.bytes:enc.encode($('#hsin').value),key=$('#hskey').value,fmtOut=$('#hsenc').value;
  $('#hsinfo').textContent=hsFile?`File: ${hsFile.name} · ${fmt(bytes.length)} (${bytes.length.toLocaleString()} bytes)`:`UTF-8 text · ${bytes.length.toLocaleString()} bytes`;
  const rows=[];
  for(const [name,fn,block] of HASHES){const d=key?await hmac(fn,block,enc.encode(key),bytes):await fn(bytes);
    rows.push([(key?'HMAC-':'')+name,fmtOut==='b64'?b64(d):fmtOut==='HEX'?toHex(d).toUpperCase():toHex(d)])}
  if(id===hsRun)$('#hsout').innerHTML=kvHTML(rows)}

/* ================= Encrypt / decrypt ================= */
let crMode='enc';
toolInit.crypt=()=>{
  segInit('#crmode',b=>{crMode=b.dataset.m;const e=crMode==='enc';$('#crgo').textContent=e?'Encrypt':'Decrypt';
    $('#crinl').textContent=e?'Plain text':'Encrypted text (Base64)';$('#croutl').textContent=e?'Encrypted (Base64)':'Decrypted text';$('#crout').value='';$('#crerr').textContent=''});
  $('#crshow').onclick=()=>{const p=$('#crpass');p.type=p.type==='password'?'text':'password'};
  $('#crgo').onclick=cryptRun;
  for(const id of['#crin','#crpass'])$(id).addEventListener('keydown',e=>{if(e.key==='Enter'&&(e.ctrlKey||id==='#crpass')){e.preventDefault();cryptRun()}});
  $('#crswap').onclick=()=>{const v=$('#crout').value;if(!v)return;$('#crin').value=v;$('#crout').value='';$(`#crmode [data-m=${crMode==='enc'?'dec':'enc'}]`).click();$('#crin').value=v}};
async function deriveKey(pass,salt){const base=await crypto.subtle.importKey('raw',enc.encode(pass),'PBKDF2',false,['deriveKey']);
  return crypto.subtle.deriveKey({name:'PBKDF2',salt,iterations:210000,hash:'SHA-256'},base,{name:'AES-GCM',length:256},false,['encrypt','decrypt'])}
async function cryptRun(){const pass=$('#crpass').value,input=$('#crin').value;$('#crerr').textContent='';
  if(!pass)return $('#crerr').textContent='Enter a passphrase';if(!input)return $('#crerr').textContent='Enter some text';
  $('#crgo').disabled=true;
  try{if(crMode==='enc'){const salt=rand(16),iv=rand(12),key=await deriveKey(pass,salt);
      const ct=new Uint8Array(await crypto.subtle.encrypt({name:'AES-GCM',iv},key,enc.encode(input)));$('#crout').value=b64(concat([1],salt,iv,ct))}
    else{let b;try{b=unb64(input.trim())}catch(e){throw new Error('This is not Base64 text encrypted by My Tool Box')}
      if(b[0]!==1||b.length<1+16+12+16)throw new Error('This is not text encrypted by My Tool Box (unknown format)');
      const key=await deriveKey(pass,b.subarray(1,17));let pt;
      try{pt=await crypto.subtle.decrypt({name:'AES-GCM',iv:b.subarray(17,29)},key,b.subarray(29))}catch(e){throw new Error('Wrong passphrase, or the text was changed or damaged')}
      $('#crout').value=fromUTF8(new Uint8Array(pt))}}
  catch(e){$('#crout').value='';$('#crerr').textContent=e.message}
  $('#crgo').disabled=false}

/* ================= Token & UUID ================= */
function fmtUUID(b){const h=toHex(b);return`${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`}
function uuid7(){const b=rand(16);let t=Date.now();for(let i=5;i>=0;i--){b[i]=t%256;t=Math.floor(t/256)}b[6]=b[6]&0x0f|0x70;b[8]=b[8]&0x3f|0x80;return fmtUUID(b)}
const CROCK='0123456789ABCDEFGHJKMNPQRSTVWXYZ';
function ulid(){let t=Date.now(),s='';for(let i=0;i<10;i++){s=CROCK[t%32]+s;t=Math.floor(t/32)}for(const x of rand(16))s+=CROCK[x&31];return s}
toolInit.token=()=>{
  $('#uugo').onclick=uuidGen;for(const id of['#uuver','#uuup','#uuhy','#uubr','#uun'])$(id).addEventListener('change',uuidGen);
  $('#tkgo').onclick=tokenGen;for(const id of['#tklen','#tkn','#tkU','#tkL','#tkD','#tkS','#tkA','#tkfmt'])$(id).addEventListener('change',tokenGen);
  uuidGen();tokenGen()};
function uuidGen(){const v=$('#uuver').value,n=Math.max(1,Math.min(1000,+$('#uun').value||1)),out=[];
  for(let i=0;i<n;i++){let s=v==='ulid'?ulid():v==='7'?uuid7():v==='nil'?'00000000-0000-0000-0000-000000000000':crypto.randomUUID();
    if(v!=='ulid'){if(!$('#uuhy').checked)s=s.replace(/-/g,'');s=$('#uuup').checked?s.toUpperCase():s.toLowerCase();if($('#uubr').checked)s='{'+s+'}'}
    out.push(s)}
  $('#uuout').value=out.join('\n')}
function tokenGen(){const len=Math.max(1,Math.min(1024,+$('#tklen').value||1)),n=Math.max(1,Math.min(500,+$('#tkn').value||1)),f=$('#tkfmt').value,out=[];
  let bits;
  if(f==='chars'){let cs=($('#tkU').checked?'ABCDEFGHIJKLMNOPQRSTUVWXYZ':'')+($('#tkL').checked?'abcdefghijklmnopqrstuvwxyz':'')+($('#tkD').checked?'0123456789':'')+($('#tkS').checked?'!@#$%^&*()-_=+[]{};:,.<>/?~':'');
    if($('#tkA').checked)cs=cs.replace(/[0Oo1lI|]/g,'');
    if(!cs){$('#tkout').value='';$('#tkinfo').textContent='Pick at least one character set.';return}
    for(let i=0;i<n;i++){let s='';for(let j=0;j<len;j++)s+=cs[randInt(cs.length)];out.push(s)}
    bits=len*Math.log2(cs.length);$('#tkinfo').textContent=`${cs.length} possible characters · about ${Math.floor(bits)} bits of entropy each`}
  else{for(let i=0;i<n;i++){const b=rand(len);out.push(f==='hex'?toHex(b):b64(b).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,''))}
    $('#tkinfo').textContent=`${len} random bytes = ${len*8} bits of entropy each`}
  $('#tkout').value=out.join('\n')}

/* ================= Encoders ================= */
let enDir='enc';
toolInit.encode=()=>{
  segInit('#endir',b=>{enDir=b.dataset.d;encodeRun()});
  $('#enkind').onchange=encodeRun;$('#enin').addEventListener('input',encodeRun);
  $('#enswap').onclick=()=>{const v=$('#enout').value;$(`#endir [data-d=${enDir==='enc'?'dec':'enc'}]`).click();$('#enin').value=v;encodeRun()}};
function encodeRun(){const k=$('#enkind').value,s=$('#enin').value,e=enDir==='enc';let out='';$('#enerr').textContent='';
  try{switch(k){
    case'b64':out=e?b64(enc.encode(s)):fromUTF8(unb64(s));break;
    case'b64u':out=e?b64(enc.encode(s)).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,''):fromUTF8(unb64(s));break;
    case'url':out=e?encodeURIComponent(s):decodeURIComponent(s.replace(/\+/g,' '));break;
    case'uri':out=e?encodeURI(s):decodeURI(s);break;
    case'html':if(e)out=s.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));else{const t=document.createElement('textarea');t.innerHTML=s;out=t.value}break;
    case'hex':if(e)out=toHex(enc.encode(s));else{const h=s.replace(/0x|[\s:,-]/gi,'');if(/[^0-9a-f]/i.test(h)||h.length%2)throw new Error('Hex input must be pairs of 0-9 / A-F');
      out=fromUTF8(Uint8Array.from(h.match(/../g)||[],x=>parseInt(x,16)))}break;
    case'uni':out=e?Array.from(s,c=>{const cp=c.codePointAt(0);return cp<128?c:cp>0xffff?'\\u{'+cp.toString(16).toUpperCase()+'}':'\\u'+cp.toString(16).toUpperCase().padStart(4,'0')}).join('')
      :s.replace(/\\u\{([0-9a-f]+)\}|\\u([0-9a-f]{4})|\\x([0-9a-f]{2})/gi,(m,a,b,c)=>{const cp=parseInt(a||b||c,16);return cp<=0x10ffff?String.fromCodePoint(cp):m});break}}
  catch(err){$('#enerr').textContent=err instanceof URIError?'Not valid URL-encoded text':err.message}
  $('#enout').value=out}

/* ================= JWT ================= */
function hlJSON(o,indent=2){const s=JSON.stringify(o,null,indent);let out='',last=0,m;const re=/("(?:\\.|[^"\\])*")(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/g;
  while((m=re.exec(s))){out+=esc(s.slice(last,m.index));out+=m[1]?(m[2]?`<span class="k">${esc(m[1])}</span>${m[2]}`:`<span class="s">${esc(m[1])}</span>`):`<span class="${m[3]?'b':'n'}">${m[0]}</span>`;last=re.lastIndex}
  return out+esc(s.slice(last))}
const rtf=new Intl.RelativeTimeFormat(undefined,{numeric:'auto'});
function relTime(ms){const d=(ms-Date.now())/1000,a=Math.abs(d);for(const [u,s] of[['year',31536000],['month',2592000],['day',86400],['hour',3600],['minute',60]])if(a>=s)return rtf.format(Math.round(d/s),u);return rtf.format(Math.round(d),'second')}
toolInit.jwt=()=>{$('#jwin').addEventListener('input',jwtRun);jwtRun()};
function jwtRun(){const t=$('#jwin').value.trim().replace(/^Bearer\s+/i,'');$('#jwerr').textContent='';$('#jwh').innerHTML='';$('#jwp').innerHTML='';$('#jwclaims').style.display='none';
  if(!t)return;const parts=t.split('.');
  try{if(parts.length<2)throw new Error('A JWT has three parts separated by dots (header.payload.signature)');
    const part=(p,n)=>{try{return JSON.parse(fromUTF8(unb64(p)))}catch(e){throw new Error(`The ${n} is not valid Base64url-encoded JSON`)}};
    const h=part(parts[0],'header'),p=part(parts[1],'payload');
    $('#jwh').innerHTML=hlJSON(h);$('#jwp').innerHTML=hlJSON(p);
    const rows=[],names={exp:'Expires (exp)',iat:'Issued at (iat)',nbf:'Not before (nbf)',auth_time:'Authenticated (auth_time)'};
    for(const k of Object.keys(names))if(typeof p[k]==='number'){const d=new Date(p[k]*1000);rows.push([names[k],d.toLocaleString(),`${esc(d.toLocaleString())} <span style="color:var(--muted)">· ${esc(relTime(d))}</span>`])}
    if(typeof p.exp==='number'){const ok=p.exp*1000>Date.now();rows.push(['Status',ok?'Not expired':'Expired',`<span class="st ${ok?'ok':'bad'}">${ok?'Not expired':'Expired'}</span>`])}
    if(h.alg)rows.push(['Algorithm',h.alg]);if(parts[2]!==undefined)rows.push(['Signature',parts[2]?'Present (not verified)':'None','<span style="color:var(--muted)">'+(parts[2]?'Present — not verified':'None (unsigned token)')+'</span>']);
    $('#jwkv').innerHTML=kvHTML(rows);$('#jwclaims').style.display=rows.length?'':'none'}
  catch(e){$('#jwerr').textContent=e.message}}

/* ================= JSON ================= */
let jsMode='fmt';
toolInit.json=()=>{$('#jsfmt').onclick=()=>{jsMode='fmt';jsonRun()};$('#jsmin').onclick=()=>{jsMode='min';jsonRun()};
  $('#jsin').addEventListener('input',debounce(jsonRun,150));$('#jsind').onchange=jsonRun;$('#jssort').onchange=jsonRun};
function sortKeys(v){return Array.isArray(v)?v.map(sortKeys):v&&typeof v==='object'?Object.fromEntries(Object.keys(v).sort().map(k=>[k,sortKeys(v[k])])):v}
function jsonRun(){const s=$('#jsin').value;$('#jserr').textContent='';$('#jsinfo').textContent='';if(!s.trim()){$('#jsout').value='';return}
  try{let v=JSON.parse(s);if($('#jssort').checked)v=sortKeys(v);const ind=$('#jsind').value;
    const out=jsMode==='min'?JSON.stringify(v):JSON.stringify(v,null,ind==='tab'?'\t':+ind);$('#jsout').value=out;
    $('#jsinfo').textContent=`Valid JSON · ${fmt(enc.encode(s).length)} in → ${fmt(enc.encode(out).length)} out`}
  catch(e){$('#jsout').value='';const pos=+((e.message.match(/position (\d+)/)||[])[1]);
    if(pos>=0&&!isNaN(pos)){const before=s.slice(0,pos),line=before.split('\n').length,col=pos-before.lastIndexOf('\n');$('#jserr').textContent=`Invalid JSON at line ${line}, column ${col}: ${e.message}`}
    else $('#jserr').textContent='Invalid JSON: '+e.message}}

/* ================= Timestamp ================= */
let tmTimer=null;
toolInit.time=()=>{const run=()=>timeRun();$('#tmin').addEventListener('input',run);$('#tmunit').onchange=run;
  $('#tmpick').addEventListener('input',()=>{const v=$('#tmpick').value;if(v){$('#tmin').value=String(Math.floor(new Date(v).getTime()/1000));$('#tmunit').value='auto';timeRun(true)}});
  $('#tmnowbtn').onclick=()=>{$('#tmin').value=String(Math.floor(Date.now()/1000));timeRun()};
  const tick=()=>{if(curTool!=='time')return;const n=new Date();
    $('#tmnow').innerHTML=[['Unix time (seconds)',Math.floor(n/1000)],['Unix time (ms)',+n],['UTC',n.toISOString().replace('.000','')],['Local',n.toLocaleString()]]
      .map(([k,v])=>`<div class="tile" data-copytext="${esc(v)}" title="Click to copy"><small>${k}</small><b class="sm">${esc(v)}</b></div>`).join('')};
  tick();tmTimer=setInterval(tick,1000);
  $('#tmin').value=String(Math.floor(Date.now()/1000));timeRun()};
function isoLocal(d){const p=n=>String(n).padStart(2,'0'),o=-d.getTimezoneOffset(),sign=o>=0?'+':'-';
  return`${d.getFullYear()}-${p(d.getMonth()+1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}${sign}${p(Math.floor(Math.abs(o)/60))}:${p(Math.abs(o)%60)}`}
function isoWeek(d){const t=new Date(Date.UTC(d.getFullYear(),d.getMonth(),d.getDate()));const day=t.getUTCDay()||7;t.setUTCDate(t.getUTCDate()+4-day);const y=new Date(Date.UTC(t.getUTCFullYear(),0,1));return Math.ceil(((t-y)/86400000+1)/7)}
function timeRun(fromPicker){const s=$('#tmin').value.trim();$('#tmerr').textContent='';if(!s){$('#tmout').innerHTML='';return}
  let ms;
  if(/^-?\d+(\.\d+)?$/.test(s)){const n=+s,a=Math.abs(n);let u=$('#tmunit').value;if(u==='auto')u=a<1e11?'s':a<1e14?'ms':a<1e17?'us':'ns';ms=u==='s'?n*1000:u==='ms'?n:u==='us'?n/1000:n/1e6}
  else ms=Date.parse(s);
  const d=new Date(ms);if(isNaN(d)){$('#tmerr').textContent='Could not read that date. Try a Unix timestamp or an ISO 8601 date like 2026-10-15T12:00:00Z.';$('#tmout').innerHTML='';return}
  if(!fromPicker){const p=n=>String(n).padStart(2,'0');$('#tmpick').value=`${d.getFullYear()}-${p(d.getMonth()+1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`}
  const start=new Date(d.getFullYear(),0,1);
  $('#tmout').innerHTML=kvHTML([['Unix time (seconds)',String(Math.floor(ms/1000))],['Unix time (milliseconds)',String(Math.round(ms))],['ISO 8601 (UTC)',d.toISOString()],['ISO 8601 (local)',isoLocal(d)],
    ['Local time',d.toLocaleString(undefined,{dateStyle:'full',timeStyle:'long'})],['RFC 7231 / HTTP date',d.toUTCString()],['Relative',relTime(d)],
    ['Day of year',String(Math.floor((new Date(d.getFullYear(),d.getMonth(),d.getDate())-start)/86400000)+1)],['ISO week',String(isoWeek(d))],
    ['Time zone',Intl.DateTimeFormat().resolvedOptions().timeZone+' (UTC'+isoLocal(d).slice(-6)+')']])}

/* ================= Number base ================= */
toolInit.base=()=>{const run=()=>baseRun();$('#nbin').addEventListener('input',run);$('#nbfrom').onchange=run;baseRun()};
function baseRun(){let s=$('#nbin').value.trim().replace(/[\s_]/g,'').toLowerCase();$('#nberr').textContent='';if(!s){$('#nbout').innerHTML='';return}
  let neg=false;if(s[0]==='-'){neg=true;s=s.slice(1)}
  const pre={'0x':16,'0b':2,'0o':8}[s.slice(0,2)];if(pre){$('#nbfrom').value=String(pre);s=s.slice(2)}
  const base=+$('#nbfrom').value,digits='0123456789abcdefghijklmnopqrstuvwxyz'.slice(0,base);let v=0n;
  for(const c of s){const d=digits.indexOf(c);if(d<0){$('#nberr').textContent=`“${c}” is not a valid base-${base} digit`;$('#nbout').innerHTML='';return}v=v*BigInt(base)+BigInt(d)}
  if(neg)v=-v;const abs=v<0n?-v:v,bl=abs.toString(2).length;
  const grp=(x,n)=>x.replace(new RegExp(`\\B(?=(.{${n}})+$)`,'g'),' ');
  $('#nbout').innerHTML=kvHTML([['Binary (2)',v.toString(2),esc(grp(v.toString(2),4))],['Octal (8)',v.toString(8)],['Decimal (10)',v.toString(10),esc(v.toLocaleString())],
    ['Hexadecimal (16)',v.toString(16).toUpperCase(),esc(grp(v.toString(16).toUpperCase(),2))],['Base 32',v.toString(32)],['Base 36',v.toString(36)],
    ['Bit length',String(bl)],['Bytes',String(Math.ceil(bl/8))]])}

