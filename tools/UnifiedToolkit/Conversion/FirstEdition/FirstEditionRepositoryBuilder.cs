using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Conversion.FirstEdition;

public static class FirstEditionRepositoryBuilder
{
    public static FirstEditionRepositoryBuildResult Build(
        string repositoryFolder,
        string mappingFolder,
        bool allowSourceErrors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingFolder);

        var source = RepositoryLoader.Load(Path.GetFullPath(repositoryFolder));
        var sourceErrors = RepositoryValidator.Validate(source)
            .Count(x => x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));

        if (sourceErrors > 0 && !allowSourceErrors)
        {
            throw new InvalidOperationException(
                $"The source repository contains {sourceErrors} validation error(s). " +
                "Re-run with --allow-source-errors to build the First Edition repository diagnostically.");
        }

        var mappings = ConversionMappingLoader.Load(Path.GetFullPath(mappingFolder));
        var result = ConversionEngine.ConvertRepository(
            source,
            mappings,
            new ConversionProfile { AllowSourceValidationErrors = allowSourceErrors });

        return new FirstEditionRepositoryBuildResult
        {
            Repository = result.Repository,
            MappingVersion = mappings.Version,
            Issues = result.Issues,
            SourceValidationErrorCount = sourceErrors
        };
    }
}
