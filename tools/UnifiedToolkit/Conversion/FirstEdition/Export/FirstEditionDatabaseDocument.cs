using UnifiedToolkit.Conversion.FirstEdition.Pilots;
using UnifiedToolkit.Conversion.FirstEdition.Upgrades;

namespace UnifiedToolkit.Conversion.FirstEdition.Export;

public sealed class FirstEditionDatabaseDocument
{
    public string SchemaVersion { get; init; } = "1.0";
    public string MappingVersion { get; init; } = "";
    public DateTimeOffset GeneratedUtc { get; init; }
    public FirstEditionDatabaseSummary Summary { get; init; } = new();
    public IReadOnlyList<FirstEditionShip> Ships { get; init; } = Array.Empty<FirstEditionShip>();
    public IReadOnlyList<FirstEditionPilot> Pilots { get; init; } = Array.Empty<FirstEditionPilot>();
    public IReadOnlyList<FirstEditionUpgrade> Upgrades { get; init; } = Array.Empty<FirstEditionUpgrade>();
}

public sealed class FirstEditionDatabaseSummary
{
    public int ShipCount { get; init; }
    public int PilotCount { get; init; }
    public int UpgradeCount { get; init; }
    public IReadOnlyDictionary<string, int> ShipsByFaction { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> PilotsByFaction { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> UpgradesBySlot { get; init; } = new Dictionary<string, int>();
}
