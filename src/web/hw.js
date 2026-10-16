// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Hardware info (CPU-Z style): processor, mainboard and BIOS, memory modules, graphics, system. Shares the helpers from index.html / tools.js.
"use strict";
TOOL_NAMES.hw='Hardware info';

const hw={data:null,tab:'cpu',loading:false};
const HW_TABS=[['cpu','Processor'],['board','Mainboard'],['mem','Memory'],['gpu','Graphics'],['sys','System']];
const MEM_TYPE={20:'DDR',21:'DDR2',24:'DDR3',26:'DDR4',34:'DDR5',35:'LPDDR5'};
const MEM_FORM={8:'DIMM',12:'SODIMM',13:'SRIMM'};
const CPU_ARCH={0:'x86',5:'ARM',9:'x64',12:'ARM64'};
const hwGHz=m=>m?(m>=1000?(m/1000).toFixed(m%100?2:1).replace(/\.?0+$/,'')+' GHz':m+' MHz'):'—';
const hwTrim=s=>String(s??'').replace(/\s+/g,' ').trim();
const hwOne=a=>Array.isArray(a)?a[0]||{}:a||{};
const hwList=a=>Array.isArray(a)?a:a?[a]:[];
const hwRow=(k,v)=>[k,v==null||v===''?'—':String(v)];

toolInit.hw=()=>{
  $('#hwtabs').innerHTML=HW_TABS.map(([k,l])=>`<button data-t="${k}"${k===hw.tab?' class="on"':''}>${l}</button>`).join('');
  segInit('#hwtabs',b=>{hw.tab=b.dataset.t;hwRender()});
  $('#hwref').onclick=()=>hwLoad(true);
  $('#hwcopy').onclick=()=>{const t=hwReport();if(t)copyText(t);else toast('Nothing to copy yet')};
  addEventListener('keydown',e=>{if(e.key==='F5'&&curTool==='hw'){e.preventDefault();hwLoad(true)}});
  hwLoad()};

async function hwLoad(fresh){if(hw.loading)return;hw.loading=true;$('#hwref').disabled=true;$('#s3').textContent='Reading hardware…';
  try{hw.data=await api('/api/hw'+(fresh?'?fresh=1':''));hwRender();$('#s3').textContent='Read '+new Date().toLocaleTimeString()}
  catch(e){$('#hwbody').innerHTML=`<div class="card"><div class="err">${esc(e.message)}</div></div>`}
  hw.loading=false;$('#hwref').disabled=false}

function hwModel(){
  const d=hw.data;if(!d)return null;const w=d.wmi,live=d.live;
  const cpus=hwList(w.cpu),cpu=hwOne(w.cpu),bb=hwOne(w.board),bios=hwOne(w.bios),cs=hwOne(w.system),os=hwOne(w.os),mods=hwList(w.memory),gpus=hwList(w.gpu);
  const vram=w.vram||{};
  return{cpus,cpu,bb,bios,cs,os,mods,gpus,vram,live}}

function hwCards(m){
  const{cpu,cpus,bb,bios,cs,os,mods,gpus,vram,live}=m,out={};
  const cores=cpus.reduce((a,c)=>a+(c.NumberOfCores||0),0),threads=cpus.reduce((a,c)=>a+(c.NumberOfLogicalProcessors||0),0);
  out.cpu=[{title:hwTrim(cpu.Name)||'Processor',rows:[hwRow('Name',hwTrim(cpu.Name)),hwRow('Vendor',cpu.Manufacturer),hwRow('Cores / threads',cores?`${cores} cores · ${threads} threads`:null),
    hwRow('Base clock',hwGHz(cpu.MaxClockSpeed)),hwRow('Current clock',hwGHz(cpu.CurrentClockSpeed)),hwRow('Socket',cpu.SocketDesignation),hwRow('Architecture',CPU_ARCH[cpu.Architecture]||cpu.Architecture),
    hwRow('L2 cache',cpu.L2CacheSize?fmt(cpu.L2CacheSize*1024):null),hwRow('L3 cache',cpu.L3CacheSize?fmt(cpu.L3CacheSize*1024):null),hwRow('Virtualization',cpu.VirtualizationFirmwareEnabled==null?null:cpu.VirtualizationFirmwareEnabled?'Enabled in firmware':'Disabled'),
    hwRow('Family / model',cpu.Description),hwRow('Processor ID',cpu.ProcessorId),hwRow('Revision',cpu.Revision)]}];
  out.board=[{title:'Mainboard',rows:[hwRow('Manufacturer',bb.Manufacturer),hwRow('Model',bb.Product),hwRow('Version',bb.Version),hwRow('Serial number',bb.SerialNumber)]},
    {title:'BIOS / UEFI',rows:[hwRow('Vendor',bios.Manufacturer),hwRow('Version',bios.SMBIOSBIOSVersion||bios.Version),hwRow('Release date',bios.ReleaseDate)]},
    {title:'Computer',rows:[hwRow('Manufacturer',cs.Manufacturer),hwRow('Model',cs.Model),hwRow('System type',cs.SystemType)]}];
  const total=mods.reduce((a,x)=>a+(+x.Capacity||0),0),first=mods[0]||{};
  const used=live.memUsed,tot=live.memTotal||total;
  out.mem=[{title:'Memory',rows:[hwRow('Installed',tot?fmt(tot):null),hwRow('In use now',tot?`${fmt(used)} (${Math.round(used/tot*100)} %)`:null),hwRow('Type',MEM_TYPE[first.SMBIOSMemoryType]||(first.SMBIOSMemoryType?'Type '+first.SMBIOSMemoryType:null)),
    hwRow('Speed',first.ConfiguredClockSpeed||first.Speed?(first.ConfiguredClockSpeed||first.Speed)+' MT/s':null),hwRow('Modules',mods.length?`${mods.length} installed`:null),hwRow('Page file',live.pageTotal?fmt(live.pageTotal):null)],bar:tot?used/tot:null}];
  out.memTable=mods;
  out.gpu=gpus.map(g=>{const vr=vram[g.Name]||g.AdapterRAM;return{title:hwTrim(g.Name),rows:[hwRow('Name',hwTrim(g.Name)),hwRow('Chip',g.VideoProcessor),hwRow('Video memory',vr?fmt(vr):null),hwRow('Driver version',g.DriverVersion),hwRow('Driver date',g.DriverDate),
    hwRow('Resolution',g.CurrentHorizontalResolution?`${g.CurrentHorizontalResolution} × ${g.CurrentVerticalResolution} @ ${g.CurrentRefreshRate||'?'} Hz`:null)]}});
  const up=os.LastBoot?Math.max(0,(Date.now()-new Date(os.LastBoot.replace(' ','T')).getTime())/86400000):null;
  out.sys=[{title:'Operating system',rows:[hwRow('Edition',hwTrim(os.Caption)),hwRow('Version',os.Version?`${os.Version} (build ${os.BuildNumber})`:null),hwRow('Architecture',os.OSArchitecture),hwRow('Installed',os.InstallDate),hwRow('Last start',os.LastBoot),
    hwRow('Uptime',up==null?null:up>=1?up.toFixed(1)+' days':Math.round(up*24)+' hours')]},
    {title:'This app',rows:[hwRow('Logical processors',live.logical),hwRow('App architecture',live.goarch)]}];
  return out}

function hwRender(){
  if(!hw.data)return;
  $$('#hwtabs button').forEach(b=>b.classList.toggle('on',b.dataset.t===hw.tab));
  const c=hwCards(hwModel());let html='';
  const card=x=>`<div class="card"><div class="chhead"><h3>${esc(x.title)}</h3></div>${x.bar!=null?`<div class="ubar hwbar"><i style="width:${Math.min(100,x.bar*100).toFixed(1)}%"></i></div>`:''}<div class="kv hwkv">${kvHTML(x.rows)}</div></div>`;
  if(hw.tab==='mem'){
    html=c.mem.map(card).join('')+`<div class="card flush"><div class="chhead" style="padding:12px 16px 0"><h3>Memory modules</h3><span class="hint" style="margin:0">${c.memTable.length} slot${c.memTable.length===1?'':'s'} populated</span></div><div class="tblwrap"><table class="tbl"><thead><tr><th>Slot</th><th class="r">Size</th><th>Type</th><th class="r">Speed</th><th>Manufacturer</th><th>Part number</th></tr></thead><tbody>`+
      (c.memTable.map(x=>`<tr><td>${esc(x.DeviceLocator||x.BankLabel||'—')}</td><td class="r">${x.Capacity?fmt(+x.Capacity):'—'}</td><td>${esc(MEM_TYPE[x.SMBIOSMemoryType]||'—')} ${esc(MEM_FORM[x.FormFactor]||'')}</td><td class="r">${x.ConfiguredClockSpeed||x.Speed?(x.ConfiguredClockSpeed||x.Speed)+' MT/s':'—'}</td><td>${esc(hwTrim(x.Manufacturer)||'—')}</td><td class="mono">${esc(hwTrim(x.PartNumber)||'—')}</td></tr>`).join('')||'<tr><td colspan="6" class="empty">Windows did not list the memory modules.</td></tr>')+'</tbody></table></div></div>'}
  else html=(c[hw.tab]||[]).map(card).join('')||'<div class="card"><div class="empty">Nothing reported for this part.</div></div>';
  $('#hwbody').innerHTML=html}

function hwReport(){
  if(!hw.data)return'';const c=hwCards(hwModel()),L=['My Tool Box — hardware report',new Date().toLocaleString(),''];
  const sect=(t,rows)=>{L.push('['+t+']');rows.filter(r=>!/serial/i.test(r[0])).forEach(r=>L.push(`  ${r[0].padEnd(20)} ${r[1]}`));L.push('')};
  c.cpu.forEach(x=>sect('Processor',x.rows));c.board.forEach(x=>sect(x.title,x.rows));c.mem.forEach(x=>sect('Memory',x.rows));
  c.memTable.forEach((x,i)=>L.push(`  Module ${i+1}: ${x.DeviceLocator||''} ${x.Capacity?fmt(+x.Capacity):''} ${MEM_TYPE[x.SMBIOSMemoryType]||''} ${x.ConfiguredClockSpeed||x.Speed||''} MT/s ${hwTrim(x.Manufacturer)} ${hwTrim(x.PartNumber)}`));L.push('');
  c.gpu.forEach(x=>sect('Graphics',x.rows));c.sys.forEach(x=>sect(x.title,x.rows));return L.join('\n')}
