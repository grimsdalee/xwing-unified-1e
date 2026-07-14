using UnifiedToolkit.Conversion.Mapping.Dispositions;
using UnifiedToolkit.Conversion.Mapping.Pilots;
using UnifiedToolkit.Conversion.Mapping.Upgrades;

namespace UnifiedToolkit.Conversion.Mapping;

public sealed class ConversionMappingSet
{
    public string Version { get; init; } = "";
    public IReadOnlyList<ShipMapping> Ships { get; init; } = Array.Empty<ShipMapping>();
    public IReadOnlyList<ShipDisposition> ShipDispositions { get; init; } = Array.Empty<ShipDisposition>();
    public IReadOnlyList<PilotMapping> Pilots { get; init; } = Array.Empty<PilotMapping>();
    public IReadOnlyList<PilotSourceAlternate> PilotSourceAlternates { get; init; } = Array.Empty<PilotSourceAlternate>();
    public IReadOnlyList<PilotDisposition> PilotDispositions { get; init; } = Array.Empty<PilotDisposition>();
    public IReadOnlyList<UpgradeMapping> Upgrades { get; init; } = Array.Empty<UpgradeMapping>();
    public IReadOnlyList<UpgradeSourceAlternate> UpgradeSourceAlternates { get; init; } = Array.Empty<UpgradeSourceAlternate>();
    public IReadOnlyList<UpgradeDisposition> UpgradeDispositions { get; init; } = Array.Empty<UpgradeDisposition>();
}
