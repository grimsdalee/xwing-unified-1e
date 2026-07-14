using UnifiedToolkit.Conversion.Converters;
using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.FirstEdition.Validation;
using UnifiedToolkit.Conversion.Issues;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Pilots;
using UnifiedToolkit.Conversion.Mapping.Upgrades;

namespace UnifiedToolkit.Conversion;

public static class ConversionEngine
{
    public static ConversionResult ConvertRepository(global::UnifiedToolkit.Repository.Repository source, ConversionMappingSet mappings, ConversionProfile profile)
    {
        var issues = new List<ConversionIssue>();
        var shipCoverage = ShipMappingCoverageBuilder.Build(source.Ships, mappings);
        var pilotCoverage = PilotMappingCoverageBuilder.Build(source.Pilots, mappings.Pilots, mappings.PilotSourceAlternates, mappings.PilotDispositions);
        var upgradeCoverage = UpgradeMappingCoverageBuilder.Build(source.Upgrades, mappings.Upgrades, mappings.UpgradeSourceAlternates, mappings.UpgradeDispositions);

        issues.AddRange(ConversionMappingValidator.Validate(mappings));
        foreach (var text in PilotMappingValidator.Validate(mappings.Pilots, mappings.PilotSourceAlternates))
            issues.Add(Error("Mapping", "InvalidPilotMapping", "Pilot", text));
        foreach (var text in UpgradeMappingValidator.Validate(mappings.Upgrades, mappings.UpgradeSourceAlternates))
            issues.Add(Error("Mapping", "InvalidUpgradeMapping", "Upgrade", text));

        ValidateSourceClassifications(source, mappings, issues);
        if (issues.Any(IsError)) return Empty(source, shipCoverage, pilotCoverage, upgradeCoverage, issues);

        var shipConversion = new ShipConverter(mappings, profile).Convert(source.Ships);
        issues.AddRange(shipConversion.Issues);
        var pilotConversion = PilotConverter.Convert(source.Pilots, mappings, new FirstEditionRepository(shipConversion.Ships));
        issues.AddRange(pilotConversion.Issues);
        var upgradeConversion = UpgradeConverter.Convert(source.Upgrades, mappings);
        issues.AddRange(upgradeConversion.Issues);

        FirstEditionRepository repository;
        try
        {
            repository = new FirstEditionRepository(shipConversion.Ships, pilotConversion.Pilots, upgradeConversion.Upgrades);
        }
        catch (Exception ex)
        {
            issues.Add(Error("TargetValidation", "FirstEditionRepositoryBuildFailed", "Repository", ex.Message));
            return Empty(source, shipCoverage, pilotCoverage, upgradeCoverage, issues);
        }

        issues.AddRange(FirstEditionRepositoryValidator.Validate(repository));
        AddInformation(pilotCoverage, "Pilot", issues);
        AddInformation(upgradeCoverage, "Upgrade", issues);

        return new ConversionResult
        {
            Repository = repository,
            Issues = issues,
            ShipCoverage = shipCoverage,
            PilotCoverage = pilotCoverage,
            UpgradeCoverage = upgradeCoverage,
            SourceShipCount = source.Ships.Count,
            ExcludedShipCount = shipConversion.ExcludedCount,
            DeferredShipCount = shipConversion.DeferredCount,
            UnmappedShipCount = shipCoverage.Count(x => x.Status == "Unmapped")
        };
    }

    public static ConversionResult ConvertShips(global::UnifiedToolkit.Repository.Repository source, ConversionMappingSet mappings, ConversionProfile profile) => ConvertRepository(source, mappings, profile);

    private static void ValidateSourceClassifications(global::UnifiedToolkit.Repository.Repository source, ConversionMappingSet mappings, List<ConversionIssue> issues)
    {
        ValidateClassification(
            source.Pilots.Select(x => x.Id),
            mappings.Pilots.Select(x => x.SourceId),
            mappings.PilotSourceAlternates.Select(x => x.SourceId),
            mappings.PilotDispositions.Select(x => x.SourceId),
            "Pilot",
            issues);

        ValidateClassification(
            source.Upgrades.Select(x => x.Id),
            mappings.Upgrades.Select(x => x.SourceId),
            mappings.UpgradeSourceAlternates.Select(x => x.SourceId),
            mappings.UpgradeDispositions.Select(x => x.SourceId),
            "Upgrade",
            issues);
    }

    private static void ValidateClassification(IEnumerable<string> sourceIds, IEnumerable<string> canonical, IEnumerable<string> alternates, IEnumerable<string> dispositions, string sourceType, List<ConversionIssue> issues)
    {
        var all = canonical.Concat(alternates).Concat(dispositions).ToList();
        foreach (var group in all.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            issues.Add(new ConversionIssue { Severity = "Error", Category = "Mapping", Code = $"Duplicate{sourceType}SourceClassification", SourceType = sourceType, SourceId = group.Key, Message = $"{sourceType} source appears in more than one mapping/disposition category." });

        var known = sourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in dispositions.Where(x => !known.Contains(x)))
            issues.Add(new ConversionIssue { Severity = "Error", Category = "Mapping", Code = $"Unknown{sourceType}DispositionSource", SourceType = sourceType, SourceId = id, Message = $"{sourceType} disposition references a missing source record." });
    }

    private static void AddInformation<T>(IEnumerable<T> coverage, string sourceType, List<ConversionIssue> issues)
    {
        foreach (var item in coverage)
        {
            string status;
            string sourceId;
            string sourceName;
            string notes;

            if (item is PilotMappingCoverageEntry pilot)
            {
                status = pilot.Status; sourceId = pilot.SourceId; sourceName = pilot.SourceName; notes = pilot.Notes;
            }
            else if (item is UpgradeMappingCoverageEntry upgrade)
            {
                status = upgrade.Status; sourceId = upgrade.SourceId; sourceName = upgrade.SourceName; notes = upgrade.Notes;
            }
            else continue;

            if (status is "ConvertedCanonical" or "AlternatePrinting") continue;
            issues.Add(new ConversionIssue { Severity = status == "Unmapped" ? "Warning" : "Information", Category = sourceType, Code = status, SourceType = sourceType, SourceId = sourceId, SourceName = sourceName, Message = notes });
        }
    }

    private static ConversionResult Empty(global::UnifiedToolkit.Repository.Repository source, IReadOnlyList<ShipMappingCoverageEntry> ships, IReadOnlyList<PilotMappingCoverageEntry> pilots, IReadOnlyList<UpgradeMappingCoverageEntry> upgrades, List<ConversionIssue> issues) => new()
    {
        Repository = new FirstEditionRepository(Array.Empty<FirstEditionShip>()),
        Issues = issues,
        ShipCoverage = ships,
        PilotCoverage = pilots,
        UpgradeCoverage = upgrades,
        SourceShipCount = source.Ships.Count,
        ExcludedShipCount = ships.Count(x => x.Status == "Excluded"),
        DeferredShipCount = ships.Count(x => x.Status == "Deferred" || x.Status.StartsWith("Planned", StringComparison.OrdinalIgnoreCase)),
        UnmappedShipCount = ships.Count(x => x.Status == "Unmapped")
    };

    private static ConversionIssue Error(string category, string code, string sourceType, string message) => new() { Severity = "Error", Category = category, Code = code, SourceType = sourceType, Message = message };
    private static bool IsError(ConversionIssue issue) => issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase);
}
