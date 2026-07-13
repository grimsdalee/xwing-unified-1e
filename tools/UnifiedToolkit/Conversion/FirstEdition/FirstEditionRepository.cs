namespace UnifiedToolkit.Conversion.FirstEdition;

public sealed class FirstEditionRepository
{
    private readonly Dictionary<string, FirstEditionShip> _shipsById;

    public FirstEditionRepository(IEnumerable<FirstEditionShip> ships)
    {
        ArgumentNullException.ThrowIfNull(ships);
        Ships = ships.ToList();
        _shipsById = new Dictionary<string, FirstEditionShip>(StringComparer.OrdinalIgnoreCase);

        foreach (var ship in Ships)
        {
            if (string.IsNullOrWhiteSpace(ship.Id))
                throw new InvalidDataException("Cannot index First Edition ship with a blank ID.");

            if (!_shipsById.TryAdd(ship.Id, ship))
                throw new InvalidDataException($"Duplicate First Edition ship ID '{ship.Id}'.");
        }
    }

    public IReadOnlyList<FirstEditionShip> Ships { get; }

    public FirstEditionShip? FindShip(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return _shipsById.TryGetValue(id, out var ship) ? ship : null;
    }
}
