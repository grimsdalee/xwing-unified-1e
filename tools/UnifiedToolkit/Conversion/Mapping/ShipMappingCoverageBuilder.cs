using UnifiedToolkit.Conversion.Mapping.Dispositions;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.Mapping;

public static class ShipMappingCoverageBuilder
{
    public static IReadOnlyList<ShipMappingCoverageEntry> Build(IEnumerable<ShipDefinition> sourceShips, ConversionMappingSet mappings)
    {
        var bySource = mappings.Ships.Where(x => !string.IsNullOrWhiteSpace(x.SourceId)).GroupBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var dispositions = mappings.ShipDispositions.Where(x => !string.IsNullOrWhiteSpace(x.SourceId)).GroupBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        return sourceShips.Select(x => Create(x, bySource, dispositions)).OrderBy(x => x.Status).ThenBy(x => x.SourceName).ToList();
    }

    private static ShipMappingCoverageEntry Create(ShipDefinition source, IReadOnlyDictionary<string, ShipMapping> mappings, IReadOnlyDictionary<string, ShipDisposition> dispositions)
    {
        if (mappings.TryGetValue(source.Id, out var mapping))
        {
            return new ShipMappingCoverageEntry { SourceId = source.Id, SourceName = source.Name, SourceSize = source.Size, Status = mapping.Kind == ConversionKind.Excluded ? "Excluded" : "Converted", MappingId = mapping.MappingId, TargetId = mapping.TargetId, TargetName = mapping.Name, Kind = mapping.Kind, Reason = mapping.ExclusionReason };
        }
        if (dispositions.TryGetValue(source.Id, out var disposition))
        {
            var status = disposition.Kind switch { ShipDispositionKind.Excluded => "Excluded", ShipDispositionKind.Deferred => "Deferred", ShipDispositionKind.Custom => "PlannedCustom", ShipDispositionKind.Adapted => "PlannedAdapted", ShipDispositionKind.Alias => "PlannedAlias", _ => "Unmapped" };
            return new ShipMappingCoverageEntry { SourceId = source.Id, SourceName = source.Name, SourceSize = source.Size, Status = status, TargetId = disposition.ProposedTargetId, Disposition = disposition.Kind.ToString(), Reason = disposition.Reason };
        }
        return new ShipMappingCoverageEntry { SourceId = source.Id, SourceName = source.Name, SourceSize = source.Size, Status = "Unmapped" };
    }
}
