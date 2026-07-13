using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Repository;

public sealed class Repository
{
    private readonly Dictionary<string, ShipDefinition> _shipsById;
    private readonly Dictionary<string, PilotDefinition> _pilotsById;
    private readonly Dictionary<string, UpgradeDefinition> _upgradesById;

    public Repository(
        IEnumerable<ShipDefinition> ships,
        IEnumerable<PilotDefinition> pilots,
        IEnumerable<UpgradeDefinition> upgrades)
    {
        ArgumentNullException.ThrowIfNull(ships);
        ArgumentNullException.ThrowIfNull(pilots);
        ArgumentNullException.ThrowIfNull(upgrades);

        Ships = ships.ToList();
        Pilots = pilots.ToList();
        Upgrades = upgrades.ToList();

        _shipsById = BuildIndex(
            Ships,
            ship => ship.Id,
            "ship");

        _pilotsById = BuildIndex(
            Pilots,
            pilot => pilot.Id,
            "pilot");

        _upgradesById = BuildIndex(
            Upgrades,
            upgrade => upgrade.Id,
            "upgrade");
    }

    public IReadOnlyList<ShipDefinition> Ships { get; }

    public IReadOnlyList<PilotDefinition> Pilots { get; }

    public IReadOnlyList<UpgradeDefinition> Upgrades { get; }

    public ShipDefinition? FindShip(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return _shipsById.TryGetValue(id, out var ship)
            ? ship
            : null;
    }

    public PilotDefinition? FindPilot(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return _pilotsById.TryGetValue(id, out var pilot)
            ? pilot
            : null;
    }

    public UpgradeDefinition? FindUpgrade(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return _upgradesById.TryGetValue(id, out var upgrade)
            ? upgrade
            : null;
    }

    public override string ToString()
    {
        return
            $"Ships={Ships.Count}, " +
            $"Pilots={Pilots.Count}, " +
            $"Upgrades={Upgrades.Count}";
    }

    private static Dictionary<string, T> BuildIndex<T>(
        IEnumerable<T> items,
        Func<T, string> idSelector,
        string itemType)
    {
        var index = new Dictionary<string, T>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var id = idSelector(item);

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException(
                    $"Cannot index {itemType} with a blank ID.");
            }

            if (!index.TryAdd(id, item))
            {
                throw new InvalidDataException(
                    $"Duplicate {itemType} ID '{id}'.");
            }
        }

        return index;
    }
}