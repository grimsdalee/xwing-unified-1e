using System.Text.Json.Nodes;
using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.FirstEdition.Pilots;
using UnifiedToolkit.Models;
using UnifiedToolkit.TTS;

namespace UnifiedToolkit.Hybrid;

public sealed class LegacyShipAssetCatalogue
{
    public string SchemaVersion { get; init; } = "1.2";
    public IReadOnlyList<LegacyShipFamily> ShipFamilies { get; init; } = Array.Empty<LegacyShipFamily>();
    public IReadOnlyList<LegacyModelAsset> ModelObjects { get; init; } = Array.Empty<LegacyModelAsset>();
    public IReadOnlyList<LegacyEditionAsset> Dials { get; init; } = Array.Empty<LegacyEditionAsset>();
    public IReadOnlyList<LegacyEditionAsset> ShipReferences { get; init; } = Array.Empty<LegacyEditionAsset>();
    public IReadOnlyList<LegacyEditionAsset> PhysicalBaseTokens { get; init; } = Array.Empty<LegacyEditionAsset>();
    public IReadOnlyList<LegacyEditionAsset> Cards { get; init; } = Array.Empty<LegacyEditionAsset>();
    public IReadOnlyList<LegacyIgnoredObject> IgnoredObjects { get; init; } = Array.Empty<LegacyIgnoredObject>();
}

public sealed class LegacyShipFamily
{
    public string FamilyId { get; init; } = "";
    public string CanonicalKey { get; init; } = "";
    public string SemanticShipIdHint { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string FactionHint { get; init; } = "";
    public string CollectionPath { get; init; } = "";
    public string DiscoveryMethod { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    public IReadOnlyList<LegacyAppearance> Appearances { get; init; } = Array.Empty<LegacyAppearance>();
}

public sealed class LegacyAppearance
{
    public string AppearanceId { get; init; } = "";
    public string Signature { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SourceGuid { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string MeshUrl { get; init; } = "";
    public string DiffuseUrl { get; init; } = "";
    public string NormalUrl { get; init; } = "";
    public string ColliderUrl { get; init; } = "";
    public IReadOnlyList<string> ProvenanceNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ProvenanceGuids { get; init; } = Array.Empty<string>();
    public string TemplateJson { get; init; } = "";
}

public sealed class LegacyModelAsset
{
    public string AssetId { get; init; } = "";
    public string FamilyId { get; init; } = "";
    public string FamilyName { get; init; } = "";
    public string SemanticShipIdHint { get; init; } = "";
    public string FactionHint { get; init; } = "";
    public string DiscoveryMethod { get; init; } = "";
    public string AppearanceSignature { get; init; } = "";
    public string SourceGuid { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string MeshUrl { get; init; } = "";
    public string DiffuseUrl { get; init; } = "";
    public string NormalUrl { get; init; } = "";
    public string ColliderUrl { get; init; } = "";
    public string TemplateJson { get; init; } = "";
}

public sealed class LegacyEditionAsset
{
    public string AssetId { get; init; } = "";
    public string Kind { get; init; } = "";
    public string SourceGuid { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string FactionHint { get; init; } = "";
    public IReadOnlyList<string> ContextAliases { get; init; } = Array.Empty<string>();
    public string TemplateJson { get; init; } = "";
}

public sealed class LegacyIgnoredObject
{
    public string SourceGuid { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string Reason { get; init; } = "";
}

public static class LegacyShipAssetCatalogueBuilder
{
    public static LegacyShipAssetCatalogue Build(string legacySavePath, FirstEditionRepository repository)
    {
        var game = TtsSaveLoader.Load(legacySavePath);
        var pilotIndex = BuildPilotIndex(repository.Pilots);
        var modelObjects = new List<LegacyModelAsset>();
        var dials = new List<LegacyEditionAsset>();
        var references = new List<LegacyEditionAsset>();
        var physicalTokens = new List<LegacyEditionAsset>();
        var cards = new List<LegacyEditionAsset>();
        var ignored = new List<LegacyIgnoredObject>();

        foreach (var obj in game.AllObjects())
        {
            var displayName = FirstNonEmpty(obj.Nickname, obj.Description, obj.Name, obj.Guid);
            var path = SpawnerFrameworkAnalyser.ObjectPath(obj);
            var directContext = HybridText.NormalizeWords(displayName, obj.Description, obj.Name);

            if (TryCreateModelAsset(obj, displayName, path, directContext, pilotIndex, repository, out var model, out var modelFailure))
            {
                modelObjects.Add(model!);
                continue;
            }

            if (IsDialOwner(obj, directContext))
            {
                dials.Add(CreateEditionAsset(obj, displayName, path, "dial"));
                continue;
            }

            if (IsShipReference(obj, directContext))
            {
                references.Add(CreateEditionAsset(obj, displayName, path, "ship-reference"));
                continue;
            }

            if (IsPhysicalBaseToken(obj, directContext))
            {
                physicalTokens.Add(CreateEditionAsset(obj, displayName, path, "physical-base-token"));
                continue;
            }

            if (IsRelevantCardOwner(obj, directContext))
            {
                cards.Add(CreateEditionAsset(obj, displayName, path, "card"));
                continue;
            }

            if (obj.IsModel && obj.Json["CustomMesh"] is JsonObject)
            {
                ignored.Add(new LegacyIgnoredObject
                {
                    SourceGuid = obj.Guid,
                    DisplayName = displayName,
                    SourcePath = path,
                    Reason = modelFailure ?? "Custom model is not a confirmed ship appearance."
                });
            }
        }

        var families = BuildFamilies(modelObjects);
        return new LegacyShipAssetCatalogue
        {
            ShipFamilies = families,
            ModelObjects = modelObjects.OrderBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            Dials = Deduplicate(dials),
            ShipReferences = Deduplicate(references),
            PhysicalBaseTokens = Deduplicate(physicalTokens),
            Cards = Deduplicate(cards),
            IgnoredObjects = ignored.OrderBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static bool TryCreateModelAsset(
        TtsObject obj,
        string displayName,
        string path,
        string directContext,
        IReadOnlyDictionary<string, IReadOnlyList<FirstEditionPilot>> pilotIndex,
        FirstEditionRepository repository,
        out LegacyModelAsset? asset,
        out string? failure)
    {
        asset = null;
        failure = null;
        if (!obj.IsModel || obj.Json["CustomMesh"] is not JsonObject mesh)
            return false;
        if (ContainsAny(directContext, "dial", "token", "base", "peg", "movement", "ruler", "obstacle", "reference", "refcard"))
        {
            failure = "Custom model has an edition-asset or utility role rather than a ship-appearance role.";
            return false;
        }

        var meshUrl = Get(mesh, "MeshURL");
        if (string.IsNullOrWhiteSpace(meshUrl))
        {
            failure = "Custom model does not have a mesh URL.";
            return false;
        }

        var collection = FindNamedShipCollection(obj);
        var familyName = "";
        var semanticShipId = "";
        var faction = "";
        var discovery = "";
        var collectionPath = "";

        if (collection is not null && !IsFlatShipModelsBag(collection))
        {
            familyName = HybridText.CleanCollectionName(FirstNonEmpty(collection.Nickname, collection.Description, collection.Name));
            faction = HybridText.DetectFaction(SpawnerFrameworkAnalyser.ObjectPath(collection));
            collectionPath = SpawnerFrameworkAnalyser.ObjectPath(collection);
            discovery = "collection-hierarchy";
        }
        else if (IsInsideFlatShipModelsBag(obj))
        {
            var pilot = ResolvePilot(displayName, pilotIndex);
            if (pilot is null)
            {
                failure = "Flat Ship Models Bag object could not be resolved to one unambiguous First Edition pilot.";
                return false;
            }

            var ship = repository.FindShip(pilot.ShipId);
            if (ship is null)
            {
                failure = $"Pilot '{pilot.Name}' resolves to unknown semantic ship '{pilot.ShipId}'.";
                return false;
            }

            semanticShipId = ship.Id;
            familyName = ship.Name;
            faction = HybridText.Normalize(pilot.Faction);
            collectionPath = SpawnerFrameworkAnalyser.ObjectPath(collection ?? obj.Parent ?? obj);
            discovery = "semantic-pilot";
        }
        else
        {
            failure = "Custom model is outside a confirmed ship-model collection.";
            return false;
        }

        var familyKey = !string.IsNullOrWhiteSpace(semanticShipId)
            ? HybridText.Normalize(semanticShipId)
            : HybridText.FamilyKey(familyName);
        var familyId = discovery == "semantic-pilot"
            ? SpawnerFrameworkAnalyser.StableId("legacy-family", "semantic", familyKey)
            : SpawnerFrameworkAnalyser.StableId("legacy-family", "collection", familyKey, faction, collectionPath);
        var diffuse = Get(mesh, "DiffuseURL");
        var normal = Get(mesh, "NormalURL");
        var collider = Get(mesh, "ColliderURL");
        var signature = SpawnerFrameworkAnalyser.StableId("appearance", meshUrl, diffuse, normal, collider, Get(mesh, "Type"), Get(mesh, "MaterialIndex"));

        asset = new LegacyModelAsset
        {
            AssetId = SpawnerFrameworkAnalyser.StableId("legacy-model", obj.Guid, signature),
            FamilyId = familyId,
            FamilyName = familyName,
            SemanticShipIdHint = semanticShipId,
            FactionHint = faction,
            DiscoveryMethod = discovery,
            AppearanceSignature = signature,
            SourceGuid = obj.Guid,
            DisplayName = displayName,
            SourcePath = path,
            MeshUrl = meshUrl,
            DiffuseUrl = diffuse,
            NormalUrl = normal,
            ColliderUrl = collider,
            TemplateJson = obj.Json.ToJsonString()
        };
        return true;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<FirstEditionPilot>> BuildPilotIndex(IEnumerable<FirstEditionPilot> pilots)
    {
        return pilots
            .SelectMany(pilot => PilotAliases(pilot).Select(alias => new { Alias = alias, Pilot = pilot }))
            .GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FirstEditionPilot>)group.Select(x => x.Pilot)
                    .DistinctBy(x => FirstEditionRepository.PilotIdentity(x.Id, x.ShipId, x.Faction))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static FirstEditionPilot? ResolvePilot(string modelName, IReadOnlyDictionary<string, IReadOnlyList<FirstEditionPilot>> index)
    {
        foreach (var alias in ModelNameAliases(modelName))
        {
            if (!index.TryGetValue(alias, out var matches)) continue;
            if (matches.Count == 1) return matches[0];
        }
        return null;
    }

    private static IEnumerable<string> PilotAliases(FirstEditionPilot pilot)
    {
        yield return HybridText.Normalize(pilot.Name);
        yield return HybridText.Normalize(pilot.Id);
    }

    private static IEnumerable<string> ModelNameAliases(string value)
    {
        var normalized = HybridText.Normalize(value);
        yield return normalized;
        foreach (var suffix in new[] { "v2", "alt", "alternate", "rebel", "imperial", "scum", "resistance", "firstorder" })
            if (normalized.EndsWith(suffix, StringComparison.Ordinal) && normalized.Length > suffix.Length)
                yield return normalized[..^suffix.Length];
    }

    private static LegacyShipFamily[] BuildFamilies(IEnumerable<LegacyModelAsset> models)
    {
        return models.GroupBy(x => x.FamilyId).Select(group =>
        {
            var first = group.First();
            var appearances = group.GroupBy(x => x.AppearanceSignature).Select(appearanceGroup =>
            {
                var source = appearanceGroup.First();
                return new LegacyAppearance
                {
                    AppearanceId = SpawnerFrameworkAnalyser.StableId("legacy-appearance", group.Key, appearanceGroup.Key),
                    Signature = appearanceGroup.Key,
                    DisplayName = SelectAppearanceName(appearanceGroup.Select(x => x.DisplayName)),
                    SourceGuid = source.SourceGuid,
                    SourcePath = source.SourcePath,
                    MeshUrl = source.MeshUrl,
                    DiffuseUrl = source.DiffuseUrl,
                    NormalUrl = source.NormalUrl,
                    ColliderUrl = source.ColliderUrl,
                    ProvenanceNames = appearanceGroup.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
                    ProvenanceGuids = appearanceGroup.Select(x => x.SourceGuid).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
                    TemplateJson = source.TemplateJson
                };
            }).OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

            return new LegacyShipFamily
            {
                FamilyId = group.Key,
                CanonicalKey = !string.IsNullOrWhiteSpace(first.SemanticShipIdHint) ? HybridText.Normalize(first.SemanticShipIdHint) : HybridText.FamilyKey(first.FamilyName),
                SemanticShipIdHint = first.SemanticShipIdHint,
                DisplayName = first.FamilyName,
                FactionHint = group.Select(x => x.FactionHint).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                    ? group.Select(x => x.FactionHint).First(x => !string.IsNullOrWhiteSpace(x))
                    : "",
                CollectionPath = ParentPath(first.SourcePath),
                DiscoveryMethod = first.DiscoveryMethod,
                Aliases = HybridText.FamilyAliases(first.FamilyName)
                    .Append(HybridText.Normalize(first.SemanticShipIdHint))
                    .Where(x => x.Length >= 2)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Appearances = appearances
            };
        }).OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.FactionHint).ToArray();
    }

    private static bool IsDialOwner(TtsObject obj, string directContext)
    {
        if (!ContainsAny(directContext, "dial", "maneuverdial", "manoeuvredial")) return false;
        if (HasAncestorWithDirectRole(obj, ancestor => ContainsAny(DirectContext(ancestor), "dial", "maneuverdial", "manoeuvredial"))) return false;
        return obj.IsBag || obj.IsDeck || obj.IsModel || obj.Children.Count > 0;
    }

    private static bool IsShipReference(TtsObject obj, string directContext)
    {
        if (!ContainsAny(directContext, "refcard", "referencecard", "shipreference")) return false;
        return !HasAncestorWithDirectRole(obj, ancestor => ContainsAny(DirectContext(ancestor), "refcard", "referencecard", "shipreference"));
    }

    private static bool IsPhysicalBaseToken(TtsObject obj, string directContext)
    {
        if (!ContainsAny(directContext, "shiptoken", "basetoken", "shipbasetoken", "pilottoken")) return false;
        if (ContainsAny(directContext, "targetlock", "focus", "evade", "stress", "critical", "objective")) return false;
        return !HasAncestorWithDirectRole(obj, ancestor => ContainsAny(DirectContext(ancestor), "shiptoken", "basetoken", "shipbasetoken", "pilottoken"));
    }

    private static bool IsRelevantCardOwner(TtsObject obj, string directContext)
    {
        if (!(obj.IsCard || obj.IsDeck)) return false;
        var fullPath = HybridText.Normalize(SpawnerFrameworkAnalyser.ObjectPath(obj));
        if (ContainsAny(fullPath, "damagedeck", "damagecards", "dial", "maneuver", "manoeuvre")) return false;
        if (obj.Parent is not null && obj.Parent.IsDeck) return false;
        return ContainsAny(directContext, "pilotcard", "shipcard", "upgradecard", "pilotdeck", "shipdeck", "upgradedeck")
            || ContainsAny(fullPath, "pilotcards", "shipcards", "upgradecards");
    }

    private static bool HasAncestorWithDirectRole(TtsObject obj, Func<TtsObject, bool> predicate)
    {
        for (var current = obj.Parent; current is not null; current = current.Parent)
            if (predicate(current)) return true;
        return false;
    }

    private static string DirectContext(TtsObject obj) => HybridText.NormalizeWords(obj.Nickname, obj.Description, obj.Name);

    private static TtsObject? FindNamedShipCollection(TtsObject obj)
    {
        for (var current = obj.Parent; current is not null; current = current.Parent)
        {
            var text = HybridText.NormalizeWords(current.Nickname, current.Description, current.Name);
            if ((text.Contains("models") || text.Contains("ship model")) && !ContainsAny(text, "dial", "token", "base", "peg", "obstacle", "accessor", "damage"))
                return current;
        }
        return null;
    }

    private static bool IsInsideFlatShipModelsBag(TtsObject obj)
    {
        for (var current = obj.Parent; current is not null; current = current.Parent)
            if (IsFlatShipModelsBag(current)) return true;
        return false;
    }

    private static bool IsFlatShipModelsBag(TtsObject obj)
    {
        var text = HybridText.NormalizeWords(obj.Nickname, obj.Description, obj.Name);
        return text.Contains("ship models bag") || text == "ship models";
    }

    private static LegacyEditionAsset CreateEditionAsset(TtsObject obj, string name, string path, string kind)
    {
        var context = HybridText.NormalizeWords(name, obj.Description, path);
        return new LegacyEditionAsset
        {
            AssetId = SpawnerFrameworkAnalyser.StableId("legacy-edition", kind, obj.Guid, name),
            Kind = kind,
            SourceGuid = obj.Guid,
            DisplayName = name,
            SourcePath = path,
            FactionHint = HybridText.DetectFaction(path),
            ContextAliases = HybridText.ContextAliases(context),
            TemplateJson = obj.Json.ToJsonString()
        };
    }

    private static LegacyEditionAsset[] Deduplicate(IEnumerable<LegacyEditionAsset> assets) =>
        assets.GroupBy(x => x.AssetId).Select(x => x.First()).OrderBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);
    private static string Get(JsonObject? obj, string property) => obj is null ? "" : TtsJsonHelpers.GetString(obj, property);
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    private static string ParentPath(string path) { var index = path.LastIndexOf(" / ", StringComparison.Ordinal); return index < 0 ? path : path[..index]; }
    private static string SelectAppearanceName(IEnumerable<string> names) => names.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x.Length).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? "Appearance";
}

internal static class HybridText
{
    public static string Normalize(string? value) => new((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    public static string NormalizeWords(params string?[] values) => string.Join(' ', values.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant().Replace('-', ' ').Replace('_', ' ').Replace('/', ' ');

    public static string CleanCollectionName(string value)
    {
        var result = value;
        foreach (var suffix in new[] { " Models Collection", " Model Collection", " Models Bag", " Models", " Model", " Collection" })
            if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) result = result[..^suffix.Length];
        return result.Trim();
    }

    public static string FamilyKey(string value)
    {
        var cleaned = CleanCollectionName(value);
        foreach (var faction in new[] { "rebel alliance", "galactic empire", "scum and villainy", "rebel", "imperial", "empire", "scum", "resistance", "first order" })
            cleaned = cleaned.Replace(faction, "", StringComparison.OrdinalIgnoreCase);
        return Normalize(cleaned);
    }

    public static IReadOnlyList<string> FamilyAliases(string value)
    {
        var cleaned = CleanCollectionName(value);
        return new[]
        {
            Normalize(cleaned), FamilyKey(cleaned),
            Normalize(cleaned.Replace("class", "", StringComparison.OrdinalIgnoreCase)),
            Normalize(cleaned.Replace("fighter", "", StringComparison.OrdinalIgnoreCase)),
            Normalize(cleaned.Replace("attack platform", "", StringComparison.OrdinalIgnoreCase))
        }.Where(x => x.Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> ContextAliases(string context) =>
        context.Split(new[] { ' ', '/', '\\', '-', '_', '.', ':', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).Where(x => x.Length >= 3).Distinct().ToArray();

    public static string DetectFaction(string value)
    {
        var text = Normalize(value);
        if (text.Contains("firstorder")) return "firstorder";
        if (text.Contains("resistance")) return "resistance";
        if (text.Contains("scum")) return "scumandvillainy";
        if (text.Contains("imperial") || text.Contains("empire")) return "galacticempire";
        if (text.Contains("rebel")) return "rebelalliance";
        return "";
    }
}
