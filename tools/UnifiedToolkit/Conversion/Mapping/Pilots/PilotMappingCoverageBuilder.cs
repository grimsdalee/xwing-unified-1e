using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public static class PilotMappingCoverageBuilder
{
    public static IReadOnlyList<PilotMappingCoverageEntry> Build(
        IEnumerable<PilotDefinition> sourcePilots,
        IEnumerable<PilotMapping> mappings,
        IEnumerable<PilotSourceAlternate> alternates,
        IEnumerable<PilotDisposition> dispositions)
    {
        var canonical = mappings.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
        var alternate = alternates.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
        var disposition = dispositions.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
        return sourcePilots.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(source => Build(source, canonical, alternate, disposition)).ToList();
    }

    private static PilotMappingCoverageEntry Build(PilotDefinition source,
        IReadOnlyDictionary<string,PilotMapping> canonical,
        IReadOnlyDictionary<string,PilotSourceAlternate> alternate,
        IReadOnlyDictionary<string,PilotDisposition> disposition)
    {
        var sourceShipId = source.Ship?.Id ?? source.ShipType;
        if (canonical.TryGetValue(source.Id, out var map))
            return Entry(source, sourceShipId, "ConvertedCanonical", source.Id, map.TargetId, map.ShipId, map.Faction, "Canonical First Edition pilot mapping.");
        if (alternate.TryGetValue(source.Id, out var alt))
            return Entry(source, sourceShipId, "AlternatePrinting", alt.CanonicalSourceId, alt.TargetId, alt.TargetShipId, alt.TargetFaction, alt.Notes);
        if (disposition.TryGetValue(source.Id, out var disp))
            return Entry(source, sourceShipId, disp.Kind.ToString(), "", "", "", "", disp.Reason);
        return Entry(source, sourceShipId, "Unmapped", "", "", "", "", "No pilot mapping or explicit disposition exists.");
    }

    private static PilotMappingCoverageEntry Entry(PilotDefinition p,string ship,string status,string canonical,string target,string targetShip,string faction,string notes) => new()
    { SourceId=p.Id,SourceName=p.Name,SourceShipId=ship,SourceFaction=p.Faction,Status=status,CanonicalSourceId=canonical,TargetId=target,TargetShipId=targetShip,TargetFaction=faction,Notes=notes };
}
