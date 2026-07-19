(() => {
  const data = window.PILOT_SHEET_EXPLORER_DATA;
  if (!data) { document.body.innerHTML = '<h1>Explorer data was not loaded.</h1>'; return; }

  const STORAGE_KEY = 'unifiedToolkit.pilotSheetExplorer.v3';
  const LEGACY_STORAGE_KEYS = ['unifiedToolkit.pilotSheetExplorer.v2','unifiedToolkit.pilotSheetExplorer.v1'];
  const state = { currentImageId:null, selection:null, assignments:[], reviewedImageIds:[], generationRequiredPilotIds:[], imageMetadataBySha:{}, zoom:1 };
  const $ = id => document.getElementById(id);
  const els = {
    stats:$('stats'), imageSearch:$('imageSearch'), imageFilter:$('imageFilter'), imageList:$('imageList'), candidateSummary:$('candidateSummary'),
    title:$('currentImageTitle'), meta:$('currentImageMeta'), evidence:$('currentImageEvidence'), source:$('sourceImage'), overlay:$('overlay'), stage:$('canvasStage'), shell:$('canvasShell'),
    pilotSearch:$('pilotSearch'), pilotSelect:$('pilotSelect'), details:$('selectedPilotDetails'), compact:$('activePilotCompact'),
    x:$('cropX'), y:$('cropY'), w:$('cropWidth'), h:$('cropHeight'), assignments:$('assignmentList'), zoomLabel:$('zoomLabel'),
    metaContainsTokens:$('metaContainsTokens'), metaShips:$('metaShips'), metaPilots:$('metaPilots'), metaImageType:$('metaImageType'), metaReviewStatus:$('metaReviewStatus'), metaNotes:$('metaNotes'), metaFactions:$('metaFactions'), metadataFile:$('metadataFile')
  };

  const escapeHtml = s => String(s ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const norm = s => String(s ?? '').toLowerCase().replace(/[^a-z0-9]+/g,' ').trim();
  const fmt = n => Number(n || 0).toFixed(4);
  const currentImage = () => data.images.find(x => x.imageId === state.currentImageId);
  const currentPilot = () => data.missingPilots.find(x => x.pilotId === els.pilotSelect.value);
  const assignedPilotIds = () => new Set(state.assignments.map(x=>x.pilotId));
  const metadataFor = img => img ? (state.imageMetadataBySha[img.sha256] || null) : null;
  const splitTags = value => String(value || '').split(/[;,\n]/).map(x=>x.trim()).filter(Boolean);
  const unique = values => [...new Set(values.filter(Boolean))];
  function metadataTerms(img) {
    const m=metadataFor(img); if(!m)return '';
    return norm([m.containsPilotTokens,m.imageType,m.reviewStatus,...(m.factions||[]),...(m.ships||[]),...(m.pilots||[]),m.notes].join(' '));
  }

  function imageTerms(img) {
    const crops = img.knownCrops || [];
    return norm([img.fileName,img.repositoryPath,...crops.flatMap(c=>[c.displayName,c.faction,c.ship]),metadataTerms(img)].join(' '));
  }

  function evidenceFor(img,pilot) {
    const crops=img.knownCrops||[];
    const sameShip=crops.filter(c=>norm(c.ship)===norm(pilot?.ship));
    const sameFaction=crops.filter(c=>norm(c.faction)===norm(pilot?.faction));
    const names=[...new Set(crops.map(c=>c.displayName).filter(Boolean))];
    let score=img.priorityScore||0;
    if (pilot) {
      if (sameShip.length) score+=100000;
      if (sameFaction.length) score+=10000;
      if (imageTerms(img).includes(norm(pilot.ship))) score+=3000;
      if (imageTerms(img).includes(norm(pilot.faction))) score+=1000;
    }
    return {score,sameShip,sameFaction,names};
  }

  function semanticTitle(img) {
    const m=metadataFor(img);
    if(m){ const ships=m.ships||[], pilots=m.pilots||[], factions=m.factions||[]; if(ships.length)return `${ships.join(', ')} · ${pilots.slice(0,2).join(', ') || 'catalogued source'}`; if(pilots.length)return `Pilot source · ${pilots.slice(0,2).join(', ')}`; if(m.containsPilotTokens==='no')return `No pilot tokens · ${m.imageType||'catalogued image'}`; if(factions.length)return `${factions.join(', ')} · catalogued image`; }
    const crops=img.knownCrops||[];
    if (!crops.length) return `Unknown legacy image · ${img.width}×${img.height}`;
    const ships=[...new Set(crops.map(c=>c.ship).filter(Boolean))];
    const factions=[...new Set(crops.map(c=>c.faction).filter(Boolean))];
    const pilots=[...new Set(crops.map(c=>c.displayName).filter(Boolean))];
    if (ships.length===1) return `${ships[0]} source · ${pilots.slice(0,2).join(', ')}${pilots.length>2?'…':''}`;
    if (factions.length===1) return `${factions[0]} pilot source · ${pilots.slice(0,2).join(', ')}${pilots.length>2?'…':''}`;
    return `Known pilot source · ${pilots.slice(0,2).join(', ')}${pilots.length>2?'…':''}`;
  }

  function renderStats(){
    const recovered=assignedPilotIds().size;
    els.stats.innerHTML=[
      [data.missingPilots.length,'missing pilots'],[recovered,'recovered'],[state.generationRequiredPilotIds.length,'generation required'],
      [data.images.length,'candidate images'],[Object.keys(state.imageMetadataBySha).length,'catalogued images'],[data.images.filter(x=>metadataFor(x)?.containsPilotTokens==='yes').length,'token images'],[data.images.filter(x=>metadataFor(x)?.containsPilotTokens==='no').length,'no-token images']
    ].map(x=>`<div class="stat"><b>${x[0]}</b>${x[1]}</div>`).join('');
  }

  function filteredPilots(){
    const q=norm(els.pilotSearch.value);
    return data.missingPilots.filter(p=>!q||norm(`${p.displayName} ${p.ship} ${p.faction} ${p.xws}`).includes(q));
  }

  function renderPilotSelect(){
    const pilots=filteredPilots(), selected=els.pilotSelect.value, assigned=assignedPilotIds(), generation=new Set(state.generationRequiredPilotIds);
    els.pilotSelect.innerHTML=pilots.map(p=>`<option value="${escapeHtml(p.pilotId)}">${assigned.has(p.pilotId)?'✓ ':generation.has(p.pilotId)?'⚒ ':''}${escapeHtml(p.displayName)} — ${escapeHtml(p.ship)}</option>`).join('');
    if (pilots.some(p=>p.pilotId===selected)) els.pilotSelect.value=selected;
    else if (pilots.length) els.pilotSelect.value=pilots[0].pilotId;
    renderPilotDetails();
  }

  function renderPilotDetails(){
    const p=currentPilot();
    const html=p?`<b>${escapeHtml(p.displayName)}</b><br>${escapeHtml(p.faction)} · ${escapeHtml(p.ship)}<br>Skill ${p.skill} · ${p.points} points${p.donorRecommendation?`<br><b>Donor:</b> ${escapeHtml(p.donorRecommendation)}`:''}`:'Select a missing pilot.';
    els.details.innerHTML=html; els.compact.innerHTML=html;
  }

  function filteredImages(){
    const pilot=currentPilot(), q=norm(els.imageSearch.value), mode=els.imageFilter.value;
    let rows=data.images.map(img=>({img,ev:evidenceFor(img,pilot)})).filter(({img,ev})=>{
      const m=metadataFor(img);
      if (q && !imageTerms(img).includes(q)) return false;
      if (mode==='pilot' && m?.containsPilotTokens==='no') return false;
      if (mode==='known' && !img.isKnownSourceSheet) return false;
      if (mode==='unknown' && img.isKnownSourceSheet) return false;
      if (mode==='sameShip' && !ev.sameShip.length) return false;
      if (mode==='sameFaction' && !ev.sameFaction.length) return false;
      if (mode==='catalogueRelevant' && m?.containsPilotTokens!=='yes') return false;
      if (mode==='catalogueUnknown' && m) return false;
      if (mode==='catalogueNoTokens' && m?.containsPilotTokens!=='no') return false;
      if (mode==='cataloguePartial' && m?.reviewStatus!=='partial') return false;
      return true;
    });
    rows.sort((a,b)=>b.ev.score-a.ev.score || a.img.repositoryPath.localeCompare(b.img.repositoryPath));
    if (mode==='pilot') rows=rows.slice(0,80);
    return rows;
  }

  function renderImageList(){
    const pilot=currentPilot(), rows=filteredImages();
    els.candidateSummary.textContent=pilot?`${rows.length} images ranked for ${pilot.displayName}. Same-ship and same-faction evidence appears first.`:`${rows.length} images.`;
    els.imageList.innerHTML=rows.map(({img,ev})=>{
      const badges=[];
      if(ev.sameShip.length) badges.push(`<span class="badge ship">same ship</span>`);
      if(ev.sameFaction.length) badges.push(`<span class="badge faction">same faction</span>`);
      const m=metadataFor(img); if(m?.containsPilotTokens==='yes')badges.push(`<span class="badge catalogue">catalogued tokens</span>`); if(m?.containsPilotTokens==='no')badges.push(`<span class="badge no-tokens">no tokens</span>`); if(m?.reviewStatus==='partial')badges.push(`<span class="badge partial">partial</span>`);
      const known=m?.pilots?.length?`Catalogued: ${m.pilots.slice(0,3).join(', ')}${m.pilots.length>3?'…':''}`:ev.names.length?`Known: ${ev.names.slice(0,3).join(', ')}${ev.names.length>3?'…':''}`:'No identified pilot crops';
      return `<button class="image-item ${img.imageId===state.currentImageId?'active':''} ${img.isKnownSourceSheet?'known':''} ${state.reviewedImageIds.includes(img.imageId)?'reviewed':''} ${metadataFor(img)?'catalogued':''} ${metadataFor(img)?.containsPilotTokens==='no'?'no-tokens':''} ${metadataFor(img)?.reviewStatus==='partial'?'partial':''}" data-id="${img.imageId}">
        <span class="name">${escapeHtml(semanticTitle(img))}</span>${badges.join('')}
        <span class="evidence">${escapeHtml(known)}</span><span class="meta">${img.width}×${img.height} · ${escapeHtml(img.fileName)}</span></button>`;
    }).join('') || '<p>No images match this filter.</p>';
    els.imageList.querySelectorAll('button[data-id]').forEach(b=>b.addEventListener('click',()=>selectImage(b.dataset.id)));
  }

  function selectImage(id){
    state.currentImageId=id; state.selection=null; syncCropInputs(); renderImageList();
    const img=currentImage(); if(!img)return;
    const ev=evidenceFor(img,currentPilot());
    els.title.textContent=semanticTitle(img);
    els.meta.textContent=`${img.repositoryPath} · ${img.width}×${img.height} · SHA ${img.sha256.slice(0,12)}`;
    const parts=[]; if(ev.sameShip.length)parts.push(`same ship: ${[...new Set(ev.sameShip.map(x=>x.ship))].join(', ')}`); if(ev.sameFaction.length)parts.push(`same faction evidence`);
    els.evidence.textContent=parts.join(' · ') || 'No semantic association is known for this image.';
    els.source.onload=()=>{ applyZoom(false); renderOverlay(); };
    els.source.src=img.browserPath; loadMetadataForm(img);
  }

  function renderOverlay(){
    const img=currentImage(); if(!img)return; els.overlay.innerHTML='';
    [...(img.knownCrops||[]).map(c=>({...c,kind:'known'})),...state.assignments.filter(a=>a.imageId===img.imageId).map(a=>({...a,kind:'new'}))].forEach(c=>{
      const div=document.createElement('div'); div.className=`crop ${c.kind==='known'?'known':''}`;
      Object.assign(div.style,{left:`${c.x*100}%`,top:`${c.y*100}%`,width:`${c.width*100}%`,height:`${c.height*100}%`});
      div.innerHTML=`<span>${escapeHtml(c.displayName||c.pilotName||'Assigned')}</span>`; els.overlay.appendChild(div);
    });
    if(state.selection){ const c=state.selection,div=document.createElement('div');div.className='crop selection';Object.assign(div.style,{left:`${c.x*100}%`,top:`${c.y*100}%`,width:`${c.width*100}%`,height:`${c.height*100}%`});els.overlay.appendChild(div); }
  }

  let dragStart=null;
  els.overlay.addEventListener('pointerdown',e=>{ if(!currentImage())return;const r=els.overlay.getBoundingClientRect();dragStart={x:Math.max(0,Math.min(1,(e.clientX-r.left)/r.width)),y:Math.max(0,Math.min(1,(e.clientY-r.top)/r.height))};els.overlay.setPointerCapture(e.pointerId); });
  els.overlay.addEventListener('pointermove',e=>{ if(!dragStart)return;const r=els.overlay.getBoundingClientRect(),px=Math.max(0,Math.min(1,(e.clientX-r.left)/r.width)),py=Math.max(0,Math.min(1,(e.clientY-r.top)/r.height));state.selection={x:Math.min(dragStart.x,px),y:Math.min(dragStart.y,py),width:Math.abs(px-dragStart.x),height:Math.abs(py-dragStart.y)};syncCropInputs();renderOverlay(); });
  els.overlay.addEventListener('pointerup',()=>{dragStart=null;});

  function syncCropInputs(){const c=state.selection||{};els.x.value=c.x==null?'':fmt(c.x);els.y.value=c.y==null?'':fmt(c.y);els.w.value=c.width==null?'':fmt(c.width);els.h.value=c.height==null?'':fmt(c.height);}
  function readCropInputs(){const c={x:Number(els.x.value),y:Number(els.y.value),width:Number(els.w.value),height:Number(els.h.value)};if(Object.values(c).some(v=>!Number.isFinite(v))||c.width<=0||c.height<=0||c.x<0||c.y<0||c.x+c.width>1.0001||c.y+c.height>1.0001)return null;return c;}
  [els.x,els.y,els.w,els.h].forEach(x=>x.addEventListener('input',()=>{state.selection=readCropInputs();renderOverlay();}));

  function applyZoom(keepCentre=true){
    if(!els.source.naturalWidth)return;
    const oldW=els.stage.offsetWidth||1, oldH=els.stage.offsetHeight||1, cx=(els.shell.scrollLeft+els.shell.clientWidth/2)/oldW, cy=(els.shell.scrollTop+els.shell.clientHeight/2)/oldH;
    const w=Math.max(1,Math.round(els.source.naturalWidth*state.zoom)), h=Math.max(1,Math.round(els.source.naturalHeight*state.zoom));
    els.source.style.width=`${w}px`;els.source.style.height=`${h}px`;els.stage.style.width=`${w}px`;els.stage.style.height=`${h}px`;els.zoomLabel.textContent=`${Math.round(state.zoom*100)}%`;
    if(keepCentre)requestAnimationFrame(()=>{els.shell.scrollLeft=Math.max(0,cx*w-els.shell.clientWidth/2);els.shell.scrollTop=Math.max(0,cy*h-els.shell.clientHeight/2);});
  }
  function setZoom(z){state.zoom=Math.max(.1,Math.min(4,z));applyZoom(true);}
  $('zoomIn').addEventListener('click',()=>setZoom(state.zoom*1.25)); $('zoomOut').addEventListener('click',()=>setZoom(state.zoom/1.25)); $('zoomActual').addEventListener('click',()=>setZoom(1));
  $('zoomFit').addEventListener('click',()=>{if(!els.source.naturalWidth)return;const availableW=Math.max(100,els.shell.clientWidth-40),availableH=Math.max(100,els.shell.clientHeight-40);setZoom(Math.min(1,availableW/els.source.naturalWidth,availableH/els.source.naturalHeight));els.shell.scrollLeft=0;els.shell.scrollTop=0;});
  els.shell.addEventListener('wheel',e=>{if(e.ctrlKey||!e.shiftKey){e.preventDefault();setZoom(state.zoom*(e.deltaY<0?1.12:1/1.12));}}, {passive:false});

  $('addAssignment').addEventListener('click',()=>{
    const img=currentImage(),p=currentPilot(),c=readCropInputs();if(!img||!p||!c){alert('Select a pilot, an image, and draw a valid crop rectangle.');return;}
    state.assignments=state.assignments.filter(x=>x.pilotId!==p.pilotId);state.generationRequiredPilotIds=state.generationRequiredPilotIds.filter(x=>x!==p.pilotId);
    state.assignments.push({assignmentId:`recovery-${Date.now()}`,pilotId:p.pilotId,targetId:p.xws||p.pilotId,displayName:p.displayName,faction:p.faction,ship:p.ship,skill:p.skill,points:p.points,imageId:img.imageId,sourceRepositoryPath:img.repositoryPath,sourceSha256:img.sha256,x:c.x,y:c.y,width:c.width,height:c.height,status:'RecoveredSource',notes:''});
    state.selection=null;syncCropInputs();renderAll();movePilot(1);
  });
  $('clearSelection').addEventListener('click',()=>{state.selection=null;syncCropInputs();renderOverlay();});
  $('markGenerationRequired').addEventListener('click',()=>{const p=currentPilot();if(!p)return;state.assignments=state.assignments.filter(x=>x.pilotId!==p.pilotId);if(!state.generationRequiredPilotIds.includes(p.pilotId))state.generationRequiredPilotIds.push(p.pilotId);renderAll();movePilot(1);});
  $('clearPilotResolution').addEventListener('click',()=>{const p=currentPilot();if(!p)return;state.assignments=state.assignments.filter(x=>x.pilotId!==p.pilotId);state.generationRequiredPilotIds=state.generationRequiredPilotIds.filter(x=>x!==p.pilotId);renderAll();});

  function loadMetadataForm(img){
    const m=metadataFor(img)||{};
    els.metaContainsTokens.value=m.containsPilotTokens||'unknown';
    els.metaShips.value=(m.ships||[]).join('; '); els.metaPilots.value=(m.pilots||[]).join('; ');
    els.metaImageType.value=m.imageType||'unknown'; els.metaReviewStatus.value=m.reviewStatus||'unreviewed'; els.metaNotes.value=m.notes||'';
    const selected=new Set(m.factions||[]); els.metaFactions.querySelectorAll('input[type=checkbox]').forEach(x=>x.checked=selected.has(x.value));
  }
  function readMetadataForm(){
    const img=currentImage(); if(!img)return null;
    return { sha256:img.sha256, repositoryPath:img.repositoryPath, fileName:img.fileName, width:img.width, height:img.height,
      containsPilotTokens:els.metaContainsTokens.value, factions:[...els.metaFactions.querySelectorAll('input:checked')].map(x=>x.value),
      ships:unique(splitTags(els.metaShips.value)), pilots:unique(splitTags(els.metaPilots.value)), imageType:els.metaImageType.value,
      reviewStatus:els.metaReviewStatus.value, notes:els.metaNotes.value.trim(), modifiedUtc:new Date().toISOString() };
  }
  function saveCurrentMetadata(silent=false){ const m=readMetadataForm(); if(!m)return; state.imageMetadataBySha[m.sha256]=m; if(!state.reviewedImageIds.includes(currentImage().imageId)&&m.reviewStatus!=='unreviewed')state.reviewedImageIds.push(currentImage().imageId); persistProgress(); renderAll(); if(!silent)alert('Image metadata saved in this browser.'); }
  function clearCurrentMetadata(){const img=currentImage();if(!img)return;delete state.imageMetadataBySha[img.sha256];persistProgress();loadMetadataForm(img);renderAll();}
  function markNoTokens(){const img=currentImage();if(!img)return;els.metaContainsTokens.value='no';els.metaReviewStatus.value='complete';if(els.metaImageType.value==='unknown')els.metaImageType.value='other';saveCurrentMetadata(true);moveImage(1);}
  function downloadText(name,text,type='application/json'){const blob=new Blob([text],{type}),a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),0);}
  function metadataDocument(){return {schemaVersion:'1.0.0',generatedUtc:new Date().toISOString(),identityKey:'sha256',images:Object.values(state.imageMetadataBySha).sort((a,b)=>a.repositoryPath.localeCompare(b.repositoryPath))};}
  function csvCell(v){return `"${String(v??'').replaceAll('"','""')}"`;}
  function downloadMetadata(){const doc=metadataDocument();downloadText('legacy-pilot-image-catalogue.json',JSON.stringify(doc,null,2));const lines=['Sha256,RepositoryPath,FileName,Width,Height,ContainsPilotTokens,Factions,Ships,Pilots,ImageType,ReviewStatus,Notes,ModifiedUtc'];for(const m of doc.images)lines.push([m.sha256,m.repositoryPath,m.fileName,m.width,m.height,m.containsPilotTokens,(m.factions||[]).join('; '),(m.ships||[]).join('; '),(m.pilots||[]).join('; '),m.imageType,m.reviewStatus,m.notes,m.modifiedUtc].map(csvCell).join(','));downloadText('legacy-pilot-image-catalogue.csv',lines.join('\r\n'),'text/csv');}
  function importMetadataFile(file){const reader=new FileReader();reader.onload=()=>{try{const doc=JSON.parse(String(reader.result));const rows=Array.isArray(doc)?doc:(doc.images||[]);for(const m of rows){if(m?.sha256)state.imageMetadataBySha[String(m.sha256).toLowerCase()]={...m,sha256:String(m.sha256).toLowerCase()};}renderAll();if(currentImage())loadMetadataForm(currentImage());alert(`${rows.length} metadata records imported.`);}catch(e){alert(`Metadata file could not be imported: ${e.message}`);}};reader.readAsText(file);}

  function renderAssignments(){
    const generations=state.generationRequiredPilotIds.map(id=>data.missingPilots.find(p=>p.pilotId===id)).filter(Boolean).map(p=>`<div class="assignment generation"><strong>${escapeHtml(p.displayName)}</strong>${escapeHtml(p.ship)}<br>Generation required<button data-clear-pilot="${escapeHtml(p.pilotId)}">Remove</button></div>`);
    const recovered=state.assignments.map(a=>`<div class="assignment"><strong>${escapeHtml(a.displayName)}</strong>${escapeHtml(a.ship)}<br>${escapeHtml(a.sourceRepositoryPath)}<button data-id="${a.assignmentId}">Remove</button></div>`);
    els.assignments.innerHTML=[...recovered,...generations].join('')||'<p>No resolutions yet.</p>';
    els.assignments.querySelectorAll('button[data-id]').forEach(b=>b.addEventListener('click',()=>{state.assignments=state.assignments.filter(x=>x.assignmentId!==b.dataset.id);renderAll();}));
    els.assignments.querySelectorAll('button[data-clear-pilot]').forEach(b=>b.addEventListener('click',()=>{state.generationRequiredPilotIds=state.generationRequiredPilotIds.filter(x=>x!==b.dataset.clearPilot);renderAll();}));
  }

  function moveImage(delta){const rows=filteredImages();if(!rows.length)return;let i=rows.findIndex(x=>x.img.imageId===state.currentImageId);i=Math.max(0,Math.min(rows.length-1,(i<0?0:i+delta)));selectImage(rows[i].img.imageId);}
  function movePilot(delta){const pilots=filteredPilots();if(!pilots.length)return;let i=pilots.findIndex(x=>x.pilotId===els.pilotSelect.value);i=Math.max(0,Math.min(pilots.length-1,(i<0?0:i+delta)));els.pilotSelect.value=pilots[i].pilotId;onPilotChanged();}
  $('previousImage').addEventListener('click',()=>moveImage(-1));$('nextImage').addEventListener('click',()=>moveImage(1));
  $('previousPilot').addEventListener('click',()=>movePilot(-1));$('nextPilot').addEventListener('click',()=>movePilot(1));
  $('markReviewed').addEventListener('click',()=>{const id=state.currentImageId;if(!id)return;if(!state.reviewedImageIds.includes(id))state.reviewedImageIds.push(id);renderAll();moveImage(1);});

  function onPilotChanged(){renderPilotDetails();renderImageList();const rows=filteredImages();if(rows.length)selectImage(rows[0].img.imageId);}
  function persistProgress(){localStorage.setItem(STORAGE_KEY,JSON.stringify({assignments:state.assignments,reviewedImageIds:state.reviewedImageIds,generationRequiredPilotIds:state.generationRequiredPilotIds,imageMetadataBySha:state.imageMetadataBySha,currentImageId:state.currentImageId,selectedPilotId:els.pilotSelect.value,zoom:state.zoom}));}
  function save(){persistProgress();alert('Progress saved in this browser.');}
  function restore(silent=false){const raw=localStorage.getItem(STORAGE_KEY)||LEGACY_STORAGE_KEYS.map(k=>localStorage.getItem(k)).find(Boolean);if(!raw){if(!silent)alert('No saved browser progress was found.');return;}try{const saved=JSON.parse(raw);state.assignments=saved.assignments||[];state.reviewedImageIds=saved.reviewedImageIds||[];state.generationRequiredPilotIds=saved.generationRequiredPilotIds||[];state.imageMetadataBySha=saved.imageMetadataBySha||{};state.currentImageId=saved.currentImageId||null;state.zoom=saved.zoom||1;renderAll();if(saved.selectedPilotId&&data.missingPilots.some(p=>p.pilotId===saved.selectedPilotId))els.pilotSelect.value=saved.selectedPilotId;renderPilotDetails();renderImageList();if(state.currentImageId)selectImage(state.currentImageId);if(!silent)alert('Browser progress restored.');}catch{if(!silent)alert('Saved progress could not be read.');}}
  $('saveProgress').addEventListener('click',save);$('restoreProgress').addEventListener('click',()=>restore(false));
  $('downloadPlan').addEventListener('click',()=>{const resolved=new Set([...state.assignments.map(a=>a.pilotId),...state.generationRequiredPilotIds]);const plan={schemaVersion:'2.0.0',generatedUtc:new Date().toISOString(),sourceCatalogue:'pilot-sheet-catalogue.json',assignments:state.assignments,generationRequiredPilotIds:state.generationRequiredPilotIds,reviewedImageIds:state.reviewedImageIds,imageMetadataCatalogue:metadataDocument(),remainingPilotIds:data.missingPilots.map(x=>x.pilotId).filter(id=>!resolved.has(id))};const blob=new Blob([JSON.stringify(plan,null,2)],{type:'application/json'}),a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='pilot-token-source-recovery-plan.completed.json';a.click();URL.revokeObjectURL(a.href);});

  $('saveImageMetadata').addEventListener('click',()=>saveCurrentMetadata(false));$('markNoPilotTokens').addEventListener('click',markNoTokens);$('clearImageMetadata').addEventListener('click',clearCurrentMetadata);$('downloadMetadata').addEventListener('click',downloadMetadata);$('importMetadata').addEventListener('click',()=>els.metadataFile.click());els.metadataFile.addEventListener('change',()=>{const f=els.metadataFile.files?.[0];if(f)importMetadataFile(f);els.metadataFile.value='';});
  els.imageSearch.addEventListener('input',renderImageList);els.imageFilter.addEventListener('change',()=>{renderImageList();const rows=filteredImages();if(rows.length)selectImage(rows[0].img.imageId);});
  els.pilotSearch.addEventListener('input',()=>{renderPilotSelect();onPilotChanged();});els.pilotSelect.addEventListener('change',onPilotChanged);
  document.addEventListener('keydown',e=>{if(['INPUT','SELECT','TEXTAREA'].includes(document.activeElement?.tagName))return;if(e.key==='ArrowLeft')moveImage(-1);if(e.key==='ArrowRight')moveImage(1);});

  function renderAll(){renderStats();renderPilotSelect();renderImageList();renderAssignments();renderOverlay();}
  renderAll();restore(true);if(!els.pilotSelect.value&&data.missingPilots.length)els.pilotSelect.value=data.missingPilots[0].pilotId;onPilotChanged();
})();
