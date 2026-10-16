// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Image tools: image to Base64 (and back) and image to .ico. Everything runs in the browser, nothing is uploaded.
"use strict";
Object.assign(TOOL_NAMES,{img64:'Image to Base64',ico:'Image to ICO'});

/* ---------- shared: drop zone, file picker, paste ---------- */
function imageDrop(tool,zone,pick,input,onFile){
  const z=$(zone),take=f=>{if(!f)return;if(!/^image\//.test(f.type)&&!/\.(ico|svg|png|jpe?g|gif|webp|bmp|avif)$/i.test(f.name))return toast('That file is not an image');onFile(f)};
  $(pick).onclick=e=>{e.stopPropagation();$(input).click()};
  z.onclick=()=>$(input).click();
  z.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();$(input).click()}});
  $(input).onchange=e=>{take(e.target.files[0]);e.target.value=''};
  for(const ev of['dragenter','dragover'])z.addEventListener(ev,e=>{e.preventDefault();z.classList.add('over')});
  for(const ev of['dragleave','drop'])z.addEventListener(ev,e=>{e.preventDefault();z.classList.remove('over')});
  z.addEventListener('drop',e=>take(e.dataTransfer.files[0]));
  addEventListener('paste',e=>{if(curTool!==tool||/TEXTAREA|INPUT/.test(document.activeElement.tagName))return;
    const it=[...(e.clipboardData?.items||[])].find(i=>i.type.startsWith('image/'));if(it){e.preventDefault();take(it.getAsFile())}})}
const baseName=n=>(n||'image').replace(/\.[^.]*$/,'')||'image';
function saveBlob(name,blob){const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=name;document.body.appendChild(a);a.click();setTimeout(()=>{URL.revokeObjectURL(a.href);a.remove()},1000)}
function loadImage(src){return new Promise((res,rej)=>{const i=new Image();i.onload=()=>res(i);i.onerror=()=>rej(new Error('The browser could not read this image'));i.src=src})}
// the image type from the first bytes, for Base64 that arrives without a data: prefix
function sniffMime(b){const h=(...s)=>s.every((x,i)=>b[i]===x);
  if(h(0x89,0x50,0x4e,0x47))return'image/png';if(h(0xff,0xd8,0xff))return'image/jpeg';if(h(0x47,0x49,0x46,0x38))return'image/gif';
  if(h(0x52,0x49,0x46,0x46)&&b[8]===0x57&&b[9]===0x45)return'image/webp';if(h(0x42,0x4d))return'image/bmp';if(h(0,0,1,0))return'image/x-icon';
  if(new TextDecoder().decode(b.subarray(0,256)).match(/<svg[\s>]/i))return'image/svg+xml';return''}
const EXT={'image/png':'png','image/jpeg':'jpg','image/gif':'gif','image/webp':'webp','image/bmp':'bmp','image/svg+xml':'svg','image/x-icon':'ico','image/vnd.microsoft.icon':'ico','image/avif':'avif'};

/* ================= Image to Base64 ================= */
const i64={mime:'',b64:'',name:'image',bytes:0,blob:null,w:0,h:0};
toolInit.img64=()=>{
  imageDrop('img64','#i64drop','#i64pick','#i64file',i64Load);
  $('#i64fmt').onchange=i64Out;
  $('#i64save').onclick=()=>{if(!i64.b64)return toast('Nothing to save');downloadText(baseName(i64.name)+'.base64.txt',$('#i64out').value,'text/plain')};
  $('#i64dl').onclick=()=>{if(i64.blob)saveBlob(baseName(i64.name)+'.'+(EXT[i64.mime]||'img'),i64.blob)};
  $('#i64in').addEventListener('input',debounce(i64Decode,200))};
async function i64Load(f){
  $('#i64err').textContent='';$('#i64in').value='';
  try{const buf=new Uint8Array(await f.arrayBuffer());
    i64.name=f.name||'image';i64.bytes=buf.length;i64.b64=b64(buf);i64.blob=f;
    i64.mime=f.type||sniffMime(buf)||'application/octet-stream';
    await i64Show(URL.createObjectURL(f));i64Out()}
  catch(e){$('#i64err').textContent=e.message}}
async function i64Show(url){const img=$('#i64prev');
  try{const im=await loadImage(url);i64.w=im.naturalWidth;i64.h=im.naturalHeight;img.src=url;img.style.display='block';$('#i64none').style.display='none';$('#i64dl').disabled=false}
  catch(e){img.style.display='none';$('#i64none').style.display='';$('#i64dl').disabled=true;throw e}}
function i64Out(){if(!i64.b64){$('#i64out').value='';$('#i64info').textContent='';return}
  const uri=`data:${i64.mime};base64,${i64.b64}`,k=$('#i64fmt').value;
  $('#i64out').value=k==='raw'?i64.b64:k==='css'?`background-image: url("${uri}");`:k==='img'?`<img src="${uri}" alt="" width="${i64.w}" height="${i64.h}">`:uri;
  $('#i64info').textContent=`${i64.mime} · ${i64.w}×${i64.h} px · ${fmt(i64.bytes)} → ${fmt($('#i64out').value.length)} as text (about ${Math.round(($('#i64out').value.length/Math.max(1,i64.bytes)-1)*100)}% larger)`}
async function i64Decode(){const t=$('#i64in').value.trim();$('#i64err').textContent='';if(!t)return;
  try{let mime='',data=t;const m=t.match(/^data:([^;,]*)(?:;[^,]*)?,/i);
    if(m){mime=m[1].toLowerCase();data=t.slice(m[0].length)}
    else{const u=t.match(/url\(\s*["']?(data:[^"')]+)["']?\s*\)/i)||t.match(/src=["'](data:[^"']+)["']/i);if(u){$('#i64in').value=u[1];return i64Decode()}}
    const bytes=unb64(decodeURIComponent(data.replace(/%(?![0-9a-f]{2})/gi,'%25')));
    mime=sniffMime(bytes)||mime;if(!mime)throw new Error('This Base64 is not a recognised image (PNG, JPG, GIF, WebP, BMP, SVG or ICO)');
    const blob=new Blob([bytes],{type:mime});
    Object.assign(i64,{mime,b64:b64(bytes),bytes:bytes.length,blob,name:'decoded'});
    await i64Show(URL.createObjectURL(blob));i64Out()}
  catch(e){$('#i64err').textContent=e.message}}

/* ================= Image to ICO ================= */
const ICO_SIZES=[16,24,32,48,64,96,128,256],ICO_DEFAULT=[16,32,48,64,128,256];
const ico={img:null,name:'icon',bytes:null,gen:0};
toolInit.ico=()=>{
  $('#icosizes').innerHTML=ICO_SIZES.map(s=>`<label><input type="checkbox" value="${s}"${ICO_DEFAULT.includes(s)?' checked':''}>${s}</label>`).join('');
  imageDrop('ico','#icodrop','#icopick','#icofile',icoLoad);
  const re=debounce(icoBuild,120);
  $('#icosizes').addEventListener('change',re);$('#icofit').onchange=re;$('#icobg').onchange=re;
  $('#icodl').onclick=()=>{if(ico.bytes)saveBlob(baseName(ico.name)+'.ico',new Blob([ico.bytes],{type:'image/x-icon'}))}};
async function icoLoad(f){$('#icoerr').textContent='';
  try{const url=URL.createObjectURL(f);ico.img=await loadImage(url);ico.name=f.name;
    if(!ico.img.naturalWidth)throw new Error('The image has no size');
    icoBuild()}
  catch(e){ico.img=null;$('#icoerr').textContent=e.message}}
// draws the source into a size×size canvas; big reductions go in halving steps so small icons stay sharp
function icoRender(size){
  const im=ico.img,fit=$('#icofit').value,bg=$('#icobg').value,sw=im.naturalWidth||256,sh=im.naturalHeight||256;
  let src=im,cw=sw,ch=sh;
  while(cw/2>=size*1.5&&ch/2>=size*1.5){const c=document.createElement('canvas');c.width=cw=Math.floor(cw/2);c.height=ch=Math.floor(ch/2);const x=c.getContext('2d');x.imageSmoothingQuality='high';x.drawImage(src,0,0,cw,ch);src=c}
  const c=document.createElement('canvas');c.width=c.height=size;const x=c.getContext('2d');x.imageSmoothingEnabled=true;x.imageSmoothingQuality='high';
  if(bg){x.fillStyle=bg;x.fillRect(0,0,size,size)}
  let dx=0,dy=0,dw=size,dh=size,sx=0,sy=0,sW=cw,sH=ch;
  if(fit==='contain'){const k=Math.min(size/cw,size/ch);dw=Math.round(cw*k);dh=Math.round(ch*k);dx=(size-dw)>>1;dy=(size-dh)>>1}
  else if(fit==='cover'){const k=Math.min(cw,ch);sW=sH=k;sx=(cw-k)>>1;sy=(ch-k)>>1}
  x.drawImage(src,sx,sy,sW,sH,dx,dy,dw,dh);return c}
const canvasPNG=async c=>unb64(c.toDataURL('image/png').split(',')[1]);
async function icoBuild(){
  const sizes=[...document.querySelectorAll('#icosizes input:checked')].map(i=>+i.value).sort((a,b)=>a-b);
  $('#icoerr').textContent='';
  if(!ico.img)return;
  if(!sizes.length){$('#icoerr').textContent='Pick at least one size';$('#icodl').disabled=true;return}
  const gen=++ico.gen,parts=[];
  for(const s of sizes){const c=icoRender(s);parts.push({s,c,png:await canvasPNG(c)});if(gen!==ico.gen)return}
  // ICO file: 6-byte header, one 16-byte entry per image, then the PNG images themselves (supported since Windows Vista)
  const head=6+16*parts.length,total=head+parts.reduce((a,p)=>a+p.png.length,0),out=new Uint8Array(total),dv=new DataView(out.buffer);
  dv.setUint16(2,1,true);dv.setUint16(4,parts.length,true);
  let off=head;
  parts.forEach((p,i)=>{const e=6+16*i;out[e]=p.s>=256?0:p.s;out[e+1]=p.s>=256?0:p.s;dv.setUint16(e+4,1,true);dv.setUint16(e+6,32,true);dv.setUint32(e+8,p.png.length,true);dv.setUint32(e+12,off,true);out.set(p.png,off);off+=p.png.length});
  ico.bytes=out;$('#icodl').disabled=false;
  const g=$('#icoprev');g.innerHTML='';
  for(const p of parts){const f=document.createElement('figure');p.c.style.width=p.c.style.height=Math.max(32,Math.min(p.s,128))+'px';if(p.s<48)p.c.style.imageRendering='pixelated';f.appendChild(p.c);f.append(`${p.s}×${p.s}`);g.appendChild(f)}
  $('#icoinfo').textContent=`${baseName(ico.name)}.ico · ${parts.length} size${parts.length===1?'':'s'} · ${fmt(out.length)}`}
