using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.FirstEdition.Pilots;
using UnifiedToolkit.Conversion.Issues;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.Converters;

public static class PilotConverter
{
    public static (IReadOnlyList<FirstEditionPilot> Pilots,IReadOnlyList<ConversionIssue> Issues) Convert(
        IEnumerable<PilotDefinition> sourcePilots, ConversionMappingSet mappings, FirstEditionRepository targetRepository)
    {
        var sourceById=sourcePilots.ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);
        var pilots=new List<FirstEditionPilot>(); var issues=new List<ConversionIssue>();
        foreach(var map in mappings.Pilots)
        {
            if(!sourceById.TryGetValue(map.SourceId,out var source)){issues.Add(Issue("Error","UnknownPilotSource",map.SourceId,map.TargetId,"Pilot mapping references a missing source pilot."));continue;}
            if(targetRepository.FindShip(map.ShipId) is null){issues.Add(Issue("Error","UnknownTargetPilotShip",map.SourceId,map.TargetId,$"Target ship '{map.ShipId}' does not exist in the converted repository."));continue;}
            pilots.Add(new FirstEditionPilot{Id=map.TargetId,Name=map.Name,ShipId=map.ShipId,Faction=map.Faction,PilotSkill=map.PilotSkill,SquadPointCost=map.SquadPointCost,Unique=map.Unique,UpgradeSlots=map.UpgradeSlots.ToArray(),Provenance=new ConversionProvenance{SourceId=source.Id,MappingId=map.MappingId,Kind=ConversionKind.Direct,MappingVersion=mappings.Version}});
        }
        return (pilots,issues);
    }
    private static ConversionIssue Issue(string severity,string code,string source,string target,string message)=>new(){Severity=severity,Category="Pilot",Code=code,SourceType="Pilot",SourceId=source,TargetId=target,Message=message};
}
