using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public sealed class OfficialAliasCandidate
{
    public required ShipDefinition Source { get; init; }
    public FirstEditionDataShip? Target { get; init; }
    public string SuggestedTargetId { get; init; } = "";
    public string MatchMethod { get; init; } = "";
    public decimal Confidence { get; init; }
    public string Decision { get; init; } = "";
    public string Notes { get; init; } = "";
    public ShipMapping? ProposedMapping { get; init; }
}
