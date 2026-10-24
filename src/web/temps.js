// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Temperature monitor: a live graph for every sensor Windows shares (thermal zones, disks, NVIDIA GPUs). Uses hwLineChart from hw.js.
"use strict";
TOOL_NAMES.temps='Temperatures';

const tp={list:null,hist:{},shown:-1,busy:false};
const TP_SKEL=document.getElementById('tpbody').innerHTML;
const tpKey=t=>t.group+'|'+t.name;
const tpLevel=t=>{const hi=t.crit>0?t.crit:100,wn=t.warn>0?t.warn:hi-15;return t.c>=hi-5?'--badc':t.c>=wn-10?'--caution':'--good'};

toolInit.temps=()=>{
  $('#tpref').onclick=()=>{tp.shown=-1;tpSkeleton();tpTick(true)};
  addEventListener('resize',debounce(()=>{if(curTool==='temps')tpDraw()},150));
  setInterval(()=>tpTick(),1000);
  tpTick()};

async function tpTick(force){
  if(curTool!=='temps'||(document.hidden&&!force)||tp.busy)return;
  tp.busy=true;
  try{const r=await api('/api/hw/temps');tp.list=r.temps||[];
    tp.list.forEach(t=>pushH(tp.hist,tpKey(t),t.c));
    if(tp.shown!==tp.list.length){tp.shown=tp.list.length;tpRender()}
    tpDraw();$('#s3').textContent='Read '+new Date().toLocaleTimeString()}
  catch(e){$('#tpbody').innerHTML=`<div class="card"><div class="err">${esc(e.message)}</div></div>`}
  tp.busy=false}

function tpSkeleton(){$('#tpbody').innerHTML=TP_SKEL}
function tpRender(){
  const L=tp.list;
  if(!L.length){$('#tpbody').innerHTML='<div class="card"><div class="empty">Windows reports no temperature sensors for this PC.</div></div>';return}
  let html='<div class="tpgrid">';
  for(const g of [...new Set(L.map(t=>t.group))]){
    html+=`<h3 class="tpgroup">${esc(g)}</h3>`+L.map((t,i)=>t.group!==g?'':
      `<div class="card tpcard"><div class="chhead"><h3>${esc(t.name)}</h3><b class="tpval" id="tv-${i}">—</b></div><div class="ubar tpbar"><i id="tb-${i}" style="width:0"></i></div><canvas class="hwchart" id="tc-${i}"></canvas><div class="hint" id="tm-${i}" style="margin:4px 0 0"></div></div>`).join('')}
  html+='</div><p class="hint" style="margin:8px 4px">Shown: ACPI thermal zones, disks and NVIDIA GPUs. Per-core CPU temperature and AMD / Intel GPU sensors need a kernel driver, which Windows does not share with normal apps.</p>';
  $('#tpbody').innerHTML=html}

function tpDraw(){
  const L=tp.list;if(!L)return;
  L.forEach((t,i)=>{const b=$('#tb-'+i),v=$('#tv-'+i),cv=$('#tc-'+i),m=$('#tm-'+i);if(!b)return;
    const col=cssColor(tpLevel(t)),h=tp.hist[tpKey(t)]||[];
    b.style.width=Math.min(100,Math.max(0,t.c)).toFixed(0)+'%';b.style.background=col;v.textContent=t.c.toFixed(0)+' °C';v.style.color=col;
    if(cv)hwLineChart(cv,[{d:h,color:col}],110);
    if(m&&h.length)m.textContent=`min ${Math.min(...h).toFixed(0)} · max ${Math.max(...h).toFixed(0)} °C`+(t.crit>0?` · limit ${t.crit} °C`:'')})}
