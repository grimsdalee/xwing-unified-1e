const data=window.generationData;
const storageKey='pilot-token-generation-review-v1';
let current=0;

const saved=JSON.parse(localStorage.getItem(storageKey)||'null');
if(saved&&Array.isArray(saved.pilots)){
  for(const p of data.pilots){
    const old=saved.pilots.find(x=>x.pilotId===p.pilotId);
    if(old){
      p.status=old.status||p.status;
      p.notes=old.notes||'';

      const oldSource=old.selectedDonorSourceRepositoryPath||'';
      const oldPreview=old.selectedDonor||'';
      const candidate=p.donorCandidates.find(c=>
        (oldSource&&c.sourceRepositoryPath===oldSource)||
        (oldPreview&&c.previewPath===oldPreview));

      if(candidate){
        p.selectedDonor=candidate.previewPath;
        p.selectedDonorSourceRepositoryPath=candidate.sourceRepositoryPath;
      }
    }
  }
}

const list=document.getElementById('list');
const detail=document.getElementById('detail');
const search=document.getElementById('search');

function save(){
  localStorage.setItem(storageKey,JSON.stringify(data));
}

function renderList(){
  const q=search.value.toLowerCase();
  list.innerHTML='';
  data.pilots.forEach((p,i)=>{
    if(!`${p.displayName} ${p.ship} ${p.faction}`.toLowerCase().includes(q))return;
    const d=document.createElement('div');
    d.className='pilot '+(i===current?'active':'');
    d.innerHTML=`<strong>${esc(p.displayName)}</strong>
      <small>${esc(p.faction)} · ${esc(p.ship)}</small>
      <small class="${p.status==='Approved'?'approved':'warning'}">${esc(p.status)}</small>`;
    d.onclick=()=>{current=i;render();};
    list.appendChild(d);
  });
}

function renderDetail(){
  const p=data.pilots[current];
  if(!p){detail.innerHTML='<p>No pilots found.</p>';return;}

  detail.innerHTML=`<h2>${esc(p.displayName)}</h2>
    <div class="fields">
      <label>Faction<input readonly value="${esc(p.faction)}"></label>
      <label>Ship<input readonly value="${esc(p.ship)}"></label>
      <label>Pilot skill<input readonly value="${p.skill}"></label>
      <label>Points<input readonly value="${p.points}"></label>
    </div>
    <div class="grid">
      <div class="panel">
        <h3>Pilot card reference</h3>
        ${p.pilotCard
          ? `<img class="preview" src="${p.pilotCard}">
             <p class="path-note">Repository source: ${esc(p.pilotCardSourceRepositoryPath)}</p>`
          : '<p class="warning">No pilot card image found.</p>'}
      </div>
      <div class="panel">
        <h3>Same-ship donor candidates</h3>
        <div id="candidates"></div>
        <p class="path-note">Images shown here are temporary review copies. The completed plan records the original repository source path; final tokens will be written beneath <code>assets/generated/PilotBaseToken</code>.</p>
        <div class="status">
          <label>Status
            <select id="status">
              <option>NeedsReview</option>
              <option>Approved</option>
              <option>NoDonorAvailable</option>
            </select>
          </label>
        </div>
        <label>Notes<textarea id="notes"></textarea></label>
      </div>
    </div>`;

  const c=document.getElementById('candidates');
  if(!p.donorCandidates.length){
    c.innerHTML='<p class="warning">No same-faction, same-ship donor exists.</p>';
  }

  p.donorCandidates.forEach((candidate,i)=>{
    const row=document.createElement('label');
    row.className='candidate';
    row.innerHTML=`<input type="radio" name="donor"
        value="${esc(candidate.previewPath)}"
        ${p.selectedDonor===candidate.previewPath?'checked':''}>
      <img src="${candidate.previewPath}">
      <span><strong>${esc(candidate.label||`Candidate ${i+1}`)}</strong>
        <br><small>Repository source:</small>
        <br><code>${esc(candidate.sourceRepositoryPath)}</code>
        <br><small>Review copy: ${esc(candidate.previewPath)}</small>
      </span>`;

    row.querySelector('input').onchange=()=>{
      p.selectedDonor=candidate.previewPath;
      p.selectedDonorSourceRepositoryPath=candidate.sourceRepositoryPath;
      save();
    };
    c.appendChild(row);
  });

  const status=document.getElementById('status');
  status.value=p.status;
  status.onchange=()=>{p.status=status.value;save();renderList();};

  const notes=document.getElementById('notes');
  notes.value=p.notes||'';
  notes.oninput=()=>{p.notes=notes.value;save();};
}

function render(){renderList();renderDetail();}
function esc(v){return String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}

search.oninput=renderList;
document.getElementById('download').onclick=()=>{
  save();
  const incomplete=data.pilots.filter(p=>p.status!=='Approved');
  if(incomplete.length){
    alert(`${incomplete.length} pilot(s) are not approved yet.`);
    return;
  }
  const complete={...data,generatedUtc:new Date().toISOString()};
  const blob=new Blob([JSON.stringify(complete,null,2)],{type:'application/json'});
  const a=document.createElement('a');
  a.href=URL.createObjectURL(blob);
  a.download='pilot-token-generation-plan.completed.json';
  a.click();
  URL.revokeObjectURL(a.href);
};
render();
