using System.Net;
using System.Text;
using System.Text.Json;
using UnifiedToolkit.KnowledgeBase.PilotAssetLinking;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class PilotTokenExtractionPreparationService
{
    public AssetExtractionPreparationResult Prepare(string repositoryRoot, string? pilotLinksFile = null, string? outputFolder = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Repository folder was not found: {root}");

        var linksPath = Path.GetFullPath(pilotLinksFile ?? Path.Combine(root, "ukb", "pilot-links.json"));
        if (!File.Exists(linksPath)) throw new FileNotFoundException("pilot-links.json was not found. Run link-pilot-assets first.", linksPath);

        var output = Path.GetFullPath(outputFolder ?? Path.Combine(root, "ukb", "reports", "pilot-token-extraction"));
        Directory.CreateDirectory(output);

        var domain = ShipAssetJson.Read<KnowledgeBasePilotDomain>(linksPath);
        var approved = domain.Pilots.Select(p => new
        {
            Pilot = p,
            Sheet = p.AssetRoles.FirstOrDefault(r => r.Role.Equals("PilotBaseTokenSheet", StringComparison.OrdinalIgnoreCase) && r.Status.Equals("clear", StringComparison.OrdinalIgnoreCase))?.Candidates.FirstOrDefault()
        }).Where(x => x.Sheet is not null).ToList();

        var sheets = approved
            .GroupBy(x => PilotTokenSheetDecisionStore.NormalizeRepositoryPath(x.Sheet!.RepositoryPath), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => new AssetExtractionSheet
            {
                SheetId = $"pilot-sheet-{index + 1:000}",
                AssetId = group.First().Sheet!.AssetId,
                RepositoryPath = group.Key,
                LayoutId = string.Empty,
                Entries = group.OrderBy(x => x.Pilot.Name, StringComparer.OrdinalIgnoreCase).Select(x => new AssetExtractionEntry
                {
                    EntityId = x.Pilot.PilotId,
                    TargetId = x.Pilot.TargetId,
                    DisplayName = x.Pilot.Name,
                    ShipId = x.Pilot.ShipId,
                    Faction = x.Pilot.Faction,
                    PilotSkill = x.Pilot.PilotSkill,
                    SquadPointCost = x.Pilot.SquadPointCost
                }).ToList()
            }).ToList();

        var plan = new AssetExtractionPlan
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            AssetRole = "PilotBaseToken",
            Sheets = sheets
        };

        var planPath = Path.Combine(output, "pilot-token-extraction-plan.template.json");
        ShipAssetJson.Write(planPath, plan);
        var htmlPath = Path.Combine(output, "index.html");
        WriteHtml(htmlPath, root, output, plan);

        return new AssetExtractionPreparationResult
        {
            Sheets = sheets.Count,
            Entries = sheets.Sum(x => x.Entries.Count),
            UnresolvedSheets = domain.Pilots.Count - approved.Count,
            PlanFile = planPath,
            HtmlFile = htmlPath,
            OutputFolder = output
        };
    }

    private static void WriteHtml(string path, string repositoryRoot, string outputFolder, AssetExtractionPlan plan)
    {
        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine("<title>Pilot Token Extraction Layout Review</title><style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#eef2f6;color:#17202a}header{position:sticky;top:0;z-index:10;background:#17202a;color:#fff;padding:14px 22px}main{max-width:1600px;margin:auto;padding:18px}.sheet{background:#fff;margin:0 0 22px;padding:16px;border-radius:10px;box-shadow:0 2px 8px #0002}.workspace{display:grid;grid-template-columns:minmax(480px,2fr) minmax(320px,1fr);gap:18px}.imagewrap{position:relative;background:#333;display:inline-block;max-width:100%}.imagewrap img{display:block;max-width:100%;height:auto}.overlay{position:absolute;border:2px solid #00e5ff;box-sizing:border-box;background:#00e5ff18;display:flex;align-items:center;justify-content:center;color:white;text-shadow:0 1px 3px #000;font-weight:700;overflow:hidden}.controls{display:grid;grid-template-columns:repeat(2,minmax(120px,1fr));gap:8px}.controls label{font-size:12px;color:#475569}.controls input{width:100%;box-sizing:border-box;padding:6px}.pilot{border-top:1px solid #ddd;padding:8px 0;display:grid;grid-template-columns:1fr 80px 80px;gap:6px;align-items:center}.pilot select{padding:5px}.path{font:12px Consolas,monospace;overflow-wrap:anywhere}.actions{display:flex;gap:10px;flex-wrap:wrap;margin-top:10px}button{padding:9px 14px;border:0;border-radius:6px;cursor:pointer;font-weight:600}.primary{background:#0b5cab;color:#fff}.secondary{background:#dfe7ef}.status{padding:5px 9px;border-radius:999px;background:#f5c542;font-size:12px}.ok{background:#46b96c;color:#fff}@media(max-width:1000px){.workspace{grid-template-columns:1fr}} </style></head><body>");
        sb.AppendLine($"<header><h1 style=\"margin:0 0 6px\">Pilot Token Extraction Layout Review</h1><div>{plan.Sheets.Count} source sheets · {plan.Sheets.Sum(x => x.Entries.Count)} pilots · normalized coordinates</div></header><main>");
        sb.AppendLine("<p>For each source image, set the grid and margins, then assign every listed pilot to a row and column. The cyan overlay is calculated in the browser. Use <b>Download completed plan</b> when finished.</p><div class=\"actions\"><button class=\"primary\" onclick=\"downloadPlan()\">Download completed plan</button><button class=\"secondary\" onclick=\"saveLocal()\">Save progress in browser</button><button class=\"secondary\" onclick=\"restoreLocal()\">Restore browser progress</button></div><div id=\"sheets\"></div></main>");
        sb.Append("<script>const initial=").Append(json).AppendLine(";let plan=JSON.parse(JSON.stringify(initial));");
        sb.AppendLine("const root=document.getElementById('sheets');const esc=s=>String(s??'').replace(/[&<>\"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',\"'\":'&#39;'}[c]));");
        sb.AppendLine("function imgPath(p){return p.split('/').map(x=>x==='..'||x==='.'?x:encodeURIComponent(decodeURIComponent(x))).join('/')} ");
        foreach (var sheet in plan.Sheets)
        {
            var absolute = Path.GetFullPath(Path.Combine(repositoryRoot, sheet.RepositoryPath.Replace('/', Path.DirectorySeparatorChar)));
            var relative = Path.GetRelativePath(outputFolder, absolute).Replace(Path.DirectorySeparatorChar, '/');
            sb.AppendLine($"plan.sheets.find(x=>x.sheetId==='{Js(sheet.SheetId)}').imageSource='{Js(relative)}';");
        }
        sb.AppendLine("function layoutFor(s){let l=plan.layouts.find(x=>x.layoutId===s.layoutId);if(!l){l={layoutId:'layout-'+s.sheetId,rows:2,columns:2,marginLeft:0,marginTop:0,marginRight:0,marginBottom:0,horizontalGap:0,verticalGap:0};plan.layouts.push(l);s.layoutId=l.layoutId}return l}");
        sb.AppendLine("function render(){root.innerHTML='';plan.sheets.forEach(s=>{const l=layoutFor(s),el=document.createElement('section');el.className='sheet';el.innerHTML=`<h2>${esc(s.sheetId)} <span class=\"status\" id=\"st-${s.sheetId}\">incomplete</span></h2><div class=\"path\">${esc(s.repositoryPath)}</div><div class=\"workspace\"><div><div class=\"imagewrap\" id=\"iw-${s.sheetId}\"><img id=\"im-${s.sheetId}\" src=\"${imgPath(s.imageSource)}\"></div></div><div><div class=\"controls\">${num(s,'rows','Rows',l.rows,1,20)}${num(s,'columns','Columns',l.columns,1,20)}${num(s,'marginLeft','Left margin',l.marginLeft,0,0.49,0.001)}${num(s,'marginRight','Right margin',l.marginRight,0,0.49,0.001)}${num(s,'marginTop','Top margin',l.marginTop,0,0.49,0.001)}${num(s,'marginBottom','Bottom margin',l.marginBottom,0,0.49,0.001)}${num(s,'horizontalGap','Horizontal gap',l.horizontalGap,0,0.49,0.001)}${num(s,'verticalGap','Vertical gap',l.verticalGap,0,0.49,0.001)}</div><h3>Pilot positions</h3><div>${s.entries.map(e=>pilotRow(s,e,l)).join('')}</div></div></div>`;root.appendChild(el);document.getElementById('im-'+s.sheetId).addEventListener('load',()=>draw(s));draw(s)});}");
        sb.AppendLine("function num(s,k,label,v,min,max,step=1){return `<label>${label}<input type=\"number\" value=\"${v}\" min=\"${min}\" max=\"${max}\" step=\"${step}\" onchange=\"setL('${s.sheetId}','${k}',this.value)\"></label>`}");
        sb.AppendLine("function opts(n,v){let o='<option value=\"\">—</option>';for(let i=0;i<n;i++)o+=`<option value=\"${i}\" ${v===i?'selected':''}>${i+1}</option>`;return o}function pilotRow(s,e,l){return `<div class=\"pilot\"><div><b>${esc(e.displayName)}</b><br><small>${esc(e.shipId)} · PS ${e.pilotSkill} · ${e.squadPointCost} pts</small></div><select onchange=\"setE('${s.sheetId}','${e.entityId}','row',this.value)\">${opts(l.rows,e.row)}</select><select onchange=\"setE('${s.sheetId}','${e.entityId}','column',this.value)\">${opts(l.columns,e.column)}</select></div>`}");
        sb.AppendLine("function setL(id,k,v){const s=plan.sheets.find(x=>x.sheetId===id),l=layoutFor(s);l[k]=(k==='rows'||k==='columns')?Math.max(1,parseInt(v)||1):Math.max(0,parseFloat(v)||0);render()}function setE(id,eid,k,v){const s=plan.sheets.find(x=>x.sheetId===id),e=s.entries.find(x=>x.entityId===eid);e[k]=v===''?null:parseInt(v);draw(s)}");
        sb.AppendLine("function draw(s){const w=document.getElementById('iw-'+s.sheetId);if(!w)return;w.querySelectorAll('.overlay').forEach(x=>x.remove());const l=layoutFor(s),cw=(1-l.marginLeft-l.marginRight-(l.columns-1)*l.horizontalGap)/l.columns,ch=(1-l.marginTop-l.marginBottom-(l.rows-1)*l.verticalGap)/l.rows;s.entries.forEach(e=>{if(e.row==null||e.column==null)return;const d=document.createElement('div');d.className='overlay';d.style.left=((l.marginLeft+e.column*(cw+l.horizontalGap))*100)+'%';d.style.top=((l.marginTop+e.row*(ch+l.verticalGap))*100)+'%';d.style.width=(cw*100)+'%';d.style.height=(ch*100)+'%';d.textContent=e.displayName;w.appendChild(d)});const complete=s.entries.every(e=>e.row!=null&&e.column!=null);const st=document.getElementById('st-'+s.sheetId);st.textContent=complete?'complete':'incomplete';st.className='status '+(complete?'ok':'')}");
        sb.AppendLine("function clean(){const p=JSON.parse(JSON.stringify(plan));p.generatedUtc=new Date().toISOString();p.sheets.forEach(s=>delete s.imageSource);return p}function downloadPlan(){const b=new Blob([JSON.stringify(clean(),null,2)],{type:'application/json'}),a=document.createElement('a');a.href=URL.createObjectURL(b);a.download='pilot-token-extraction-plan.completed.json';a.click();URL.revokeObjectURL(a.href)}function saveLocal(){localStorage.setItem('unified-pilot-token-plan',JSON.stringify(plan));alert('Progress saved in this browser.')}function restoreLocal(){const v=localStorage.getItem('unified-pilot-token-plan');if(v){plan=JSON.parse(v);render()}}render();");
        sb.AppendLine("</script></body></html>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Js(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
