using System.Text;
using UnifiedToolkit.Conversion.Mapping.Pilots;
namespace UnifiedToolkit.Reports;
public static class PilotMappingCoverageReport
{
 public static void Write(IEnumerable<PilotMappingCoverageEntry> rows,string path){Directory.CreateDirectory(Path.GetDirectoryName(path)??".");using var w=new StreamWriter(path,false,Encoding.UTF8);w.WriteLine("SourceId,SourceName,SourceShipId,SourceFaction,Status,CanonicalSourceId,TargetId,TargetShipId,TargetFaction,Notes");foreach(var r in rows)w.WriteLine(string.Join(",",new[]{C(r.SourceId),C(r.SourceName),C(r.SourceShipId),C(r.SourceFaction),C(r.Status),C(r.CanonicalSourceId),C(r.TargetId),C(r.TargetShipId),C(r.TargetFaction),C(r.Notes)}));}
 private static string C(string v){v??="";if(v.Contains('"'))v=v.Replace("\"","\"\"");return v.IndexOfAny([',','"','\n','\r'])>=0?$"\"{v}\"":v;}
}
