namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public static class PilotProposalCanonicalizer
{
    public static PilotProposalSet Canonicalize(IEnumerable<PilotMapping> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        var canonical = new List<PilotMapping>();
        var alternates = new List<PilotSourceAlternate>();

        var groups = proposals
            .GroupBy(x => IdentityKey(x), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var ordered = group
                .OrderByDescending(x => x.SourceId.Equals(x.TargetId, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.SourceId.Length)
                .ThenBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selected = ordered[0];
            var uniqueMappingId = BuildMappingId(selected);
            var canonicalMapping = Copy(selected, uniqueMappingId);
            canonical.Add(canonicalMapping);

            foreach (var alternate in ordered.Skip(1))
            {
                alternates.Add(new PilotSourceAlternate
                {
                    SourceId = alternate.SourceId,
                    CanonicalSourceId = selected.SourceId,
                    TargetId = selected.TargetId,
                    TargetShipId = selected.ShipId,
                    TargetFaction = selected.Faction,
                    Relationship = "AlternatePrinting",
                    Notes = "This Unified source record resolves to the same First Edition pilot identity as the canonical source record."
                });
            }
        }

        return new PilotProposalSet
        {
            CanonicalMappings = canonical.OrderBy(x => x.TargetId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ShipId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Faction, StringComparer.OrdinalIgnoreCase).ToList(),
            Alternates = alternates.OrderBy(x => x.CanonicalSourceId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public static string IdentityKey(PilotMapping mapping) => $"{mapping.TargetId}|{mapping.ShipId}|{mapping.Faction}";

    private static string BuildMappingId(PilotMapping mapping)
    {
        var target = Slug(mapping.TargetId);
        var ship = Slug(mapping.ShipId);
        var faction = Slug(mapping.Faction);
        return $"pilot-{target}-{ship}-{faction}-direct-v1";
    }

    private static PilotMapping Copy(PilotMapping source, string mappingId) => new()
    {
        MappingId = mappingId,
        SourceId = source.SourceId,
        TargetId = source.TargetId,
        Name = source.Name,
        ShipId = source.ShipId,
        Faction = source.Faction,
        PilotSkill = source.PilotSkill,
        SquadPointCost = source.SquadPointCost,
        Unique = source.Unique,
        UpgradeSlots = source.UpgradeSlots.ToArray()
    };

    private static string Slug(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
