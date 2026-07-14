namespace UnifiedToolkit.Conversion.Mapping.Upgrades;

public static class UpgradeProposalCanonicalizer
{
    public static UpgradeProposalSet Canonicalize(IEnumerable<UpgradeMapping> raw)
    {
        var canonical = new List<UpgradeMapping>();
        var alternates = new List<UpgradeSourceAlternate>();

        foreach (var group in raw.GroupBy(x => $"{x.TargetId}|{x.Slot}", StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(x => SourcePreference(x.SourceId)).ThenBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase).ToList();
            var first = ordered[0];
            canonical.Add(WithMappingId(first, BuildMappingId(first)));
            foreach (var alternate in ordered.Skip(1))
            {
                alternates.Add(new UpgradeSourceAlternate
                {
                    SourceId = alternate.SourceId,
                    CanonicalSourceId = first.SourceId,
                    TargetId = first.TargetId,
                    TargetSlot = first.Slot
                });
            }
        }

        return new UpgradeProposalSet
        {
            CanonicalMappings = canonical.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Slot, StringComparer.OrdinalIgnoreCase).ToList(),
            Alternates = alternates.OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static UpgradeMapping WithMappingId(UpgradeMapping source, string mappingId) => new()
    {
        MappingId = mappingId, SourceId = source.SourceId, TargetId = source.TargetId, Name = source.Name,
        Slot = source.Slot, SquadPointCost = source.SquadPointCost, Unique = source.Unique,
        Factions = source.Factions, ShipRestrictions = source.ShipRestrictions,
        SizeRestrictions = source.SizeRestrictions, Text = source.Text
    };

    private static string BuildMappingId(UpgradeMapping mapping) => $"upgrade-{Token(mapping.TargetId)}-{Token(mapping.Slot)}-direct-v1";
    private static int SourcePreference(string id) => id.Contains('-', StringComparison.Ordinal) ? 1 : 0;
    private static string Token(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
