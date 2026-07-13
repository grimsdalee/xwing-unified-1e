using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.Issues;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Conversion;

public sealed class ConversionResult
{
    public required FirstEditionRepository Repository { get; init; }
    public IReadOnlyList<ConversionIssue> Issues { get; init; } = Array.Empty<ConversionIssue>();
    public IReadOnlyList<ShipMappingCoverageEntry> ShipCoverage { get; init; } = Array.Empty<ShipMappingCoverageEntry>();
    public int SourceShipCount { get; init; }
    public int ExcludedShipCount { get; init; }
    public int UnmappedShipCount { get; init; }
}
