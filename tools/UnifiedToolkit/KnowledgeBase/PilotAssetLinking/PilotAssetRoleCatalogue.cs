namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed record PilotAssetRoleDefinition(string Name, bool Required);

public sealed class PilotAssetRoleCatalogue
{
    private static readonly IReadOnlyList<PilotAssetRoleDefinition> Roles = new[]
    {
        new PilotAssetRoleDefinition("PilotCard", true),
        new PilotAssetRoleDefinition("PilotBaseTokenSheet", false),
        new PilotAssetRoleDefinition("PilotBaseToken", true)
    };

    public IReadOnlyList<PilotAssetRoleDefinition> GetAll() => Roles;
}
