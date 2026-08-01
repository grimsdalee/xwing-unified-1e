namespace UnifiedToolkit.RepositoryMaintenance;

/// <summary>
/// Central terminology mapping between Unified 2.5 ship-size names and
/// First Edition names. Unified 2.5 uses "Huge" for the source asset folder;
/// First Edition reports and destination paths use "Epic".
/// </summary>
public static class FirstEditionShipSizeTerminology
{
    public const string Unified25HugeFolder = "huge";
    public const string FirstEditionEpic = "epic";

    public static string ToFirstEditionTerm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var normalised = value.Trim().ToLowerInvariant();
        return normalised switch
        {
            "huge" => FirstEditionEpic,
            "epic" => FirstEditionEpic,
            "small" => "small",
            "medium" => "medium",
            "large" => "large",
            _ => normalised
        };
    }
}
