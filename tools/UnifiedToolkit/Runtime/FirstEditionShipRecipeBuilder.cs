using System.Text.Json.Nodes;

namespace UnifiedToolkit.Runtime;

public static class FirstEditionShipRecipeBuilder
{
    private static readonly string[] RequiredFunctions =
    {
        "spawnPilotCardAndDial",
        "spawnCompositeShipBase",
        "pegAndShipSpawnFunction",
        "shipIdCustomObjectForSize",
        "spawnShipIdentifiersAndConfig",
        "spawnPilotShipBundle"
    };

    public static FirstEditionShipRecipeDocument Build(
        string hybridDefinitionsPath,
        string constructionRecipesPath,
        string targetShip)
    {
        var hybridRoot = JsonNode.Parse(File.ReadAllText(hybridDefinitionsPath))?.AsObject()
            ?? throw new InvalidOperationException("The hybrid ship definitions file could not be parsed.");
        var constructionRoot = JsonNode.Parse(File.ReadAllText(constructionRecipesPath))?.AsObject()
            ?? throw new InvalidOperationException("The construction recipe report could not be parsed.");

        var document = new FirstEditionShipRecipeDocument
        {
            HybridDefinitionsPath = Path.GetFullPath(hybridDefinitionsPath),
            ConstructionRecipesPath = Path.GetFullPath(constructionRecipesPath),
            TargetShipId = targetShip
        };

        var ship = FindShip(hybridRoot["ships"] as JsonArray, targetShip);
        if (ship is null)
        {
            document.Findings.Add($"No hybrid ship matched '{targetShip}'.");
            return document;
        }

        var semantic = ship["semanticData"] as JsonObject ?? new JsonObject();
        var baseDefinition = ship["baseDefinition"] as JsonObject ?? new JsonObject();
        var conversion = ship["baseSizeConversion"] as JsonObject;
        var recipe = new FirstEditionShipRecipe
        {
            ShipId = Text(semantic, "id"),
            ShipName = Text(semantic, "name"),
            Factions = Strings(semantic["factions"] as JsonArray),
            FirstEditionBaseSize = ReadBaseSize(baseDefinition, semantic),
            Source25BaseSize = conversion is null ? "" : Text(conversion, "source25BaseSize"),
            BaseConversionRequired = conversion is not null && Bool(conversion, "conversionRequired"),
            MediumRemoved = conversion is not null && Bool(conversion, "mediumRemoved"),
            RequiredRuntimeFunctions = RequiredFunctions.ToList()
        };

        document.TargetShipId = recipe.ShipId;
        document.TargetShipName = recipe.ShipName;

        foreach (var node in ship["pilots"] as JsonArray ?? new JsonArray())
        {
            if (node is not JsonObject pilot) continue;
            recipe.Pilots.Add(new FirstEditionRecipePilot
            {
                Id = Text(pilot, "id"),
                Name = Text(pilot, "name"),
                Faction = Text(pilot, "faction"),
                PilotSkill = Int(pilot, "pilotSkill"),
                SquadPointCost = Int(pilot, "squadPointCost"),
                Unique = Bool(pilot, "unique")
            });
        }

        var selectedPilot = recipe.Pilots
            .OrderBy(x => x.Unique)
            .ThenBy(x => x.SquadPointCost)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (selectedPilot is not null)
        {
            recipe.SelectedPilotId = selectedPilot.Id;
            recipe.SelectedPilotName = selectedPilot.Name;
        }

        foreach (var node in ship["appearanceVariants"] as JsonArray ?? new JsonArray())
        {
            if (node is not JsonObject appearance) continue;
            var item = new FirstEditionRecipeAppearance
            {
                VariantId = Text(appearance, "variantId"),
                DisplayName = Text(appearance, "displayName"),
                SourceGuid = Text(appearance, "sourceGuid"),
                SourcePath = Text(appearance, "sourcePath"),
                MeshUrl = Text(appearance, "meshUrl"),
                DiffuseUrl = Text(appearance, "diffuseUrl"),
                NormalUrl = Text(appearance, "normalUrl"),
                ColliderUrl = Text(appearance, "colliderUrl")
            };
            item.HasMesh = !string.IsNullOrWhiteSpace(item.MeshUrl);
            item.HasDiffuse = !string.IsNullOrWhiteSpace(item.DiffuseUrl);
            recipe.AppearanceVariants.Add(item);
        }

        var selectedPilotKey = Normalize(recipe.SelectedPilotName);
        recipe.SelectedAppearance = recipe.AppearanceVariants
            .Where(x => x.HasMesh && x.HasDiffuse)
            .OrderByDescending(x => AppearanceMatchesPilot(x, selectedPilotKey))
            .ThenByDescending(x => Normalize(x.DisplayName).Contains("xwing", StringComparison.Ordinal))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var editionAssets = ship["editionAssets"] as JsonObject;
        if (editionAssets is not null)
        {
            recipe.EditionAssets.Dials = FilterEditionAssets(ReadAssets(editionAssets["dials"] as JsonArray), semantic, conversion);
            recipe.EditionAssets.ShipReferences = FilterEditionAssets(ReadAssets(editionAssets["shipReferences"] as JsonArray), semantic, conversion);
            recipe.EditionAssets.PhysicalBaseTokens = FilterEditionAssets(ReadAssets(editionAssets["physicalBaseTokens"] as JsonArray), semantic, conversion);
            recipe.EditionAssets.Cards = FilterEditionAssets(ReadAssets(editionAssets["cards"] as JsonArray), semantic, conversion);
        }

        BuildRuntimeParameters(recipe);
        Validate(recipe, constructionRoot);

        document.Recipe = recipe;
        document.Summary = new FirstEditionShipRecipeSummary
        {
            ShipFound = true,
            ValidFirstEditionBase = IsValidBase(recipe.FirstEditionBaseSize),
            MediumBaseRejected = !recipe.FirstEditionBaseSize.Equals("Medium", StringComparison.OrdinalIgnoreCase),
            PilotCount = recipe.Pilots.Count,
            AppearanceVariantCount = recipe.AppearanceVariants.Count,
            DialAssetCount = recipe.EditionAssets.Dials.Count,
            ShipReferenceCount = recipe.EditionAssets.ShipReferences.Count,
            PhysicalBaseTokenCount = recipe.EditionAssets.PhysicalBaseTokens.Count,
            RuntimeRecipeAvailable = RequiredFunctions.All(x => FunctionFound(constructionRoot, x)),
            ReadyForReview = recipe.ValidationErrors.Count == 0
        };

        document.Findings.Add($"Selected {recipe.SelectedPilotName} as the deterministic review pilot.");
        document.Findings.Add(recipe.SelectedAppearance is null
            ? "No appearance variant could be selected."
            : $"Selected appearance '{recipe.SelectedAppearance.DisplayName}'.");
        document.Findings.Add("The output is a review recipe only; no TTS object is generated.");
        return document;
    }

    private static void BuildRuntimeParameters(FirstEditionShipRecipe recipe)
    {
        var size = recipe.FirstEditionBaseSize.Trim().ToLowerInvariant();
        var runtimeSize = size is "epic" or "huge" ? "huge" : size;
        recipe.RuntimeParameters.RuntimeSize = runtimeSize;
        recipe.RuntimeParameters.DefaultPegType = size switch
        {
            "small" => "small",
            "large" => "large",
            "epic" or "huge" => "huge",
            _ => ""
        };
        recipe.RuntimeParameters.BaseMeshPath = string.IsNullOrWhiteSpace(runtimeSize)
            ? ""
            : $"assets/ships-v2/bases/{runtimeSize}/base.obj";
        recipe.RuntimeParameters.BaseTexturePattern = string.IsNullOrWhiteSpace(runtimeSize)
            ? ""
            : $"assets/ships-v2/bases/{runtimeSize}/{{arc}}/{{faction}}.png";
        recipe.RuntimeParameters.PegMeshPath = string.IsNullOrWhiteSpace(recipe.RuntimeParameters.DefaultPegType)
            ? ""
            : $"assets/ships-v2/bases/pegs/{recipe.RuntimeParameters.DefaultPegType}.obj";

        if (recipe.SelectedAppearance is null) return;
        recipe.RuntimeParameters.ShipMeshUrl = recipe.SelectedAppearance.MeshUrl;
        recipe.RuntimeParameters.ShipDiffuseUrl = recipe.SelectedAppearance.DiffuseUrl;
        recipe.RuntimeParameters.ShipNormalUrl = recipe.SelectedAppearance.NormalUrl;
        recipe.RuntimeParameters.ShipColliderUrl = recipe.SelectedAppearance.ColliderUrl;
    }

    private static void Validate(FirstEditionShipRecipe recipe, JsonObject constructionRoot)
    {
        if (!IsValidBase(recipe.FirstEditionBaseSize))
            recipe.ValidationErrors.Add($"Invalid First Edition base size '{recipe.FirstEditionBaseSize}'. Only Small, Large, and Epic are allowed.");
        if (recipe.FirstEditionBaseSize.Equals("Medium", StringComparison.OrdinalIgnoreCase))
            recipe.ValidationErrors.Add("Medium bases are forbidden in First Edition output.");
        if (recipe.Pilots.Count == 0)
            recipe.ValidationErrors.Add("No First Edition pilots are available for the target ship.");
        if (recipe.SelectedAppearance is null)
            recipe.ValidationErrors.Add("No appearance variant is available.");
        else
        {
            if (!recipe.SelectedAppearance.HasMesh)
                recipe.ValidationErrors.Add("The selected appearance has no mesh URL.");
            if (!recipe.SelectedAppearance.HasDiffuse)
                recipe.ValidationErrors.Add("The selected appearance has no diffuse texture URL.");
        }
        foreach (var function in RequiredFunctions.Where(x => !FunctionFound(constructionRoot, x)))
            recipe.ValidationErrors.Add($"Required runtime function '{function}' was not found in the Phase 5D report.");

        recipe.ReviewNotes.Add("Confirm the selected colour variant visually before object generation.");
        recipe.ReviewNotes.Add("Confirm the First Edition pilot token and dial source objects before spawning.");
        recipe.ReviewNotes.Add("The runtime must reject Medium rather than silently creating a 2.5 base.");
    }


    private static string ReadBaseSize(JsonObject baseDefinition, JsonObject semantic)
    {
        var sizeNode = baseDefinition["size"];
        if (sizeNode is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                var normalizedText = NormalizeBaseSize(text);
                if (!string.IsNullOrWhiteSpace(normalizedText)) return normalizedText;
            }

            if (TryReadInteger(value, out var numeric))
            {
                var mapped = numeric switch
                {
                    0 => "Small",
                    1 => "Large",
                    2 => "Epic",
                    _ => ""
                };
                if (!string.IsNullOrWhiteSpace(mapped)) return mapped;
            }
        }

        var displayName = Text(baseDefinition, "displayName");
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var normalizedDisplay = NormalizeBaseSize(displayName);
            if (!string.IsNullOrWhiteSpace(normalizedDisplay)) return normalizedDisplay;
        }

        return NormalizeBaseSize(Text(semantic, "size"));
    }

    private static bool TryReadInteger(JsonValue value, out int result)
    {
        if (value.TryGetValue<int>(out result)) return true;
        if (value.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue)
        {
            result = (int)longValue;
            return true;
        }
        if (value.TryGetValue<double>(out var doubleValue) && Math.Abs(doubleValue % 1) < double.Epsilon && doubleValue is >= int.MinValue and <= int.MaxValue)
        {
            result = (int)doubleValue;
            return true;
        }
        result = 0;
        return false;
    }

    private static string NormalizeBaseSize(string value)
    {
        var normalized = Normalize(value);
        if (normalized.Contains("small", StringComparison.Ordinal)) return "Small";
        if (normalized.Contains("large", StringComparison.Ordinal)) return "Large";
        if (normalized.Contains("epic", StringComparison.Ordinal) || normalized.Contains("huge", StringComparison.Ordinal)) return "Epic";
        return "";
    }

    private static bool AppearanceMatchesPilot(FirstEditionRecipeAppearance appearance, string selectedPilotKey)
    {
        if (string.IsNullOrWhiteSpace(selectedPilotKey)) return false;
        var display = Normalize(appearance.DisplayName);
        var path = Normalize(appearance.SourcePath);
        return display == selectedPilotKey || display.Contains(selectedPilotKey, StringComparison.Ordinal) || path.Contains(selectedPilotKey, StringComparison.Ordinal);
    }

    private static List<FirstEditionRecipeAsset> FilterEditionAssets(
        List<FirstEditionRecipeAsset> assets,
        JsonObject semantic,
        JsonObject? conversion)
    {
        var sourceId = conversion is null ? "" : Text(conversion, "source25ShipId");
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            var provenance = semantic["provenance"] as JsonObject;
            sourceId = provenance is null ? "" : Text(provenance, "sourceId");
        }

        var targetVariant = ExtractVariantNumber(sourceId);
        if (string.IsNullOrWhiteSpace(targetVariant)) return assets;

        return assets.Where(asset =>
        {
            var searchable = asset.SourceName + " " + asset.SourcePath;
            var assetVariant = ExtractVariantNumber(searchable);
            if (!string.IsNullOrWhiteSpace(assetVariant))
                return assetVariant.Equals(targetVariant, StringComparison.OrdinalIgnoreCase);

            // Assets named only "X-Wing" belong to the T-65 set. T-70-labelled assets must never leak into it.
            var normalizedAsset = Normalize(searchable);
            if (targetVariant == "65" && normalizedAsset.Contains("t70", StringComparison.Ordinal)) return false;
            if (targetVariant == "70" && normalizedAsset.Contains("t65", StringComparison.Ordinal)) return false;
            return true;
        }).ToList();
    }

    private static string ExtractVariantNumber(string value)
    {
        var normalized = Normalize(value);
        for (var i = 0; i < normalized.Length - 1; i++)
        {
            if (normalized[i] != 't' || !char.IsDigit(normalized[i + 1])) continue;
            var end = i + 1;
            while (end < normalized.Length && char.IsDigit(normalized[end])) end++;
            return normalized[(i + 1)..end];
        }
        return "";
    }

    private static JsonObject? FindShip(JsonArray? ships, string target)
    {
        if (ships is null) return null;
        var normalized = Normalize(target);
        return ships.OfType<JsonObject>().FirstOrDefault(ship =>
        {
            var semantic = ship["semanticData"] as JsonObject;
            if (semantic is null) return false;
            return Normalize(Text(semantic, "id")) == normalized ||
                   Normalize(Text(semantic, "name")) == normalized ||
                   (normalized.Contains("t65") && Normalize(Text(semantic, "name")).Contains("t65xwing"));
        });
    }

    private static bool FunctionFound(JsonObject root, string name)
    {
        var functions = root["functions"] as JsonArray;
        return functions?.OfType<JsonObject>().Any(x =>
            Text(x, "name").Equals(name, StringComparison.Ordinal) && Bool(x, "found")) == true;
    }

    private static List<FirstEditionRecipeAsset> ReadAssets(JsonArray? array)
    {
        if (array is null) return new();
        return array.OfType<JsonObject>().Select(x => new FirstEditionRecipeAsset
        {
            AssetId = Text(x, "assetId"),
            SourceGuid = Text(x, "sourceGuid"),
            SourceName = Text(x, "sourceName"),
            SourcePath = Text(x, "sourcePath"),
            FactionHint = Text(x, "factionHint"),
            MatchScore = Int(x, "matchScore")
        }).ToList();
    }

    private static bool IsValidBase(string value) =>
        value.Equals("Small", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Large", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Epic", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Huge", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Text(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? "" : "";
    private static bool Bool(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<bool>(out var result) && result;
    private static int Int(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<int>(out var result) ? result : 0;
    private static List<string> Strings(JsonArray? values) =>
        values?.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList() ?? new();
}
