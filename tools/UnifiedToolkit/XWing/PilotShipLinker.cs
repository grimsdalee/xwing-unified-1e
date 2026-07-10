namespace UnifiedToolkit.XWing;

public static class PilotShipLinker
{
    public static void Link(
        IEnumerable<PilotDefinition> pilots,
        IEnumerable<ShipDefinition> ships)
    {
        ArgumentNullException.ThrowIfNull(pilots);
        ArgumentNullException.ThrowIfNull(ships);

        var shipsById = ships
            .GroupBy(
                ship => ship.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var pilot in pilots)
        {
            pilot.Ship = null;

            if (string.IsNullOrWhiteSpace(pilot.ShipType))
                continue;

            if (shipsById.TryGetValue(
                    pilot.ShipType,
                    out var ship))
            {
                pilot.Ship = ship;
            }
        }
    }
}