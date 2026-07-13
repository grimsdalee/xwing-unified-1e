namespace UnifiedToolkit.Conversion;

public sealed class ConversionProfile
{
    public string Id { get; init; } = "first-edition";
    public string Name { get; init; } = "Unified First Edition";
    public ConversionPolicy UnmappedShips { get; init; } = ConversionPolicy.WarningAndSkip;
    public bool AllowSourceValidationErrors { get; init; }
}
