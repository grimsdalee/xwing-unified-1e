using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.Mapping;

public static class ShipMappingCoverageBuilder
{
    public static IReadOnlyList<ShipMappingCoverageEntry> Build(
        IEnumerable<ShipDefinition> sourceShips,
        ConversionMappingSet mappings)
    {
        ArgumentNullException.ThrowIfNull(sourceShips);
        ArgumentNullException.ThrowIfNull(mappings);

        var mappingsBySourceId = mappings.Ships
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceId))
            .GroupBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        return sourceShips
            .Select(source => CreateEntry(source, mappingsBySourceId))
            .OrderBy(x => x.Status, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ShipMappingCoverageEntry CreateEntry(
        ShipDefinition source,
        IReadOnlyDictionary<string, ShipMapping> mappingsBySourceId)
    {
        if (!mappingsBySourceId.TryGetValue(source.Id, out var mapping))
        {
            return new ShipMappingCoverageEntry
            {
                SourceId = source.Id,
                SourceName = source.Name,
                SourceSize = source.Size,
                Status = "Unmapped"
            };
        }

        var status = mapping.Kind == ConversionKind.Excluded ? "Excluded" : "Converted";
        return new ShipMappingCoverageEntry
        {
            SourceId = source.Id,
            SourceName = source.Name,
            SourceSize = source.Size,
            Status = status,
            MappingId = mapping.MappingId,
            TargetId = mapping.TargetId,
            TargetName = mapping.Name,
            Kind = mapping.Kind,
            ExclusionReason = mapping.ExclusionReason
        };
    }
}
