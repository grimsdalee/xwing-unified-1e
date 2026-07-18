namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class ShipIdentityProfile
{
    public IReadOnlyList<string> StrongAliases { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WeakAliases { get; init; } = Array.Empty<string>();
}
