using System.Text;
using UnifiedToolkit.Conversion.FirstEdition.Pilots;
namespace UnifiedToolkit.Reports;
public static class FirstEditionPilotsReport
{
 public static void Write(IEnumerable<FirstEditionPilot> pilots,string path){Directory.CreateDirectory(Path.GetDirectoryName(path)??".");using var w=new StreamWriter(path,false,Encoding.UTF8);w.WriteLine("Id,Name,ShipId,Faction,PilotSkill,SquadPointCost,Unique,UpgradeSlots,SourceId,MappingId,Kind,MappingVersion");foreach(var p in pilots.OrderBy(x=>x.Name).ThenBy(x=>x.ShipId))w.WriteLine(string.Join(",",new[]{C(p.Id),C(p.Name),C(p.ShipId),C(p.Faction),p.PilotSkill.ToString(),p.SquadPointCost.ToString(),p.Unique.ToString(),C(string.Join("|",p.UpgradeSlots)),C(p.Provenance.SourceId),C(p.Provenance.MappingId),C(p.Provenance.Kind.ToString()),C(p.Provenance.MappingVersion)}));}
 private static string C(string v){v??="";if(v.Contains('"'))v=v.Replace("\"","\"\"");return v.IndexOfAny([',','"','\n','\r'])>=0?$"\"{v}\"":v;}
}
