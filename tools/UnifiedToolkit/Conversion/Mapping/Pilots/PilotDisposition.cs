namespace UnifiedToolkit.Conversion.Mapping.Pilots;

public sealed class PilotDisposition
{
    public string SourceId { get; init; } = "";
    public PilotDispositionKind Kind { get; init; }
    public string Reason { get; init; } = "";
}
