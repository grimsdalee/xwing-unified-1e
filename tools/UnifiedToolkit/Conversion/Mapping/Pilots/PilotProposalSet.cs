namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public sealed class PilotProposalSet
{
    public IReadOnlyList<PilotMapping> CanonicalMappings { get; init; } = Array.Empty<PilotMapping>();
    public IReadOnlyList<PilotSourceAlternate> Alternates { get; init; } = Array.Empty<PilotSourceAlternate>();
}
