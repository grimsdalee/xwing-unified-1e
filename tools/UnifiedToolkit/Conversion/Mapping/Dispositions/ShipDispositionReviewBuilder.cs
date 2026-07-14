using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.Mapping.Dispositions;

public static class ShipDispositionReviewBuilder
{
    public static IReadOnlyList<ShipDispositionReviewEntry> Build(
        IEnumerable<ShipDefinition> sourceShips,
        ConversionMappingSet mappings)
    {
        var mapped = mappings.Ships.Select(x => x.SourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = mappings.ShipDispositions
            .ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);

        return sourceShips
            .Where(x => !mapped.Contains(x.Id))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(ship =>
            {
                existing.TryGetValue(ship.Id, out var disposition);
                return new ShipDispositionReviewEntry
                {
                    SourceId = ship.Id,
                    SourceName = ship.Name,
                    SourceFactions = string.Join(";", ship.Factions.OrderBy(x => x)),
                    SourceSize = ship.Size,
                    SourceHull = ship.Hull,
                    SourceShields = ship.Shield,
                    SourceAgility = ship.Agility,
                    Kind = disposition?.Kind ?? ShipDispositionKind.Unreviewed,
                    ProposedTargetId = disposition?.ProposedTargetId ?? "",
                    Reason = disposition?.Reason ?? "",
                    Notes = disposition?.Notes ?? ""
                };
            })
            .ToList();
    }
}
