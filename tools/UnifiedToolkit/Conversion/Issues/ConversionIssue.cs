namespace UnifiedToolkit.Conversion.Issues;

public sealed class ConversionIssue
{
    public string Severity { get; init; } = "";
    public string Category { get; init; } = "";
    public string Code { get; init; } = "";
    public string SourceType { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string Message { get; init; } = "";
}
