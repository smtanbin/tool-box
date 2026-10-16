// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Full-colour navigation icons in the Windows 11 (Fluent) style: soft gradient fills, filled shapes, no outlines.
// Added to every menu item; skins.css shows them only for the Fluent skin (other skins keep their own icon style).
"use strict";
(function(){
const lg=(id,a,b,x2=1,y2=1)=>`<linearGradient id="${id}" x1="0" y1="0" x2="${x2}" y2="${y2}"><stop offset="0" stop-color="${a}"/><stop offset="1" stop-color="${b}"/></linearGradient>`;
const svg=(defs,body)=>`<svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><defs>${defs}</defs>${body}</svg>`;
const I={
 home:svg(lg('fhm1','#ffb05c','#e0620d')+lg('fhm2','#ffffff','#e4e9ef',0,1),
  `<path d="M12 2.8l9.2 8.1a.8.8 0 0 1-.5 1.4H19V20a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 5 20v-7.7H3.3a.8.8 0 0 1-.5-1.4z" fill="url(#fhm2)"/><path d="M12 2.8l9.2 8.1a.8.8 0 0 1-.5 1.4h-1.1L12 5.6 4.4 12.3H3.3a.8.8 0 0 1-.5-1.4z" fill="url(#fhm1)"/><rect x="9.6" y="13.2" width="4.8" height="8.3" rx="1.2" fill="url(#fhm1)"/>`),

 hw:svg(lg('fhw1','#6a7a8c','#2f3b4a',0,1)+lg('fhw2','#4cc2ff','#0063d6'),
  `<rect x="3.5" y="3.5" width="17" height="17" rx="3.2" fill="url(#fhw1)"/><rect x="8" y="8" width="8" height="8" rx="1.6" fill="url(#fhw2)"/><g stroke="#9fb0c3" stroke-width="1.6" stroke-linecap="round"><path d="M8.5 1.5v2M12 1.5v2M15.5 1.5v2M8.5 20.5v2M12 20.5v2M15.5 20.5v2M1.5 8.5h2M1.5 12h2M1.5 15.5h2M20.5 8.5h2M20.5 12h2M20.5 15.5h2"/></g>`),
 disk:svg(lg('fdk1','#b4c0cd','#6b7a8b',0,1)+lg('fdk2','#e7ecf2','#c3ccd6',0,1),
  `<rect x="2" y="6" width="20" height="12.5" rx="3" fill="url(#fdk1)"/><rect x="2" y="6" width="20" height="6" rx="3" fill="url(#fdk2)"/><rect x="5" y="14.2" width="8" height="1.6" rx=".8" fill="#4b5867"/><circle cx="18" cy="15" r="1.4" fill="#3ddc84"/>`),
 proc:svg(lg('fpr1','#4cc2ff','#0063d6'),
  `<rect x="2" y="3.5" width="20" height="15" rx="3" fill="url(#fpr1)"/><path d="M5 14l3.2-3.4 2.6 2.4 3.6-5 2.6 3H19" fill="none" stroke="#fff" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/><rect x="8" y="19.5" width="8" height="2" rx="1" fill="#0a4fa8"/>`),
 hosts:svg(lg('fho1','#ffffff','#dfe5ec',0,1)+lg('fho2','#ffb05c','#e0620d'),
  `<path d="M5 2.5h9l5 5V20a1.5 1.5 0 0 1-1.5 1.5h-12A1.5 1.5 0 0 1 4 20V4a1.5 1.5 0 0 1 1-1.5z" fill="url(#fho1)"/><path d="M14 2.5v4a1 1 0 0 0 1 1h4z" fill="#c4cdd8"/><rect x="6.5" y="10" width="8" height="1.4" rx=".7" fill="#9aa6b4"/><rect x="6.5" y="13" width="6" height="1.4" rx=".7" fill="#9aa6b4"/><path d="M20.8 11.6l1.6 1.6-7.6 7.6-2.6.9.9-2.6z" fill="url(#fho2)"/>`),
 env:svg(lg('fen1','#c9a8ff','#7b3fe4'),
  `<path d="M10.3 2h3.4l.5 2.6a7.6 7.6 0 0 1 1.8 1l2.5-.9 1.7 2.9-2 1.7a7.7 7.7 0 0 1 0 2.1l2 1.7-1.7 2.9-2.5-.9a7.6 7.6 0 0 1-1.8 1l-.5 2.6h-3.4l-.5-2.6a7.6 7.6 0 0 1-1.8-1l-2.5.9-1.7-2.9 2-1.7a7.7 7.7 0 0 1 0-2.1l-2-1.7 1.7-2.9 2.5.9a7.6 7.6 0 0 1 1.8-1z" fill="url(#fen1)"/><circle cx="12" cy="11.4" r="3" fill="#fff"/>`),
 adapters:svg(lg('fad1','#5fc8ff','#0067c0')+lg('fad2','#9fb0c3','#5d6d80',0,1),
  `<rect x="3" y="2.5" width="18" height="12" rx="2.5" fill="url(#fad1)"/><rect x="7" y="6" width="10" height="5" rx="1" fill="#fff" opacity=".9"/><rect x="8.5" y="6" width="1.4" height="2.4" fill="#0a5bb0"/><rect x="11.3" y="6" width="1.4" height="2.4" fill="#0a5bb0"/><rect x="14.1" y="6" width="1.4" height="2.4" fill="#0a5bb0"/><rect x="11" y="14.5" width="2" height="4" fill="url(#fad2)"/><rect x="5" y="18.5" width="14" height="3" rx="1.5" fill="url(#fad2)"/>`),
 dns:svg(lg('fdn1','#62e6d4','#0b8f80'),
  `<circle cx="12" cy="12" r="9.5" fill="url(#fdn1)"/><g fill="none" stroke="#fff" stroke-width="1.3" opacity=".9"><ellipse cx="12" cy="12" rx="4" ry="9.5"/><path d="M2.8 9h18.4M2.8 15h18.4"/></g>`),
 trace:svg(lg('ftr1','#ff8f84','#c42b1c',0,1),
  `<path d="M4 20.5c3 0 3-4 6-4s3.5 3 7 3" fill="none" stroke="#9aa6b4" stroke-width="1.6" stroke-linecap="round" stroke-dasharray="1.5 2.5"/><path d="M15 2.5a5 5 0 0 1 5 5c0 3.6-5 8.5-5 8.5s-5-4.9-5-8.5a5 5 0 0 1 5-5z" fill="url(#ftr1)"/><circle cx="15" cy="7.5" r="1.9" fill="#fff"/><circle cx="4" cy="20.5" r="1.8" fill="#0067c0"/>`),
 subnet:svg(lg('fsu1','#7cc4ff','#2b6fd6')+lg('fsu2','#b39dff','#6b4fbb'),
  `<rect x="2.5" y="2.5" width="8.5" height="8.5" rx="2.2" fill="url(#fsu1)"/><rect x="13" y="2.5" width="8.5" height="8.5" rx="2.2" fill="url(#fsu2)"/><rect x="2.5" y="13" width="8.5" height="8.5" rx="2.2" fill="url(#fsu2)"/><rect x="13" y="13" width="8.5" height="8.5" rx="2.2" fill="url(#fsu1)"/>`),
 ipscan:svg(lg('fip1','#4cc2ff','#0058c4'),
  `<circle cx="12" cy="12" r="9.5" fill="url(#fip1)"/><path d="M12 12V2.5A9.5 9.5 0 0 1 21 9z" fill="#fff" opacity=".35"/><circle cx="12" cy="12" r="5.5" fill="none" stroke="#fff" stroke-width="1" opacity=".55"/><circle cx="16" cy="7.5" r="1.4" fill="#fff"/><circle cx="8" cy="15" r="1.1" fill="#bfe6ff"/>`),
 ping:svg(lg('fpi1','#5ee08f','#0f8a3c'),
  `<circle cx="12" cy="12" r="9.5" fill="url(#fpi1)"/><path d="M5 12.5h3l1.8-4 2.8 8 2-5.5 1.2 1.5H19" fill="none" stroke="#fff" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>`),
 telnet:svg(lg('fte1','#55606e','#1e2530',0,1),
  `<rect x="2" y="3.5" width="20" height="17" rx="3" fill="url(#fte1)"/><rect x="2" y="3.5" width="20" height="3.5" rx="3" fill="#7b8794"/><path d="M6 11l3 2.5L6 16" fill="none" stroke="#5ef08a" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/><rect x="11" y="15" width="6" height="1.6" rx=".8" fill="#5ef08a"/>`),
 hash:svg(lg('fha1','#ffcf7a','#c27616'),
  `<path d="M3 4.5A1.5 1.5 0 0 1 4.5 3h7.4a2 2 0 0 1 1.4.6l7.6 7.6a2 2 0 0 1 0 2.8l-6.5 6.5a2 2 0 0 1-2.8 0L4 12.9a2 2 0 0 1-.6-1.4z" fill="url(#fha1)" transform="translate(-.4 0)"/><circle cx="7.6" cy="7.4" r="1.7" fill="#fff"/>`),
 crypt:svg(lg('fcr1','#ffd95e','#c79500',0,1)+lg('fcr2','#c3ccd6','#7d8a99',0,1),
  `<path d="M7.5 10V7.5a4.5 4.5 0 0 1 9 0V10" fill="none" stroke="url(#fcr2)" stroke-width="2.4"/><rect x="4" y="10" width="16" height="11.5" rx="3" fill="url(#fcr1)"/><circle cx="12" cy="15" r="1.7" fill="#7a5a00"/><rect x="11.2" y="15.5" width="1.6" height="3" rx=".8" fill="#7a5a00"/>`),
 token:svg(lg('fto1','#ffb27a','#d0531a'),
  `<circle cx="8" cy="9" r="5.5" fill="url(#fto1)"/><circle cx="7" cy="8" r="1.8" fill="#fff"/><path d="M11.5 12.5l9 9" stroke="url(#fto1)" stroke-width="3" stroke-linecap="round"/><path d="M16.5 17.5l2-2M19 20l2-2" stroke="#d0531a" stroke-width="2" stroke-linecap="round"/>`),
 encode:svg(lg('fec1','#8f8cff','#4b3fc7'),
  `<rect x="2.5" y="3" width="19" height="18" rx="4" fill="url(#fec1)"/><path d="M9.5 7.5c-1.6 0-2 .8-2 2v1c0 .8-.5 1.4-1.3 1.5.8.1 1.3.7 1.3 1.5v1c0 1.2.4 2 2 2M14.5 7.5c1.6 0 2 .8 2 2v1c0 .8.5 1.4 1.3 1.5-.8.1-1.3.7-1.3 1.5v1c0 1.2-.4 2-2 2" fill="none" stroke="#fff" stroke-width="1.6" stroke-linecap="round"/>`),
 img64:svg(lg('fim1','#7fd6ff','#2a8fe0',0,1)+lg('fim2','#5ad08a','#178a4c',0,1),
  `<rect x="2" y="3.5" width="20" height="17" rx="3" fill="url(#fim1)"/><circle cx="16.5" cy="8.5" r="2.2" fill="#ffd95e"/><path d="M2 17l5.5-6 4.5 4.5 3-3 7 6.5V18a2.5 2.5 0 0 1-2.5 2.5h-15A2.5 2.5 0 0 1 2 18z" fill="url(#fim2)"/>`),
 ico:svg(lg('fic1','#ffb75e','#e36a0a')+lg('fic2','#ffd9a8','#ff9a3c'),
  `<rect x="9" y="2.5" width="12.5" height="12.5" rx="3" fill="url(#fic2)"/><rect x="5" y="7" width="10" height="10" rx="2.5" fill="url(#fic1)" opacity=".85"/><rect x="2.5" y="12" width="9.5" height="9.5" rx="2.4" fill="url(#fic1)"/><path d="M4.5 19.5l2.3-2.6 1.6 1.5 1.6-1.5V20H4.5z" fill="#fff" opacity=".85"/>`),
 jwt:svg(lg('fjw1','#ff7fbe','#b3006a',0,1),
  `<path d="M12 2l8 3v6.2c0 5-3.4 9-8 10.8-4.6-1.8-8-5.8-8-10.8V5z" fill="url(#fjw1)"/><path d="M8.3 12l2.6 2.6 4.9-5.2" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>`),
 json:svg(lg('fjs1','#ffffff','#e2e8ee',0,1)+lg('fjs2','#6ad36a','#2e8b2e'),
  `<path d="M5 2.5h9l5 5V20a1.5 1.5 0 0 1-1.5 1.5h-12A1.5 1.5 0 0 1 4 20V4a1.5 1.5 0 0 1 1-1.5z" fill="url(#fjs1)"/><path d="M14 2.5v4a1 1 0 0 0 1 1h4z" fill="#c4cdd8"/><rect x="6" y="10" width="11" height="9" rx="2" fill="url(#fjs2)"/><path d="M10 12c-.9 0-1.1.4-1.1 1.1 0 .6-.3.9-.8 1 .5.1.8.4.8 1 0 .7.2 1.1 1.1 1.1M13 12c.9 0 1.1.4 1.1 1.1 0 .6.3.9.8 1-.5.1-.8.4-.8 1 0 .7-.2 1.1-1.1 1.1" fill="none" stroke="#fff" stroke-width="1.1" stroke-linecap="round"/>`),
 time:svg(lg('fti1','#5cb8ff','#0058c4'),
  `<circle cx="12" cy="12" r="9.5" fill="url(#fti1)"/><circle cx="12" cy="12" r="7.2" fill="#fff"/><path d="M12 7.5V12l3 2" fill="none" stroke="#0067c0" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>`),
 base:svg(lg('fba1','#b9a2ff','#6a48c9',0,1),
  `<rect x="4" y="2" width="16" height="20" rx="3.2" fill="url(#fba1)"/><rect x="6.5" y="4.5" width="11" height="4" rx="1.2" fill="#fff" opacity=".9"/><g fill="#fff" opacity=".85"><circle cx="8.5" cy="12" r="1.2"/><circle cx="12" cy="12" r="1.2"/><circle cx="15.5" cy="12" r="1.2"/><circle cx="8.5" cy="15.5" r="1.2"/><circle cx="12" cy="15.5" r="1.2"/><circle cx="15.5" cy="15.5" r="1.2"/><circle cx="8.5" cy="19" r="1.2"/><circle cx="12" cy="19" r="1.2"/><circle cx="15.5" cy="19" r="1.2"/></g>`),
};
for(const a of document.querySelectorAll('#rail .ri[data-tool]')){const s=I[a.dataset.tool];if(!s)continue;
  const span=document.createElement('span');span.className='cicon';span.innerHTML=s;a.insertBefore(span,a.querySelector('.fi'))}
})();
