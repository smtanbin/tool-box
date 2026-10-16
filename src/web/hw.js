// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Hardware info (CPU-Z style tabs plus a Task Manager style live CPU panel). Shares the helpers from index.html / tools.js.
"use strict";
TOOL_NAMES.hw='Hardware info';

const hw={data:null,tab:'cpu',loading:false,spd:0,hist:{total:[],cores:[],mem:[]},gpus:[],nets:[],perf:null,gh:{},nh:{},live:null,timer:0};
const HW_TABS=[['cpu','Processor'],['board','Mainboard'],['mem','Memory'],['gpu','Graphics'],['net','Network'],['sys','System']];
const MEM_TYPE={20:'DDR',21:'DDR2',24:'DDR3',26:'DDR4',34:'DDR5',35:'LPDDR5'};
const MEM_FORM={8:'DIMM',12:'SODIMM',13:'SRIMM'};
const CPU_ARCH={0:'x86',5:'ARM',9:'x64',12:'ARM64'};
const HIST=60;
const gpuKindName={gpu:'Graphics card',igpu:'Integrated GPU',npu:'NPU',software:'Software adapter'};
const hwGHz=m=>m?(m>=1000?(m/1000).toFixed(2).replace(/0$/,'').replace(/\.0$/,'')+' GHz':m+' MHz'):'—';
const hwTrim=s=>String(s??'').replace(/\s+/g,' ').trim();
const hwOne=a=>Array.isArray(a)?a[0]||{}:a||{};
const hwList=a=>Array.isArray(a)?a:a?[a]:[];
const hwRow=(k,v)=>[k,v==null||v===''?'—':String(v)];
const hwKB=b=>b>=1048576?(b/1048576)+' MB':(b/1024)+' KB';
const hwUp=s=>{const d=Math.floor(s/86400),h=Math.floor(s%86400/3600),m=Math.floor(s%3600/60),x=s%60,p=n=>String(n).padStart(2,'0');return`${d}:${p(h)}:${p(m)}:${p(x)}`};

toolInit.hw=()=>{
  $('#hwtabs').innerHTML=HW_TABS.map(([k,l])=>`<button data-t="${k}"${k===hw.tab?' class="on"':''}>${l}</button>`).join('');
  segInit('#hwtabs',b=>{hw.tab=b.dataset.t;hwRender()});
  $('#hwref').onclick=()=>hwLoad(true);
  $('#hwcopy').onclick=()=>{const t=hwReport();if(t)copyText(t);else toast('Nothing to copy yet')};
  $('#hwbody').addEventListener('change',e=>{if(e.target.id==='hwslot'){hw.spd=+e.target.value;hwRender()}});
  addEventListener('keydown',e=>{if(e.key==='F5'&&curTool==='hw'){e.preventDefault();hwLoad(true)}});
  addEventListener('resize',debounce(()=>{if(curTool==='hw'&&hw.tab==='cpu')hwDrawGraphs()},150));
  hw.timer=setInterval(()=>{hwTick();hwPerfTick()},1000);
  hwLoad()};

async function hwLoad(fresh){if(hw.loading)return;hw.loading=true;$('#hwref').disabled=true;$('#s3').textContent='Reading hardware…';
  try{const[d,g,n]=await Promise.all([api('/api/hw'+(fresh?'?fresh=1':'')),api('/api/hw/gpus').catch(()=>({adapters:[]})),api('/api/net/adapters').catch(()=>({adapters:[]}))]);hw.data=d;hw.gpus=g.adapters||[];hw.nets=n.adapters||[];hwRender();$('#s3').textContent='Read '+new Date().toLocaleTimeString()}
  catch(e){$('#hwbody').innerHTML=`<div class="card"><div class="err">${esc(e.message)}</div></div>`}
  hw.loading=false;$('#hwref').disabled=false}

/* ---------- live CPU panel ---------- */
async function hwTick(){
  if(curTool!=='hw'||(hw.tab!=='cpu'&&hw.tab!=='mem')||document.hidden)return;
  try{const r=await api('/api/hw/cpu');hw.live=r;const H=hw.hist;
    H.total.push(r.total);if(H.total.length>HIST)H.total.shift();
    r.cores.forEach((c,i)=>{(H.cores[i]=H.cores[i]||[]).push(c);if(H.cores[i].length>HIST)H.cores[i].shift()});
    if(r.memTotal){H.mem.push(r.memUsed/r.memTotal*100);if(H.mem.length>HIST)H.mem.shift()}
    hwUpdateLive();hwDrawMem()}catch(e){}}

const HQ=60;
const pushH=(o,k,v)=>{const a=o[k]=o[k]||[];a.push(v);if(a.length>HQ)a.shift()};
const normNet=s=>String(s||'').toLowerCase().replace(/[()#\/\\]/g,c=>c==='('?'[':c===')'?']':'_').replace(/\s+/g,' ').trim();
async function hwPerfTick(){
  if(curTool!=='hw'||(hw.tab!=='gpu'&&hw.tab!=='net')||document.hidden||hw.perfBusy)return;
  hw.perfBusy=true;
  try{const r=await api('/api/hw/perf');hw.perf=r;
    for(const g of r.gpu){pushH(hw.gh,g.luid+':util',g.util);pushH(hw.gh,g.luid+':mem',g.dedicated)}
    for(const n of r.net){pushH(hw.nh,n.name+':rx',n.rx);pushH(hw.nh,n.name+':tx',n.tx)}
    const npuNow=hw.perf.gpu.filter(g=>!hw.gpus.some(a=>a.luid===g.luid)).length;
    if((hw.tab==='gpu'&&npuNow!==(hw.npuShown||0))||(hw.tab==='net'&&!hw.netShown)){hw.npuShown=npuNow;hw.netShown=hw.tab==='net'||hw.netShown;hwRender()}
    hwDrawPerf()}catch(e){}
  hw.perfBusy=false}

/* one small line chart: series = [{d:[...],color}], max = number or 'auto' */
function hwLineChart(cv,series,max){
  const dpr=window.devicePixelRatio||1,w=cv.clientWidth,h=cv.clientHeight;if(!w)return 0;if(cv.width!==Math.round(w*dpr)){cv.width=Math.round(w*dpr);cv.height=Math.round(h*dpr)}
  const x=cv.getContext('2d');x.setTransform(dpr,0,0,dpr,0,0);x.clearRect(0,0,w,h);
  x.strokeStyle=cssColor('--border');x.lineWidth=1;x.beginPath();for(let i=1;i<10;i++){const gx=Math.round(w*i/10)+.5;x.moveTo(gx,0);x.lineTo(gx,h)}for(let i=1;i<4;i++){const gy=Math.round(h*i/4)+.5;x.moveTo(0,gy);x.lineTo(w,gy)}x.stroke();
  let top=max;if(max==='auto'){top=Math.max(1024,...series.flatMap(s=>s.d));top=Math.pow(2,Math.ceil(Math.log2(top)))}
  const step=w/(HQ-1);
  series.forEach((s,k)=>{const d=s.d;if(d.length<2)return;const x0=w-(d.length-1)*step,y=v=>h-1-Math.min(1,v/top)*(h-2);
    x.beginPath();d.forEach((v,i)=>i?x.lineTo(x0+i*step,y(v)):x.moveTo(x0,y(v)));x.strokeStyle=s.color;x.lineWidth=1.5;x.stroke();
    if(k===0){x.lineTo(w,h);x.lineTo(x0,h);x.closePath();x.fillStyle=withAlpha(s.color,.16);x.fill()}});
  return top}
const fmtRate=b=>b>=1048576?(b/1048576).toFixed(b>=10485760?0:1)+' MB/s':b>=1024?(b/1024).toFixed(b>=10240?0:1)+' KB/s':Math.round(b)+' B/s';

function hwDrawPerf(){
  const r=hw.perf;if(!r)return;const acc=cssColor('--accent'),alt=cssColor('--good');
  for(const cv of $$('#hwbody canvas[data-ch]')){
    const k=cv.dataset.ch,id=cv.dataset.id;
    if(k==='gutil'){hwLineChart(cv,[{d:hw.gh[id+':util']||[],color:acc}],100);const g=r.gpu.find(x=>x.luid===id);const t=document.getElementById('gt-'+cv.dataset.n);if(t&&g)t.innerHTML=`<b>${g.util.toFixed(0)}%</b> utilisation <span class="hint" style="margin:0">${Object.entries(g.engines).filter(e=>e[1]>=.5).map(e=>esc(e[0])+' '+e[1].toFixed(0)+'%').join(' · ')}</span>`}
    else if(k==='gmem'){const g=r.gpu.find(x=>x.luid===id),tot=+cv.dataset.total||0;hwLineChart(cv,[{d:(hw.gh[id+':mem']||[]).map(v=>tot?v/tot*100:0),color:alt}],100);const t=document.getElementById('gm-'+cv.dataset.n);if(t&&g)t.innerHTML=`<b>${fmt(g.dedicated)}</b>${tot?' of '+fmt(tot):''} dedicated memory in use`}
    else if(k==='nrx'){hwLineChart(cv,[{d:hw.nh[id+':rx']||[],color:acc},{d:hw.nh[id+':tx']||[],color:alt}],'auto');const n=r.net.find(x=>x.name===id);const t=document.getElementById('nt-'+cv.dataset.n);if(t&&n)t.innerHTML=`<span style="color:${acc}">▼</span> receive <b>${fmtRate(n.rx)}</b> &nbsp; <span style="color:${alt}">▲</span> send <b>${fmtRate(n.tx)}</b>`}}}

function hwUpdateLive(){
  const r=hw.live,el=$('#hwlive');if(!r||!el)return;
  const set=(id,v)=>{const x=$('#'+id);if(x)x.textContent=v};
  set('hl-util',Math.round(r.total)+'%');set('hl-speed',r.speed?(r.speed/1000).toFixed(2)+' GHz':'—');set('hl-proc',r.processes.toLocaleString());set('hl-thr',r.threads.toLocaleString());
  set('hl-hnd',r.handles.toLocaleString());set('hl-up',hwUp(r.uptime));
  const sp=$('#hc-speed');if(sp)sp.textContent=r.core0?r.core0.toFixed(2)+' MHz':'—';
  hwDrawGraphs()}

function hwDrawGraphs(){
  const grid=$('#hwgraphs');if(!grid)return;const n=hw.hist.cores.length;
  if(grid.children.length!==n){grid.innerHTML=hw.hist.cores.map((_,i)=>`<canvas data-i="${i}" title="Logical processor ${i}"></canvas>`).join('')}
  const line=cssColor('--accent'),dpr=window.devicePixelRatio||1,border=cssColor('--border');
  for(const cv of grid.children){
    const w=cv.clientWidth,h=cv.clientHeight;if(!w)continue;if(cv.width!==Math.round(w*dpr)){cv.width=Math.round(w*dpr);cv.height=Math.round(h*dpr)}
    const x=cv.getContext('2d');x.setTransform(dpr,0,0,dpr,0,0);x.clearRect(0,0,w,h);
    x.strokeStyle=border;x.lineWidth=1;x.beginPath();for(let i=1;i<10;i++){const gx=Math.round(w*i/10)+.5;x.moveTo(gx,0);x.lineTo(gx,h)}for(let i=1;i<5;i++){const gy=Math.round(h*i/5)+.5;x.moveTo(0,gy);x.lineTo(w,gy)}x.stroke();
    const d=hw.hist.cores[+cv.dataset.i]||[],step=w/(HIST-1),y=v=>h-1-v/100*(h-2);
    if(d.length<2)continue;const x0=w-(d.length-1)*step;
    x.beginPath();d.forEach((v,i)=>i?x.lineTo(x0+i*step,y(v)):x.moveTo(x0,y(v)));
    x.strokeStyle=line;x.lineWidth=1.4;x.stroke();x.lineTo(w,h);x.lineTo(x0,h);x.closePath();x.fillStyle=withAlpha(line,.18);x.fill()}}


function hwDrawMem(){
  const cv=$('#hwmemchart');if(!cv)return;const r=hw.live,d=hw.hist.mem;
  const tx=$('#hwmemtxt');if(tx&&r&&r.memTotal)tx.textContent=`${fmt(r.memUsed)} of ${fmt(r.memTotal)} (${Math.round(r.memUsed/r.memTotal*100)} %) · committed ${fmt(r.commitUsed)} of ${fmt(r.commitTotal)}`;
  const dpr=window.devicePixelRatio||1,w=cv.clientWidth,h=cv.clientHeight;if(!w)return;if(cv.width!==Math.round(w*dpr)){cv.width=Math.round(w*dpr);cv.height=Math.round(h*dpr)}
  const x=cv.getContext('2d');x.setTransform(dpr,0,0,dpr,0,0);x.clearRect(0,0,w,h);
  x.strokeStyle=cssColor('--border');x.lineWidth=1;x.beginPath();for(let i=1;i<20;i++){const gx=Math.round(w*i/20)+.5;x.moveTo(gx,0);x.lineTo(gx,h)}for(let i=1;i<5;i++){const gy=Math.round(h*i/5)+.5;x.moveTo(0,gy);x.lineTo(w,gy)}x.stroke();
  if(d.length<2)return;const step=w/(HIST-1),x0=w-(d.length-1)*step,y=v=>h-1-v/100*(h-2),c=cssColor('--accent');
  x.beginPath();d.forEach((v,i)=>i?x.lineTo(x0+i*step,y(v)):x.moveTo(x0,y(v)));x.strokeStyle=c;x.lineWidth=1.6;x.stroke();x.lineTo(w,h);x.lineTo(x0,h);x.closePath();x.fillStyle=withAlpha(c,.18);x.fill()}

/* Brand badges drawn from the name (no vendor logos are copied): vendor, family line and tier number on a dark tile. */
function hwBadge(kind,name){
  const n=String(name||''),svg=(accent,l1,l2,l3)=>`<svg class="hwbadge" viewBox="0 0 96 96" width="96" height="96" role="img" aria-label="${esc(n)}"><rect width="96" height="96" rx="10" fill="#15171c"/><rect x="0" y="0" width="96" height="6" rx="3" fill="${accent}"/>`+
    `<text x="48" y="27" text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-size="12" font-weight="700" letter-spacing="1.5" fill="${accent}">${esc(l1)}</text>`+
    `<text x="48" y="${l3?52:60}" text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-size="${l2.length>9?12:l2.length>7?15:20}" font-weight="800" letter-spacing=".5" fill="#fff">${esc(l2)}</text>`+
    (l3?`<rect x="30" y="60" width="36" height="26" rx="7" fill="none" stroke="#fff" stroke-width="2"/><text x="48" y="80" text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-size="19" font-weight="800" fill="#fff">${esc(l3)}</text>`:'')+`</svg>`;
  if(kind==='cpu'){
    if(/Threadripper/i.test(n))return svg('#f26522','AMD','THREADRIPPER','');
    let m=n.match(/Ryzen\s*(?:AI\s*)?(\d)/i);if(m)return svg('#f26522','AMD','RYZEN',m[1]);
    if(/EPYC/i.test(n))return svg('#f26522','AMD','EPYC','');if(/Athlon/i.test(n))return svg('#f26522','AMD','ATHLON','');
    m=n.match(/Core\(?(?:TM)?\)?\s*(Ultra\s*\d|i\d)/i);if(m)return svg('#3d9bf0','intel','CORE',m[1].replace(/ultra\s*/i,'U'));
    if(/Xeon/i.test(n))return svg('#3d9bf0','intel','XEON','');if(/Pentium/i.test(n))return svg('#3d9bf0','intel','PENTIUM','');if(/Celeron/i.test(n))return svg('#3d9bf0','intel','CELERON','');
    if(/Snapdragon|Qualcomm|Oryon/i.test(n))return svg('#3253dc','QUALCOMM','SNAPDRAGON',(n.match(/\b(X\s*Elite|X\s*Plus|8cx|7c)\b/i)||[''])[0].toUpperCase().replace(/\s+/g,' ').slice(0,8));
    if(/Centaur|VIA |Zhaoxin|\bCHA\b/i.test(n))return svg('#8f9bb3','CENTAUR','CHA','');
    return svg('#9aa4b2','CPU',(n.split(/\s+/)[0]||'CPU').toUpperCase().slice(0,8),'')}
  if(/NPU|AI Boost|Hexagon|IPU|Ryzen AI|Neural/i.test(n))return svg('#a78bfa','NPU','AI','');
  if(/Adreno|Qualcomm/i.test(n))return svg('#3253dc','QUALCOMM','ADRENO','');
  if(/NVIDIA|GeForce|RTX|GTX|Quadro/i.test(n))return svg('#76b900','NVIDIA',/RTX/i.test(n)?'GeForce RTX':/GTX/i.test(n)?'GeForce GTX':'GeForce','');
  if(/Radeon|AMD/i.test(n))return svg('#ed1c24','AMD','RADEON','');
  if(/Intel/i.test(n))return svg('#3d9bf0','intel',/Arc/i.test(n)?'ARC':/Iris/i.test(n)?'IRIS Xe':'UHD','');
  return svg('#9aa4b2','GPU',(n.split(/\s+/)[0]||'GPU').toUpperCase().slice(0,8),'')}

/* ---------- data model ---------- */
function hwModel(){
  const d=hw.data;if(!d)return null;const w=d.wmi;
  return{cpus:hwList(w.cpu),cpu:hwOne(w.cpu),bb:hwOne(w.board),bios:hwOne(w.bios),cs:hwOne(w.system),os:hwOne(w.os),mods:hwList(w.memory),gpus:hwList(w.gpu),vram:w.vram||{},
    live:d.live,id:d.cpuid||{},clock:d.clock||{},socket:d.socket,chipset:d.chipset,ucode:d.microcode}}

const hwCacheTxt=c=>`${c.count} × ${hwKB(c.size)}, ${c.ways}-way`;

function hwCards(m){
  const{cpu,cpus,bb,bios,cs,os,mods,gpus,vram,live,id,clock,socket,chipset,ucode}=m,out={};
  const cores=cpus.reduce((a,c)=>a+(c.NumberOfCores||0),0),threads=cpus.reduce((a,c)=>a+(c.NumberOfLogicalProcessors||0),0);
  const base=cpu.MaxClockSpeed||clock.max,now=clock.current||cpu.CurrentClockSpeed;
  const mult=now?Math.round(now/100*10)/10:null;
  const caches=id.caches||[];
  out.cpu=[{title:'Processor',rows:[hwRow('Name',hwTrim(cpu.Name)),hwRow('Code name',id.codename),hwRow('Package',socket),hwRow('Technology',id.tech),hwRow('Specification',id.brand||hwTrim(cpu.Name)),
      hwRow('Family / model / stepping',id.family?`${id.family} / ${id.model} / ${id.stepping}`:null),hwRow('Ext. family / ext. model',id.extFamily?`${id.extFamily} / ${id.extModel}`:null),
      hwRow('Instructions',id.instructions),hwRow('Vendor',cpu.Manufacturer),hwRow('Virtualization (firmware)',cpu.VirtualizationFirmwareEnabled==null?null:cpu.VirtualizationFirmwareEnabled?'Enabled':'Disabled')]},
    {title:'Clocks (core #0)',rows:[['Core speed',now?now.toFixed(2)+' MHz':'—',null],hwRow('Multiplier (est.)',mult?`× ${mult}  (assuming a 100 MHz bus)`:null),hwRow('Rated clock',hwGHz(base))]},
    {title:'Cache',rows:caches.length?caches.map(c=>[c.name,hwCacheTxt(c)]):[hwRow('L2 cache',cpu.L2CacheSize?hwKB(cpu.L2CacheSize*1024):null),hwRow('L3 cache',cpu.L3CacheSize?hwKB(cpu.L3CacheSize*1024):null)]},
    {title:'Selection',rows:[hwRow('Socket','#1'),hwRow('Cores',cores||null),hwRow('Threads',threads||null)],note:'Max TDP and core voltage are read by CPU-Z with its own kernel driver. Windows does not share them, so they are not shown here.'}];
  const boardName=hwTrim(bb.Product);
  out.board=[{title:'Motherboard',rows:[hwRow('Manufacturer',bb.Manufacturer),hwRow('Model',boardName),hwRow('Version',bb.Version),hwRow('Chipset (from the model name)',chipset?'AMD/Intel '+chipset:null),hwRow('Serial number',bb.SerialNumber)]},
    {title:'BIOS',rows:[hwRow('Brand',bios.Manufacturer),hwRow('Version',bios.SMBIOSBIOSVersion||bios.Version),hwRow('Date',bios.ReleaseDate),hwRow('CPU microcode',ucode)]},
    {title:'Computer',rows:[hwRow('Manufacturer',cs.Manufacturer),hwRow('Model',cs.Model),hwRow('System type',cs.SystemType)],note:'The PCI Express link (width and speed) is read through a driver in CPU-Z and is not available to normal apps.'}];
  const total=mods.reduce((a,x)=>a+(+x.Capacity||0),0),first=mods[0]||{},used=live.memUsed,tot=live.memTotal||total,mt=first.ConfiguredClockSpeed||first.Speed;
  out.mem=[{title:'General',rows:[hwRow('Type',MEM_TYPE[first.SMBIOSMemoryType]||(first.SMBIOSMemoryType?'Type '+first.SMBIOSMemoryType:null)),hwRow('Size',tot?fmt(tot):null),
      hwRow('Channels (est.)',mods.length?(mods.length>=2?'Dual':'Single')+` — ${mods.length} module${mods.length===1?'':'s'}`:null),hwRow('DRAM frequency',mt?(mt/2)+' MHz  ('+mt+' MT/s)':null),
      hwRow('In use now',tot?`${fmt(used)} (${Math.round(used/tot*100)} %)`:null),hwRow('Page file',live.pageTotal?fmt(live.pageTotal):null)],bar:tot?used/tot:null,
    note:'CAS latency, tRCD, tRP, tRAS and the other timings are read from the memory controller by CPU-Z\'s driver, so Windows apps cannot show them.'}];
  const mod=mods[Math.min(hw.spd,Math.max(0,mods.length-1))]||{},mm=mod.ConfiguredClockSpeed||mod.Speed,ty=MEM_TYPE[mod.SMBIOSMemoryType]||'';
  out.spd=[{title:'Module detail (SPD)',slots:mods.map((x,i)=>`Slot #${i+1}  ${x.DeviceLocator||x.BankLabel||''}`),rows:[hwRow('Type',ty&&`${ty} (${MEM_FORM[mod.FormFactor]||'module'})`),hwRow('Module size',mod.Capacity?fmt(+mod.Capacity):null),
      hwRow('Max bandwidth',mm?`${ty}-${mm} (${mm/2} MHz)`:null),hwRow('Module manufacturer',memMakerJS(mod.Manufacturer)),hwRow('Part number',hwTrim(mod.PartNumber)),hwRow('Rated speed',mod.Speed?mod.Speed+' MT/s':null),
      hwRow('Running at',mod.ConfiguredClockSpeed?mod.ConfiguredClockSpeed+' MT/s':null),hwRow('Voltage',mod.ConfiguredVoltage?(mod.ConfiguredVoltage/1000).toFixed(3)+' V':null),hwRow('Serial number',mod.SerialNumber)],
    note:'JEDEC and XMP timing tables, ranks and the DRAM manufacturer sit in the module\'s SPD chip, which Windows does not expose to normal apps.'}];
  out.gpu=gpus.map(g=>{const vr=vram[g.Name]||g.AdapterRAM;return{title:hwTrim(g.Name),rows:[hwRow('Name',hwTrim(g.Name)),hwRow('Chip',g.VideoProcessor),hwRow('Video memory',vr?fmt(vr):null),hwRow('Driver version',g.DriverVersion),hwRow('Driver date',g.DriverDate),
    hwRow('Resolution',g.CurrentHorizontalResolution?`${g.CurrentHorizontalResolution} × ${g.CurrentVerticalResolution} @ ${g.CurrentRefreshRate||'?'} Hz`:null)]}});
  const up=os.LastBoot?Math.max(0,(Date.now()-new Date(os.LastBoot.replace(' ','T')).getTime())/86400000):null;
  out.sys=[{title:'Operating system',rows:[hwRow('Edition',hwTrim(os.Caption)),hwRow('Version',os.Version?`${os.Version} (build ${os.BuildNumber})`:null),hwRow('Architecture',os.OSArchitecture),hwRow('Installed',os.InstallDate),hwRow('Last start',os.LastBoot),
    hwRow('Uptime',up==null?null:up>=1?up.toFixed(1)+' days':Math.round(up*24)+' hours')]},
    {title:'This app',rows:[hwRow('Logical processors',live.logical),hwRow('App architecture',live.goarch)]}];
  out.memTable=mods;out.coreCount=cores;out.threadCount=threads;out.caches=caches;out.base=base;
  return out}

const JEDEC={'029E':'Corsair','859B':'Crucial','802C':'Micron','80AD':'SK hynix','CE80':'Samsung','04CD':'G.Skill','0198':'Kingston','9801':'Kingston','2C00':'Micron','AD80':'SK hynix','80CE':'Samsung','CD04':'G.Skill','9E02':'Corsair','9B85':'Crucial'};
const memMakerJS=s=>{const t=hwTrim(s);return JEDEC[t.toUpperCase()]||t};

/* ---------- graphics (every adapter) and network ---------- */
function hwGpuView(){
  const wmi=hwList(hw.data.wmi.gpu),npus=hwList(hw.data.wmi.npu),vram=hw.data.wmi.vram||{};
  const list=hw.gpus.filter(a=>a.kind!=='software');const soft=hw.gpus.filter(a=>a.kind==='software');
  const known=new Set(hw.gpus.map(a=>hwTrim(a.name)));
  const extra=wmi.filter(g=>!known.has(hwTrim(g.Name))&&!/Basic (Display|Render)/i.test(g.Name||'')).map(g=>({name:g.Name,vendor:'',dedicated:vram[g.Name]||g.AdapterRAM||0,shared:0,luid:'',kind:'gpu'}));
  const all=[...list,...extra];
  const dxLuids=new Set(hw.gpus.map(a=>a.luid));
  const npuLuids=hw.perf?hw.perf.gpu.filter(g=>!dxLuids.has(g.luid)).map(g=>g.luid):[];
  let html='';
  if(!all.length)html+='<div class="card"><div class="empty">No graphics adapter reported.</div></div>';
  all.forEach((a,i)=>{
    const w=wmi.find(g=>hwTrim(g.Name)===hwTrim(a.name))||{};
    const rows=[hwRow('Type',gpuKindName[a.kind]),hwRow('Vendor',a.vendor),hwRow('Dedicated video memory',a.dedicated?fmt(a.dedicated):'none (uses system memory)'),hwRow('Shared system memory',a.shared?fmt(a.shared):null),hwRow('Driver version',w.DriverVersion),hwRow('Driver date',w.DriverDate),
      hwRow('Resolution',w.CurrentHorizontalResolution?`${w.CurrentHorizontalResolution} × ${w.CurrentVerticalResolution} @ ${w.CurrentRefreshRate||'?'} Hz`:null)];
    html+=`<div class="card hwid hwgpu">${hwBadge('gpu',a.name)}<div class="hwidtxt"><h2>${esc(hwTrim(a.name))}</h2><p>${esc(gpuKindName[a.kind])}${a.dedicated?' · '+fmt(a.dedicated):''}</p></div></div>`+
      (a.luid?`<div class="card"><div class="chhead"><h3>Load</h3><span id="gt-${i}" class="hint" style="margin:0">…</span></div><canvas class="hwchart" data-ch="gutil" data-id="${a.luid}" data-n="${i}"></canvas>
        ${a.dedicated?`<div class="chhead" style="margin-top:12px"><h3>Video memory</h3><span id="gm-${i}" class="hint" style="margin:0">…</span></div><canvas class="hwchart" data-ch="gmem" data-id="${a.luid}" data-n="${i}" data-total="${a.dedicated}"></canvas>`:''}</div>`:'')+
      `<div class="card"><div class="kv hwkv">${kvHTML(rows)}</div></div>`});
  if(npus.length)html+=`<div class="card hwid hwgpu"><div class="hwbadge hwnpubadge">NPU</div><div class="hwidtxt"><h2>AI accelerator (NPU)</h2><p>${esc(npus.map(n=>hwTrim(n.Name)).join(', '))}</p></div></div>`;
  if(npus.length){
    if(npuLuids.length)html+=npuLuids.map((l,j)=>`<div class="card"><div class="chhead"><h3>NPU load</h3><span id="gt-n${j}" class="hint" style="margin:0">…</span></div><canvas class="hwchart" data-ch="gutil" data-id="${l}" data-n="n${j}"></canvas></div>`).join('');
    else html+='<div class="card"><div class="hint" style="margin:0">Windows shows no load counter for this NPU yet. It appears once an app uses it.</div></div>'}
  if(soft.length)html+=`<p class="hint" style="margin:8px 4px">Also present: ${esc(soft.map(s=>hwTrim(s.name)).join(', '))} (software renderer, not a real GPU).</p>`;
  return html}

function hwNetView(){
  const nets=hw.nets.filter(a=>a.type!=='Loopback'&&a.status==='Up'),perf=hw.perf?hw.perf.net:[];
  if(!nets.length)return'<div class="card"><div class="empty">No connected network adapters.</div></div>';
  let html='';
  nets.forEach((a,i)=>{
    const inst=perf.find(n=>normNet(n.name)===normNet(a.description))||perf.find(n=>normNet(n.name).includes(normNet(a.description).slice(0,24)));
    const v4=a.addrs.filter(x=>!x.v6).map(x=>x.ip).join(', ');
    html+=`<div class="card"><div class="chhead"><h3>${esc(a.name)}</h3><span class="hint" style="margin:0">${esc(a.description)}</span></div>
      <div id="nt-${i}" class="hwnetline">…</div>${inst?`<canvas class="hwchart" data-ch="nrx" data-id="${esc(inst.name)}" data-n="${i}"></canvas>`:'<div class="hint">No throughput counter for this adapter.</div>'}
      <div class="kv hwkv">${kvHTML([hwRow('Type',a.type),hwRow('Link speed',a.speed?(a.speed>=1e9?(a.speed/1e9)+' Gbps':Math.round(a.speed/1e6)+' Mbps'):null),hwRow('IPv4',v4),hwRow('Gateway',a.gateways.join(', ')),hwRow('MAC',a.mac)])}</div></div>`});
  return html}

/* ---------- rendering ---------- */
function hwLiveCard(c){
  const total=(n)=>c.caches.filter(x=>x.name.startsWith(n)).reduce((a,x)=>a+x.size*x.count,0);
  const l1=total('L1'),l2=total('Level 2'),l3=total('Level 3');
  return`<div class="card" id="hwlive"><div class="chhead"><h3>Live load</h3><span class="hint" style="margin:0">% utilisation over 60 seconds, one graph per logical processor</span></div>
   <div class="hwtm"><div class="hwstats"><div><small>Utilisation</small><b id="hl-util">—</b></div><div><small>Speed</small><b id="hl-speed">—</b></div><div><small>Processes</small><b id="hl-proc">—</b></div><div><small>Threads</small><b id="hl-thr">—</b></div><div><small>Handles</small><b id="hl-hnd">—</b></div><div><small>Up time</small><b id="hl-up">—</b></div></div>
   <div class="hwgraphs" id="hwgraphs"></div>
   <div class="hwside"><div><span>Base speed</span><b>${c.base?(c.base/1000).toFixed(2)+' GHz':'—'}</b></div><div><span>Sockets</span><b>1</b></div><div><span>Cores</span><b>${c.coreCount||'—'}</b></div><div><span>Logical processors</span><b>${c.threadCount||'—'}</b></div>
   <div><span>L1 cache</span><b>${l1?hwKB(l1):'—'}</b></div><div><span>L2 cache</span><b>${l2?hwKB(l2):'—'}</b></div><div><span>L3 cache</span><b>${l3?hwKB(l3):'—'}</b></div></div></div></div>`}

function identityCard(c){const m=hwModel(),cpu=m.cpu,id=m.id;
  return`<div class="card hwid">${hwBadge('cpu',cpu.Name||id.brand)}<div class="hwidtxt"><h2>${esc(hwTrim(cpu.Name)||'Processor')}</h2><p>${esc([id.codename,m.socket,id.tech,c.coreCount?c.coreCount+' cores · '+c.threadCount+' threads':''].filter(Boolean).join('  ·  '))}</p></div></div>`}
function memChartCard(){return`<div class="card"><div class="chhead"><h3>Memory in use</h3><span class="hint" id="hwmemtxt" style="margin:0"></span></div><canvas id="hwmemchart" class="hwmemchart"></canvas></div>`}
function hwRender(){
  if(!hw.data)return;
  $$('#hwtabs button').forEach(b=>b.classList.toggle('on',b.dataset.t===hw.tab));
  const c=hwCards(hwModel());let html='';
  const card=x=>`<div class="card"><div class="chhead"><h3>${esc(x.title)}</h3>${x.slots?`<select id="hwslot" aria-label="Memory slot">${x.slots.map((s,i)=>`<option value="${i}"${i===hw.spd?' selected':''}>${esc(s)}</option>`).join('')}</select>`:''}</div>${x.bar!=null?`<div class="ubar hwbar"><i style="width:${Math.min(100,x.bar*100).toFixed(1)}%"></i></div>`:''}<div class="kv hwkv">${kvHTML(x.rows)}</div>${x.note?`<p class="hint hwnote">${esc(x.note)}</p>`:''}</div>`;
  if(hw.tab==='mem'){
    html=memChartCard()+c.mem.map(card).join('')+c.spd.map(card).join('')+`<div class="card flush"><div class="chhead" style="padding:12px 16px 0"><h3>Memory modules</h3><span class="hint" style="margin:0">${c.memTable.length} slot${c.memTable.length===1?'':'s'} populated</span></div><div class="tblwrap"><table class="tbl"><thead><tr><th>Slot</th><th class="r">Size</th><th>Type</th><th class="r">Speed</th><th>Manufacturer</th><th>Part number</th></tr></thead><tbody>`+
      (c.memTable.map(x=>`<tr><td>${esc(x.DeviceLocator||x.BankLabel||'—')}</td><td class="r">${x.Capacity?fmt(+x.Capacity):'—'}</td><td>${esc(MEM_TYPE[x.SMBIOSMemoryType]||'—')} ${esc(MEM_FORM[x.FormFactor]||'')}</td><td class="r">${x.ConfiguredClockSpeed||x.Speed?(x.ConfiguredClockSpeed||x.Speed)+' MT/s':'—'}</td><td>${esc(memMakerJS(x.Manufacturer)||'—')}</td><td class="mono">${esc(hwTrim(x.PartNumber)||'—')}</td></tr>`).join('')||'<tr><td colspan="6" class="empty">Windows did not list the memory modules.</td></tr>')+'</tbody></table></div></div>'}
  else if(hw.tab==='cpu')html=identityCard(c)+hwLiveCard(c)+c.cpu.map(card).join('');
  else if(hw.tab==='gpu')html=hwGpuView();
  else if(hw.tab==='net')html=hwNetView();
  else html=(c[hw.tab]||[]).map(card).join('')||'<div class="card"><div class="empty">Nothing reported for this part.</div></div>';
  $('#hwbody').innerHTML=html;
  if(hw.tab==='cpu'||hw.tab==='mem'){hwUpdateLive();hwDrawMem();hwTick()}
  if(hw.tab==='gpu'||hw.tab==='net'){hwDrawPerf();hwPerfTick()}}

function hwReport(){
  if(!hw.data)return'';const c=hwCards(hwModel()),L=['My Tool Box — hardware report',new Date().toLocaleString(),''];
  const sect=(t,rows)=>{L.push('['+t+']');rows.filter(r=>!/serial/i.test(r[0])).forEach(r=>L.push(`  ${r[0].padEnd(28)} ${r[1]}`));L.push('')};
  c.cpu.forEach(x=>sect(x.title,x.rows));c.board.forEach(x=>sect(x.title,x.rows));c.mem.forEach(x=>sect('Memory',x.rows));
  c.memTable.forEach((x,i)=>L.push(`  Module ${i+1}: ${x.DeviceLocator||''} ${x.Capacity?fmt(+x.Capacity):''} ${MEM_TYPE[x.SMBIOSMemoryType]||''} ${x.ConfiguredClockSpeed||x.Speed||''} MT/s ${memMakerJS(x.Manufacturer)} ${hwTrim(x.PartNumber)}`));L.push('');
  c.gpu.forEach(x=>sect('Graphics',x.rows));c.sys.forEach(x=>sect(x.title,x.rows));return L.join('\n')}
