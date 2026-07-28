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
    private readonly HashSet<string> _requiredCompositeEpicParentIds;

    public ShipConverter(ConversionMappingSet mappings, ConversionProfile profile)
    {
        _mappingVersion = mappings.Version;
        _profile = profile;
        _mappings = mappings.Ships.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
        _dispositions = mappings.ShipDispositions.ToDictionary(x => x.SourceId, StringComparer.OrdinalIgnoreCase);
        _requiredCompositeEpicParentIds = mappings.OfficialPilots
            .Select(x => ResolveCompositeEpicParentId(x.ShipId))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

        AddRequiredCompositeEpicParents(ships, issues);

        return new ShipConversionResult(ships, issues, excluded, deferred);
    }

    private void AddRequiredCompositeEpicParents(
        ICollection<FirstEditionShip> ships,
        ICollection<ConversionIssue> issues)
    {
        var existingIds = ships
            .Select(x => x.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var parentId in _requiredCompositeEpicParentIds
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (existingIds.Contains(parentId))
                continue;

            var definition = GetCompositeEpicParentDefinition(parentId);
            if (definition is null)
            {
                issues.Add(new ConversionIssue
                {
                    Severity = "Error",
                    Category = "Ship",
                    Code = "UnknownCompositeEpicParent",
                    SourceType = "Ship",
                    SourceId = parentId,
                    TargetId = parentId,
                    Message =
                        "An official Epic section requires a composite parent ship, but no parent definition exists."
                });
                continue;
            }

            var ship = new FirstEditionShip
            {
                Id = definition.Id,
                Name = definition.Name,
                Size = "epic",
                Attack = 0,
                Agility = 0,
                Hull = 0,
                Shields = 0,
                Provenance = new ConversionProvenance
                {
                    SourceId = $"official-epic-parent:{definition.Id}",
                    MappingId = $"official-epic-parent-{definition.Id}-v1",
                    Kind = ConversionKind.Official,
                    MappingVersion = _mappingVersion
                }
            };

            ship.Factions.Add(definition.Faction);
            ships.Add(ship);
            existingIds.Add(ship.Id);

            issues.Add(new ConversionIssue
            {
                Severity = "Information",
                Category = "Ship",
                Code = "CompositeEpicParentAdded",
                SourceType = "Ship",
                SourceId = ship.Provenance.SourceId,
                SourceName = ship.Name,
                TargetId = ship.Id,
                Message =
                    "Added a semantic parent shell for official fore/aft Epic ship sections. " +
                    "Section statistics and runtime object assembly remain represented by the section entries and later Epic phases."
            });
        }
    }

    private static string? ResolveCompositeEpicParentId(string sectionShipId) =>
        sectionShipId.ToLowerInvariant() switch
        {
            "cr90corvettefore" => "cr90corvette",
            "cr90corvetteaft" => "cr90corvette",
            "raiderclasscorvettefore" => "raiderclasscorvette",
            "raiderclasscorvetteaft" => "raiderclasscorvette",
            _ => null
        };

    private static CompositeEpicParentDefinition? GetCompositeEpicParentDefinition(string parentId) =>
        parentId.ToLowerInvariant() switch
        {
            "cr90corvette" => new CompositeEpicParentDefinition(
                "cr90corvette",
                "CR90 Corvette",
                "rebelalliance"),
            "raiderclasscorvette" => new CompositeEpicParentDefinition(
                "raiderclasscorvette",
                "Raider-class Corvette",
                "galacticempire"),
            _ => null
        };

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

public sealed record ShipConversionResult(
    IReadOnlyList<FirstEditionShip> Ships,
    IReadOnlyList<ConversionIssue> Issues,
    int ExcludedCount,
    int DeferredCount);

internal sealed record CompositeEpicParentDefinition(
    string Id,
    string Name,
    string Faction);
