using UnifiedToolkit.Conversion.FirstEdition.Pilots;
using UnifiedToolkit.Conversion.FirstEdition.Upgrades;

namespace UnifiedToolkit.Conversion.FirstEdition;

public sealed class FirstEditionRepository
{
    private readonly Dictionary<string, FirstEditionShip> _shipsById;
    private readonly Dictionary<string, FirstEditionPilot> _pilotsByIdentity;
    private readonly Dictionary<string, List<FirstEditionPilot>> _pilotsById;
    private readonly Dictionary<string, List<FirstEditionPilot>> _pilotsByShip;
    private readonly Dictionary<string, List<FirstEditionPilot>> _pilotsByFaction;
    private readonly Dictionary<string, FirstEditionUpgrade> _upgradesByIdentity;
    private readonly Dictionary<string, List<FirstEditionUpgrade>> _upgradesById;
    private readonly Dictionary<string, List<FirstEditionUpgrade>> _upgradesBySlot;
    private readonly Dictionary<string, List<FirstEditionUpgrade>> _upgradesByFaction;
    private readonly Dictionary<string, List<FirstEditionUpgrade>> _upgradesByShipRestriction;
    private readonly Dictionary<string, List<FirstEditionUpgrade>> _upgradesBySizeRestriction;

    public FirstEditionRepository(
        IEnumerable<FirstEditionShip> ships,
        IEnumerable<FirstEditionPilot>? pilots = null,
        IEnumerable<FirstEditionUpgrade>? upgrades = null)
    {
        Ships = ships?.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList()
            ?? throw new ArgumentNullException(nameof(ships));
        Pilots = pilots?.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ShipId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Faction, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<FirstEditionPilot>();
        Upgrades = upgrades?.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Slot, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<FirstEditionUpgrade>();

        _shipsById = new(StringComparer.OrdinalIgnoreCase);
        _pilotsByIdentity = new(StringComparer.OrdinalIgnoreCase);
        _pilotsById = new(StringComparer.OrdinalIgnoreCase);
        _pilotsByShip = new(StringComparer.OrdinalIgnoreCase);
        _pilotsByFaction = new(StringComparer.OrdinalIgnoreCase);
        _upgradesByIdentity = new(StringComparer.OrdinalIgnoreCase);
        _upgradesById = new(StringComparer.OrdinalIgnoreCase);
        _upgradesBySlot = new(StringComparer.OrdinalIgnoreCase);
        _upgradesByFaction = new(StringComparer.OrdinalIgnoreCase);
        _upgradesByShipRestriction = new(StringComparer.OrdinalIgnoreCase);
        _upgradesBySizeRestriction = new(StringComparer.OrdinalIgnoreCase);

        foreach (var ship in Ships)
        {
            if (string.IsNullOrWhiteSpace(ship.Id))
                throw new InvalidDataException("Cannot index First Edition ship with a blank ID.");
            if (!_shipsById.TryAdd(ship.Id, ship))
                throw new InvalidDataException($"Duplicate First Edition ship ID '{ship.Id}'.");
        }

        foreach (var pilot in Pilots)
        {
            var key = PilotIdentity(pilot.Id, pilot.ShipId, pilot.Faction);
            if (!_pilotsByIdentity.TryAdd(key, pilot))
                throw new InvalidDataException($"Duplicate First Edition pilot identity '{key}'.");

            Add(_pilotsById, pilot.Id, pilot);
            Add(_pilotsByShip, pilot.ShipId, pilot);
            Add(_pilotsByFaction, pilot.Faction, pilot);
        }

        foreach (var upgrade in Upgrades)
        {
            var key = UpgradeIdentity(upgrade.Id, upgrade.Slot, upgrade.Factions, upgrade.ShipRestrictions);
            if (!_upgradesByIdentity.TryAdd(key, upgrade))
                throw new InvalidDataException($"Duplicate First Edition upgrade identity '{key}'.");

            Add(_upgradesById, upgrade.Id, upgrade);
            Add(_upgradesBySlot, upgrade.Slot, upgrade);
            foreach (var faction in upgrade.Factions) Add(_upgradesByFaction, faction, upgrade);
            foreach (var shipId in upgrade.ShipRestrictions) Add(_upgradesByShipRestriction, shipId, upgrade);
            foreach (var size in upgrade.SizeRestrictions) Add(_upgradesBySizeRestriction, size, upgrade);
        }
    }

    public IReadOnlyList<FirstEditionShip> Ships { get; }
    public IReadOnlyList<FirstEditionPilot> Pilots { get; }
    public IReadOnlyList<FirstEditionUpgrade> Upgrades { get; }

    public FirstEditionShip? FindShip(string id) =>
        string.IsNullOrWhiteSpace(id) ? null : _shipsById.GetValueOrDefault(id);

    public FirstEditionPilot? FindPilot(string id, string shipId, string faction) =>
        _pilotsByIdentity.GetValueOrDefault(PilotIdentity(id, shipId, faction));

    public IReadOnlyList<FirstEditionPilot> FindPilotsById(string id) => Get(_pilotsById, id);
    public IReadOnlyList<FirstEditionPilot> FindPilotsByShip(string shipId) => Get(_pilotsByShip, shipId);
    public IReadOnlyList<FirstEditionPilot> FindPilotsByFaction(string faction) => Get(_pilotsByFaction, faction);

    public FirstEditionUpgrade? FindUpgrade(
        string id,
        string slot,
        IEnumerable<string> factions,
        IEnumerable<string> shipRestrictions) =>
        _upgradesByIdentity.GetValueOrDefault(UpgradeIdentity(id, slot, factions, shipRestrictions));

    public IReadOnlyList<FirstEditionUpgrade> FindUpgradesById(string id) => Get(_upgradesById, id);
    public IReadOnlyList<FirstEditionUpgrade> FindUpgradesBySlot(string slot) => Get(_upgradesBySlot, slot);
    public IReadOnlyList<FirstEditionUpgrade> FindUpgradesByFaction(string faction) => Get(_upgradesByFaction, faction);
    public IReadOnlyList<FirstEditionUpgrade> FindUpgradesRestrictedToShip(string shipId) => Get(_upgradesByShipRestriction, shipId);
    public IReadOnlyList<FirstEditionUpgrade> FindUpgradesRestrictedToSize(string size) => Get(_upgradesBySizeRestriction, size);

    public static string PilotIdentity(string id, string shipId, string faction) =>
        $"{id}|{shipId}|{faction}";

    public static string UpgradeIdentity(
        string id,
        string slot,
        IEnumerable<string> factions,
        IEnumerable<string> shipRestrictions) =>
        $"{id}|{slot}|{string.Join(';', factions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}|{string.Join(';', shipRestrictions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}";

    private static void Add<T>(Dictionary<string, List<T>> index, string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!index.TryGetValue(key, out var values))
        {
            values = new List<T>();
            index.Add(key, values);
        }
        values.Add(value);
    }

    private static IReadOnlyList<T> Get<T>(Dictionary<string, List<T>> index, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Array.Empty<T>();
        return index.TryGetValue(key, out var values) ? values : Array.Empty<T>();
    }
}
