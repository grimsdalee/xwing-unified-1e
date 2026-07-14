namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public static class PilotMappingValidator
{
    public static IReadOnlyList<string> Validate(IEnumerable<PilotMapping> mappings, IEnumerable<PilotSourceAlternate> alternates)
    {
        var issues = new List<string>();
        var mapList = mappings.ToList();
        var altList = alternates.ToList();

        AddDuplicates(issues, mapList, x => x.MappingId, "Duplicate mapping ID");
        AddDuplicates(issues, mapList, x => x.SourceId, "Duplicate canonical source ID");
        AddDuplicates(issues, mapList, PilotProposalCanonicalizer.IdentityKey, "Duplicate target pilot identity");
        AddDuplicates(issues, altList, x => x.SourceId, "Duplicate alternate source ID");

        var canonicalSources = mapList.Select(x => x.SourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mapList)
        {
            if (string.IsNullOrWhiteSpace(mapping.MappingId)) issues.Add("Pilot mapping has a blank mapping ID.");
            if (string.IsNullOrWhiteSpace(mapping.SourceId)) issues.Add($"Pilot mapping '{mapping.MappingId}' has a blank source ID.");
            if (string.IsNullOrWhiteSpace(mapping.TargetId)) issues.Add($"Pilot mapping '{mapping.MappingId}' has a blank target ID.");
            if (string.IsNullOrWhiteSpace(mapping.ShipId)) issues.Add($"Pilot mapping '{mapping.MappingId}' has a blank target ship ID.");
            if (string.IsNullOrWhiteSpace(mapping.Faction)) issues.Add($"Pilot mapping '{mapping.MappingId}' has a blank faction.");
            if (mapping.PilotSkill < 0 || mapping.PilotSkill > 12) issues.Add($"Pilot mapping '{mapping.MappingId}' has invalid pilot skill {mapping.PilotSkill}.");
            if (mapping.SquadPointCost < 0) issues.Add($"Pilot mapping '{mapping.MappingId}' has invalid squad point cost {mapping.SquadPointCost}.");
        }

        foreach (var alternate in altList)
        {
            if (!canonicalSources.Contains(alternate.CanonicalSourceId))
                issues.Add($"Alternate source '{alternate.SourceId}' references missing canonical source '{alternate.CanonicalSourceId}'.");
            if (canonicalSources.Contains(alternate.SourceId))
                issues.Add($"Source '{alternate.SourceId}' is both canonical and alternate.");
        }

        return issues;
    }

    private static void AddDuplicates<T>(List<string> issues, IEnumerable<T> items, Func<T, string> keySelector, string label)
    {
        foreach (var group in items.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Count() > 1))
            issues.Add($"{label}: '{group.Key}' appears {group.Count()} times.");
    }
}
