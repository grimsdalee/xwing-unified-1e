namespace UnifiedToolkit.Assets;

public sealed class AssetRoleRequirement
{
    public AssetEntityKey Entity { get; init; } = new();
    public string EntityName { get; init; } = "";
    public AssetRole Role { get; init; }
    public bool Required { get; init; } = true;
    public string ChassisId { get; init; } = "";
    public string ShipSize { get; init; } = "";
}
