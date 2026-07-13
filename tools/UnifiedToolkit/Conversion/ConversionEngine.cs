using UnifiedToolkit.Conversion.Converters;
using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.FirstEdition.Validation;
using UnifiedToolkit.Conversion.Issues;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Conversion;

public static class ConversionEngine
{
    public static ConversionResult ConvertShips(
        global::UnifiedToolkit.Repository.Repository sourceRepository,
        ConversionMappingSet mappings,
        ConversionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(sourceRepository);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(profile);

        var issues = new List<ConversionIssue>();
        var coverage = ShipMappingCoverageBuilder.Build(sourceRepository.Ships, mappings);
        issues.AddRange(ConversionMappingValidator.Validate(mappings));

        if (issues.Any(IsError))
        {
            return new ConversionResult
            {
                Repository = new FirstEditionRepository(Array.Empty<FirstEditionShip>()),
                Issues = issues,
                ShipCoverage = coverage,
                SourceShipCount = sourceRepository.Ships.Count,
                ExcludedShipCount = coverage.Count(x => x.Status == "Excluded"),
                UnmappedShipCount = coverage.Count(x => x.Status == "Unmapped")
            };
        }

        var conversion = new ShipConverter(mappings, profile).Convert(sourceRepository.Ships);
        issues.AddRange(conversion.Issues);

        FirstEditionRepository repository;
        try
        {
            repository = new FirstEditionRepository(conversion.Ships);
        }
        catch (InvalidDataException exception)
        {
            issues.Add(new ConversionIssue
            {
                Severity = "Error",
                Category = "TargetValidation",
                Code = "FirstEditionRepositoryBuildFailed",
                SourceType = "Repository",
                Message = exception.Message
            });
            repository = new FirstEditionRepository(Array.Empty<FirstEditionShip>());
        }

        issues.AddRange(FirstEditionRepositoryValidator.Validate(repository));

        return new ConversionResult
        {
            Repository = repository,
            Issues = issues,
            ShipCoverage = coverage,
            SourceShipCount = sourceRepository.Ships.Count,
            ExcludedShipCount = conversion.ExcludedCount,
            UnmappedShipCount = coverage.Count(x => x.Status == "Unmapped")
        };
    }

    private static bool IsError(ConversionIssue issue) =>
        issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase);
}
