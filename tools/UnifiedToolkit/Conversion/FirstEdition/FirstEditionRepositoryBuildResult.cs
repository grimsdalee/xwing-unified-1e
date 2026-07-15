using UnifiedToolkit.Conversion.Issues;

namespace UnifiedToolkit.Conversion.FirstEdition;

public sealed class FirstEditionRepositoryBuildResult
{
    public required FirstEditionRepository Repository { get; init; }
    public required string MappingVersion { get; init; }
    public IReadOnlyList<ConversionIssue> Issues { get; init; } = Array.Empty<ConversionIssue>();
    public int SourceValidationErrorCount { get; init; }
}
