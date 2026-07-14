namespace UnifiedToolkit.Conversion.Mapping.Upgrades;

public sealed class UpgradeProposalSet
{
    public IReadOnlyList<UpgradeMapping> CanonicalMappings { get; init; } = Array.Empty<UpgradeMapping>();
    public IReadOnlyList<UpgradeSourceAlternate> Alternates { get; init; } = Array.Empty<UpgradeSourceAlternate>();
}
