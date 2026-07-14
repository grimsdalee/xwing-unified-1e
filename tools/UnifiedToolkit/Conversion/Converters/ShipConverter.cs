using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.Issues;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Dispositions;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.Converters;

public sealed class ShipConverter
{
    private readonly Dictionary<string, ShipMapping> _mappings;
    private readonly Dictionary<string, ShipDisposition> _dispositions;
    private readonly string _mappingVersion;
    private readonly ConversionProfile _profile;

    public ShipConverter(ConversionMappingSet mappings, ConversionProfile profile)
    {
        _mappingVersion = mappings.Version;
        _profile = profile;
        _mappings = mappings.Ships.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
        _dispositions = mappings.ShipDispositions.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
    }

    public ShipConversionResult Convert(IReadOnlyList<ShipDefinition> sourceShips)
    {
        var ships = new List<FirstEditionShip>();
        var issues = new List<ConversionIssue>();
        var excluded = 0;
        var deferred = 0;

        foreach (var source in sourceShips)
        {
            if (!_mappings.TryGetValue(source.Id, out var mapping))
            {
                if (_dispositions.TryGetValue(source.Id, out var disposition))
                {
                    if (disposition.Kind == ShipDispositionKind.Excluded) excluded++;
                    else deferred++;
                    issues.Add(new ConversionIssue
                    {
                        Severity = "Information",
                        Category = "Ship",
                        Code = disposition.Kind == ShipDispositionKind.Excluded ? "ExcludedByDisposition" : "DeferredByDisposition",
                        SourceType = "Ship",
                        SourceId = source.Id,
                        SourceName = source.Name,
                        TargetId = disposition.ProposedTargetId,
                        Message = $"{disposition.Kind}: {disposition.Reason}"
                    });
                    continue;
                }

                issues.Add(CreateUnmappedIssue(source));
                continue;
            }

            if (mapping.Kind == ConversionKind.Excluded)
            {
                excluded++;
                issues.Add(new ConversionIssue { Severity = "Information", Category = "Ship", Code = "ExcludedByMapping", SourceType = "Ship", SourceId = source.Id, SourceName = source.Name, Message = mapping.ExclusionReason });
                continue;
            }

            var target = new FirstEditionShip
            {
                Id = mapping.TargetId,
                Name = mapping.Name,
                Size = mapping.Size,
                Attack = mapping.Attack,
                Agility = mapping.Agility,
                Hull = mapping.Hull,
                Shields = mapping.Shields,
                Provenance = new ConversionProvenance { SourceId = source.Id, MappingId = mapping.MappingId, Kind = mapping.Kind, MappingVersion = _mappingVersion }
            };
            target.Actions.AddRange(mapping.Actions);
            target.Factions.AddRange(mapping.Factions);
            ships.Add(target);
        }

        return new ShipConversionResult(ships, issues, excluded, deferred);
    }

    private ConversionIssue CreateUnmappedIssue(ShipDefinition source) => new()
    {
        Severity = _profile.UnmappedShips == ConversionPolicy.Error ? "Error" : "Warning",
        Category = "Ship",
        Code = "MissingShipMapping",
        SourceType = "Ship",
        SourceId = source.Id,
        SourceName = source.Name,
        Message = "No First Edition ship mapping or reviewed disposition exists for this source ship."
    };
}

public sealed record ShipConversionResult(IReadOnlyList<FirstEditionShip> Ships, IReadOnlyList<ConversionIssue> Issues, int ExcludedCount, int DeferredCount);
