namespace UnifiedToolkit.Repository;

public sealed class RepositoryValidationIssue
{
    public string Severity { get; init; } = "";

    public string Category { get; init; } = "";

    public string Code { get; init; } = "";

    public string EntityType { get; init; } = "";

    public string EntityId { get; init; } = "";

    public string EntityName { get; init; } = "";

    public string FieldName { get; init; } = "";

    public string Message { get; init; } = "";
}