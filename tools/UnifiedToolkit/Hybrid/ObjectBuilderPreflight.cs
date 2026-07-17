namespace UnifiedToolkit.Hybrid;

public sealed class ObjectBuilderPreflightDocument
{
    public string SchemaVersion { get; init; } = "1.0";
    public string GeneratedUtc { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    public ObjectBuilderPreflightSummary Summary { get; init; } = new();
    public IReadOnlyList<ObjectBuilderShipPreflight> Ships { get; init; } = Array.Empty<ObjectBuilderShipPreflight>();
}

public sealed class ObjectBuilderPreflightSummary
{
    public int ShipCount { get; init; }
    public int ConstructionRecipeCount { get; init; }
    public int ShipsWithAppearance { get; init; }
    public int ShipsReadyForObjectBuilder { get; init; }
    public int ShipsBlockedByAppearance { get; init; }
    public int ShipsBlockedByRecipe { get; init; }
    public bool T65XWingReady { get; init; }
    public bool Arc170Ready { get; init; }
}

public sealed class ObjectBuilderShipPreflight
{
    public string ShipId { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string FirstEditionBaseSize { get; init; } = "";
    public string Source25BaseSize { get; init; } = "";
    public bool MediumRemoved { get; init; }
    public bool HasConstructionRecipe { get; init; }
    public bool HasAppearance { get; init; }
    public int AppearanceCount { get; init; }
    public IReadOnlyList<string> AppearanceNames { get; init; } = Array.Empty<string>();
    public bool ReadyForObjectBuilder { get; init; }
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();
}

public static class ObjectBuilderPreflightBuilder
{
    public static ObjectBuilderPreflightDocument Build(HybridShipDefinitionDocument hybrid, ShipSpawnerRecipeAnalysis recipe)
    {
        var recipeSizes = recipe.FirstEditionRecipes.Select(x => x.BaseSize.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ships = hybrid.Ships.Select(ship =>
        {
            var blockers = new List<string>();
            var hasRecipe = recipeSizes.Contains(ship.BaseDefinition.Size.ToString());
            if (!hasRecipe) blockers.Add($"No First Edition {ship.BaseDefinition.Size} construction recipe was extracted.");
            if (ship.AppearanceVariants.Count == 0) blockers.Add("No confirmed ship-model appearance is assigned.");

            return new ObjectBuilderShipPreflight
            {
                ShipId = ship.SemanticData.Id,
                ShipName = ship.SemanticData.Name,
                FirstEditionBaseSize = ship.BaseDefinition.Size.ToString(),
                Source25BaseSize = ship.BaseSizeConversion?.Source25BaseSize ?? "",
                MediumRemoved = ship.BaseSizeConversion?.MediumRemoved ?? false,
                HasConstructionRecipe = hasRecipe,
                HasAppearance = ship.AppearanceVariants.Count > 0,
                AppearanceCount = ship.AppearanceVariants.Count,
                AppearanceNames = ship.AppearanceVariants.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
                ReadyForObjectBuilder = hasRecipe && ship.AppearanceVariants.Count > 0,
                Blockers = blockers
            };
        }).OrderBy(x => x.ShipName, StringComparer.OrdinalIgnoreCase).ToArray();

        bool Ready(string id) => ships.FirstOrDefault(x => x.ShipId.Equals(id, StringComparison.OrdinalIgnoreCase))?.ReadyForObjectBuilder == true;
        return new ObjectBuilderPreflightDocument
        {
            Ships = ships,
            Summary = new ObjectBuilderPreflightSummary
            {
                ShipCount = ships.Length,
                ConstructionRecipeCount = recipe.FirstEditionRecipes.Count,
                ShipsWithAppearance = ships.Count(x => x.HasAppearance),
                ShipsReadyForObjectBuilder = ships.Count(x => x.ReadyForObjectBuilder),
                ShipsBlockedByAppearance = ships.Count(x => !x.HasAppearance),
                ShipsBlockedByRecipe = ships.Count(x => !x.HasConstructionRecipe),
                T65XWingReady = Ready("xwing"),
                Arc170Ready = Ready("arc170")
            }
        };
    }
}
