using UnifiedToolkit.Conversion.FirstEdition.Pilots;
using UnifiedToolkit.Conversion.FirstEdition.Upgrades;

namespace UnifiedToolkit.Conversion.FirstEdition;

public sealed class FirstEditionRepository
{
    private readonly Dictionary<string, FirstEditionShip> _shipsById;
    private readonly Dictionary<string, FirstEditionPilot> _pilotsByIdentity;
    private readonly Dictionary<string, FirstEditionUpgrade> _upgradesByIdentity;

    public FirstEditionRepository(
        IEnumerable<FirstEditionShip> ships,
        IEnumerable<FirstEditionPilot>? pilots = null,
        IEnumerable<FirstEditionUpgrade>? upgrades = null)
    {
        Ships = ships?.ToList() ?? throw new ArgumentNullException(nameof(ships));
        Pilots = pilots?.ToList() ?? new List<FirstEditionPilot>();
        Upgrades = upgrades?.ToList() ?? new List<FirstEditionUpgrade>();
        _shipsById = new(StringComparer.OrdinalIgnoreCase);
        _pilotsByIdentity = new(StringComparer.OrdinalIgnoreCase);
        _upgradesByIdentity = new(StringComparer.OrdinalIgnoreCase);

        foreach (var ship in Ships)
        {
            if (string.IsNullOrWhiteSpace(ship.Id)) throw new InvalidDataException("Cannot index First Edition ship with a blank ID.");
            if (!_shipsById.TryAdd(ship.Id, ship)) throw new InvalidDataException($"Duplicate First Edition ship ID '{ship.Id}'.");
        }

        foreach (var pilot in Pilots)
        {
            var key = PilotIdentity(pilot.Id, pilot.ShipId, pilot.Faction);
            if (!_pilotsByIdentity.TryAdd(key, pilot)) throw new InvalidDataException($"Duplicate First Edition pilot identity '{key}'.");
        }

        foreach (var upgrade in Upgrades)
        {
            var key = UpgradeIdentity(upgrade.Id, upgrade.Slot, upgrade.Factions, upgrade.ShipRestrictions);
            if (!_upgradesByIdentity.TryAdd(key, upgrade)) throw new InvalidDataException($"Duplicate First Edition upgrade identity '{key}'.");
        }
    }

    public IReadOnlyList<FirstEditionShip> Ships { get; }
    public IReadOnlyList<FirstEditionPilot> Pilots { get; }
    public IReadOnlyList<FirstEditionUpgrade> Upgrades { get; }

    public FirstEditionShip? FindShip(string id) => string.IsNullOrWhiteSpace(id) ? null : _shipsById.GetValueOrDefault(id);
    public FirstEditionPilot? FindPilot(string id, string shipId, string faction) => _pilotsByIdentity.GetValueOrDefault(PilotIdentity(id, shipId, faction));
    public FirstEditionUpgrade? FindUpgrade(string id, string slot, IEnumerable<string> factions, IEnumerable<string> shipRestrictions) =>
        _upgradesByIdentity.GetValueOrDefault(UpgradeIdentity(id, slot, factions, shipRestrictions));

    public static string PilotIdentity(string id, string shipId, string faction) => $"{id}|{shipId}|{faction}";
    public static string UpgradeIdentity(string id, string slot, IEnumerable<string> factions, IEnumerable<string> shipRestrictions) =>
        $"{id}|{slot}|{string.Join(';', factions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}|{string.Join(';', shipRestrictions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}";
}
