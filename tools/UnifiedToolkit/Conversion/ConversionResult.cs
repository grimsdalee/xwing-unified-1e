using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.Issues;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Pilots;
using UnifiedToolkit.Conversion.Mapping.Upgrades;

namespace UnifiedToolkit.Conversion;

public sealed class ConversionResult
{
    public required FirstEditionRepository Repository { get; init; }
    public IReadOnlyList<ConversionIssue> Issues { get; init; } = Array.Empty<ConversionIssue>();
    public IReadOnlyList<ShipMappingCoverageEntry> ShipCoverage { get; init; } = Array.Empty<ShipMappingCoverageEntry>();
    public IReadOnlyList<PilotMappingCoverageEntry> PilotCoverage { get; init; } = Array.Empty<PilotMappingCoverageEntry>();
    public IReadOnlyList<UpgradeMappingCoverageEntry> UpgradeCoverage { get; init; } = Array.Empty<UpgradeMappingCoverageEntry>();
    public int SourceShipCount { get; init; }
    public int ExcludedShipCount { get; init; }
    public int DeferredShipCount { get; init; }
    public int UnmappedShipCount { get; init; }
}
