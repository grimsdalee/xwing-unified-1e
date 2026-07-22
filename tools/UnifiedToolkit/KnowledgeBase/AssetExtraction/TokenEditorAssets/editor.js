const data=window.editorData;
const storageKey='pilot-token-editor-v1';
let current=0,zoom=1,target='name',drawing=false,start=null,moving=false,moveStart=null,originalRegion=null;

const saved=JSON.parse(localStorage.getItem(storageKey)||'null');
if(saved?.pilots){
  for(const p of data.pilots){
    const old=saved.pilots.find(x=>x.pilotId===p.pilotId);
    if(old) Object.assign(p,old);
  }
}

const q=id=>document.getElementById(id);
const list=q('pilotList'),donor=q('donor'),card=q('card'),stage=q('stage'),viewport=q('viewport');
const nameOverlay=q('nameOverlay'),skillOverlay=q('skillOverlay');

function save(){localStorage.setItem(storageKey,JSON.stringify(data));}
function esc(v){return String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
function p(){return data.pilots[current];}
function region(){return target==='name'?p().nameRegion:p().skillRegion;}
function style(){return target==='name'?p().nameStyle:p().skillStyle;}

function renderList(){
 const search=q('search').value.toLowerCase();list.innerHTML='';
 data.pilots.forEach((x,i)=>{
  if(!`${x.displayName} ${x.ship} ${x.faction}`.toLowerCase().includes(search))return;
  const d=document.createElement('div');d.className='pilot '+(i===current?'active':'');
  d.innerHTML=`<strong>${esc(x.displayName)}</strong><small>${esc(x.faction)} · ${esc(x.ship)}</small><small class="${x.status==='Approved'?'approved':'warning'}">${esc(x.status)}</small>`;
  d.onclick=()=>{current=i;render();};list.appendChild(d);
 });
}
function render(){
 renderList();
 const x=p();
 q('summary').innerHTML=`<h2>${esc(x.displayName)}</h2><div>${esc(x.faction)} · ${esc(x.ship)} · Skill ${x.skill} · ${x.points} points</div><div class="path">Donor: ${esc(x.donorRepositoryPath)}</div>`;
 donor.src=x.donorPreview;
 card.style.display=x.pilotCardPreview?'block':'none';card.src=x.pilotCardPreview||'';
 q('cardMissing').textContent=x.pilotCardPreview?'':'No pilot card image was copied.';
 q('status').value=x.status;q('notes').value=x.notes||'';
 donor.onload=()=>{setZoomToFit();updateOverlays();};
 renderControls();updateOverlays();
}
function setZoom(v){zoom=Math.max(.1,Math.min(4,v));stage.style.transform=`scale(${zoom})`;q('zoomLabel').textContent=`${Math.round(zoom*100)}%`;}
function setZoomToFit(){if(!donor.naturalWidth)return;setZoom(Math.min(1,(viewport.clientWidth-20)/donor.naturalWidth,(viewport.clientHeight-20)/donor.naturalHeight));}
function updateOverlays(){
 if(!donor.naturalWidth)return;
 for(const [el,r,s] of [[nameOverlay,p().nameRegion,p().nameStyle],[skillOverlay,p().skillRegion,p().skillStyle]]){
  el.style.left=`${r.x*donor.naturalWidth}px`;el.style.top=`${r.y*donor.naturalHeight}px`;el.style.width=`${r.width*donor.naturalWidth}px`;el.style.height=`${r.height*donor.naturalHeight}px`;
  el.textContent=s.text;el.style.fontFamily=s.fontFamily;el.style.fontSize=`${s.fontSize}px`;el.style.fontWeight=s.fontWeight;el.style.color=s.textColor;el.style.background=s.backgroundColor;
  el.style.textAlign=s.align;el.style.transform=`rotate(${s.rotation||0}deg)`;
  if(el===skillOverlay){el.style.webkitTextStroke=`${s.strokeWidth||0}px ${s.strokeColor||'transparent'}`;}
 }
}
function renderControls(){
 const r=region(),s=style();
 q('regionControls').innerHTML=`<div class="form-grid"><h3>${target==='name'?'Pilot name':'Pilot skill'} rectangle</h3>
 ${['x','y','width','height'].map(k=>`<label>${k}<input data-region="${k}" type="number" step="0.001" min="0" max="1" value="${r[k]}"></label>`).join('')}</div>`;
 q('styleControls').innerHTML=`<div class="form-grid"><h3>Text style</h3>
 <label>Text<input data-style="text" value="${esc(s.text)}"></label>
 <label>Font family<input data-style="fontFamily" value="${esc(s.fontFamily)}"></label>
 <label>Font size<input data-style="fontSize" type="number" value="${s.fontSize}"></label>
 <label>Weight<input data-style="fontWeight" value="${esc(s.fontWeight)}"></label>
 <label>Text colour<input data-style="textColor" type="color" value="${s.textColor}"></label>
 <label>Background<input data-style="backgroundColor" value="${esc(s.backgroundColor)}"></label>
 ${target==='skill'?`<label>Stroke colour<input data-style="strokeColor" type="color" value="${s.strokeColor}"></label><label>Stroke width<input data-style="strokeWidth" type="number" value="${s.strokeWidth}"></label>`:''}
 <label>Alignment<select data-style="align"><option>left</option><option>center</option><option>right</option></select></label>
 <label>Rotation<input data-style="rotation" type="number" value="${s.rotation||0}"></label></div>`;
 document.querySelectorAll('[data-region]').forEach(el=>el.oninput=()=>{r[el.dataset.region]=Number(el.value);save();updateOverlays();});
 document.querySelectorAll('[data-style]').forEach(el=>{if(el.dataset.style==='align')el.value=s.align;el.oninput=()=>{s[el.dataset.style]=['fontSize','strokeWidth','rotation'].includes(el.dataset.style)?Number(el.value):el.value;save();updateOverlays();};});
}
function imagePoint(e){const rect=stage.getBoundingClientRect();return{x:(e.clientX-rect.left)/zoom,y:(e.clientY-rect.top)/zoom};}
function normRect(a,b){const x=Math.max(0,Math.min(a.x,b.x)),y=Math.max(0,Math.min(a.y,b.y)),x2=Math.min(donor.naturalWidth,Math.max(a.x,b.x)),y2=Math.min(donor.naturalHeight,Math.max(a.y,b.y));return{x:x/donor.naturalWidth,y:y/donor.naturalHeight,width:(x2-x)/donor.naturalWidth,height:(y2-y)/donor.naturalHeight};}
stage.onmousedown=e=>{
 if(e.target.classList.contains('overlay')){
  moving=true;moveStart=imagePoint(e);originalRegion={...region()};e.preventDefault();return;
 }
 drawing=true;start=imagePoint(e);e.preventDefault();
};
window.onmousemove=e=>{
 if(drawing){Object.assign(region(),normRect(start,imagePoint(e)));updateOverlays();}
 if(moving){const pt=imagePoint(e),dx=(pt.x-moveStart.x)/donor.naturalWidth,dy=(pt.y-moveStart.y)/donor.naturalHeight;region().x=Math.max(0,Math.min(1-originalRegion.width,originalRegion.x+dx));region().y=Math.max(0,Math.min(1-originalRegion.height,originalRegion.y+dy));updateOverlays();}
};
window.onmouseup=()=>{if(drawing||moving){drawing=false;moving=false;save();renderControls();}};
document.querySelectorAll('input[name=target]').forEach(el=>el.onchange=()=>{target=el.value;renderControls();});
q('status').onchange=()=>{p().status=q('status').value;save();renderList();};
q('notes').oninput=()=>{p().notes=q('notes').value;save();};
q('search').oninput=renderList;
q('zoomIn').onclick=()=>setZoom(zoom*1.2);q('zoomOut').onclick=()=>setZoom(zoom/1.2);q('fit').onclick=setZoomToFit;q('actual').onclick=()=>setZoom(1);
q('download').onclick=()=>{
 save();const incomplete=data.pilots.filter(x=>x.status!=='Approved');
 if(incomplete.length){alert(`${incomplete.length} pilot(s) still need an approved layout.`);return;}
 const blob=new Blob([JSON.stringify({...data,generatedUtc:new Date().toISOString()},null,2)],{type:'application/json'});
 const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='pilot-token-editor-plan.completed.json';a.click();URL.revokeObjectURL(a.href);
};
render();