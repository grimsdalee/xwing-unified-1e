using UnifiedToolkit.Conversion.FirstEdition;

namespace UnifiedToolkit.Hybrid;

public static class HybridShipDefinitionBuilder
{
    public static HybridShipDefinitionDocument Build(
        FirstEditionRepository repository,
        string mappingVersion,
        IReadOnlyList<SpawnerFrameworkReference> frameworks,
        LegacyShipAssetCatalogue legacy,
        IReadOnlyList<ShipBaseSizeConversion> baseConversions,
        string unifiedSavePath,
        string legacySavePath)
    {
        var conversionsByShip = baseConversions.ToDictionary(x => x.ShipId, StringComparer.OrdinalIgnoreCase);
        var ships = repository.Ships.Select(ship => BuildShip(repository, ship, frameworks, legacy, conversionsByShip.GetValueOrDefault(ship.Id))).ToArray();
        return new HybridShipDefinitionDocument
        {
            SemanticMappingVersion = mappingVersion,
            UnifiedSavePath = Path.GetFullPath(unifiedSavePath),
            LegacySavePath = Path.GetFullPath(legacySavePath),
            Ships = ships,
            Summary = new HybridBuildSummary
            {
                ShipCount = ships.Length,
                FrameworkTemplateCount = frameworks.Count,
                LegacyModelObjectCount = legacy.ModelObjects.Count,
                LegacyShipFamilyCount = legacy.ShipFamilies.Count,
                UniqueAppearanceCount = legacy.ShipFamilies.Sum(x => x.Appearances.Count),
                ShipsWithFramework = ships.Count(x => x.Readiness.HasFramework),
                ShipsWithAppearance = ships.Count(x => x.Readiness.HasAppearance),
                ShipsWithDial = ships.Count(x => x.Readiness.HasDial),
                ShipsWithShipReference = ships.Count(x => x.Readiness.HasShipReference),
                ShipsWithPhysicalBaseToken = ships.Count(x => x.Readiness.HasPhysicalBaseToken),
                ShipsFrameworkReady = ships.Count(x => x.Readiness.FrameworkReady),
                ShipsAppearanceReady = ships.Count(x => x.Readiness.AppearanceReady),
                ShipsEditionAssetsReady = ships.Count(x => x.Readiness.EditionAssetsReady),
                ShipsReadyForObjectBuilder = ships.Count(x => x.Readiness.ReadyForObjectBuilder)
            }
        };
    }

    private static HybridShipDefinition BuildShip(
        FirstEditionRepository repository,
        FirstEditionShip ship,
        IReadOnlyList<SpawnerFrameworkReference> frameworks,
        LegacyShipAssetCatalogue legacy,
        ShipBaseSizeConversion? baseConversion)
    {
        var baseDefinition = FirstEditionBaseDefinitionCatalogue.Resolve(ship);
        var framework = SpawnerFrameworkAnalyser.SelectForSize(frameworks, baseDefinition.Size.ToString());
        var familyMatch = MatchFamily(ship, legacy.ShipFamilies);
        var appearances = familyMatch is null
            ? Array.Empty<ShipAppearanceVariant>()
            : familyMatch.Family.Appearances.Select(ToAppearance).ToArray();
        var references = MatchEditionAssets(ship, legacy.ShipReferences);
        var physicalTokens = MatchEditionAssets(ship, legacy.PhysicalBaseTokens);
        var dials = MatchEditionAssets(ship, legacy.Dials);
        var cards = MatchEditionAssets(ship, legacy.Cards);

        var frameworkReady = framework is not null
            && framework.HasLua
            && framework.HasSnapPoints
            && framework.HasContainedObjects
            && framework.HasBaseComponent
            && framework.HasPegComponent;
        var appearanceReady = familyMatch is not null && appearances.Length > 0;
        var editionReady = dials.Count > 0 && references.Count > 0;
        var issues = new List<string>();
        if (baseConversion is null) issues.Add("No base-size provenance record was generated.");
        if (baseConversion?.MediumRemoved == true) issues.Add($"Source 2.5 Medium base deliberately converted to First Edition {baseDefinition.Size} base.");

        if (framework is null) issues.Add("No size-compatible Unified 2.5 ship spawning framework was identified.");
        else if (!frameworkReady) issues.Add("The selected Unified 2.5 framework is missing one or more required structural elements.");
        if (familyMatch is null) issues.Add("No unambiguous legacy First Edition ship-model family matched this ship.");
        if (appearances.Length == 0) issues.Add("No confirmed legacy First Edition ship appearance was identified.");
        if (dials.Count == 0) issues.Add("No owner-level legacy First Edition manoeuvre dial was identified.");
        if (references.Count == 0) issues.Add("No legacy First Edition ship reference card was identified.");
        if (physicalTokens.Count == 0) issues.Add("Physical ship-base token source is not yet identified; this remains a tracked dependency.");

        return new HybridShipDefinition
        {
            SemanticData = ship,
            BaseDefinition = baseDefinition,
            BaseSizeConversion = baseConversion,
            Pilots = repository.FindPilotsByShip(ship.Id),
            SpawnFramework = framework,
            LegacyShipFamily = familyMatch is null ? null : new LegacyShipFamilyReference
            {
                FamilyId = familyMatch.Family.FamilyId,
                CanonicalKey = familyMatch.Family.CanonicalKey,
                DisplayName = familyMatch.Family.DisplayName,
                FactionHint = familyMatch.Family.FactionHint,
                SourcePath = familyMatch.Family.CollectionPath,
                DiscoveryMethod = familyMatch.Family.DiscoveryMethod,
                MatchScore = familyMatch.Score,
                MatchReasons = familyMatch.Reasons
            },
            AppearanceVariants = appearances,
            EditionAssets = new EditionAssetReferences
            {
                ShipReferences = references,
                PhysicalBaseTokens = physicalTokens,
                Dials = dials,
                Cards = cards
            },
            Readiness = new HybridReadiness
            {
                HasSemanticData = true,
                HasValidFirstEditionBase = true,
                HasFramework = framework is not null,
                HasAppearance = appearances.Length > 0,
                HasDial = dials.Count > 0,
                HasShipReference = references.Count > 0,
                HasPhysicalBaseToken = physicalTokens.Count > 0,
                FrameworkReady = frameworkReady,
                AppearanceReady = appearanceReady,
                EditionAssetsReady = editionReady,
                ReadyForObjectBuilder = frameworkReady && appearanceReady && editionReady,
                CompleteSaveReady = frameworkReady && appearanceReady && editionReady && physicalTokens.Count > 0 && cards.Count > 0,
                Issues = issues
            }
        };
    }

    private static ShipAppearanceVariant ToAppearance(LegacyAppearance source) => new()
    {
        VariantId = source.AppearanceId,
        AppearanceSignature = source.Signature,
        DisplayName = source.DisplayName,
        SourceGuid = source.SourceGuid,
        SourcePath = source.SourcePath,
        MeshUrl = source.MeshUrl,
        DiffuseUrl = source.DiffuseUrl,
        NormalUrl = source.NormalUrl,
        ColliderUrl = source.ColliderUrl,
        ProvenanceNames = source.ProvenanceNames,
        ProvenanceGuids = source.ProvenanceGuids,
        TemplateJson = source.TemplateJson
    };

    private static FamilyMatch? MatchFamily(FirstEditionShip ship, IReadOnlyList<LegacyShipFamily> families)
    {
        var candidates = families
            .Select(family => ScoreFamily(ship, family))
            .Where(x => x.Score >= 70)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Family.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0) return null;
        if (candidates.Length > 1 && candidates[0].Score == candidates[1].Score && candidates[0].Family.FamilyId != candidates[1].Family.FamilyId)
            return null;
        return candidates[0];
    }

    private static FamilyMatch ScoreFamily(FirstEditionShip ship, LegacyShipFamily family)
    {
        var reasons = new List<string>();
        var score = 0;

        if (!string.IsNullOrWhiteSpace(family.SemanticShipIdHint)
            && HybridText.Normalize(family.SemanticShipIdHint) == HybridText.Normalize(ship.Id))
        {
            score += 100;
            reasons.Add("legacy family was resolved directly through a First Edition semantic pilot");
        }
        else
        {
            var shipAliases = ShipAliases(ship);
            var familyAliases = family.Aliases.Append(family.CanonicalKey).Where(x => x.Length >= 2).Distinct().ToArray();
            if (shipAliases.Intersect(familyAliases, StringComparer.OrdinalIgnoreCase).Any())
            {
                score += 85;
                reasons.Add("canonical ship alias exactly matches legacy collection");
            }
            else if (shipAliases.Any(s => familyAliases.Any(f => s.Length >= 4 && f.Length >= 4 && (s.Contains(f) || f.Contains(s)))))
            {
                score += 70;
                reasons.Add("canonical ship alias contains legacy collection alias");
            }
        }

        var shipFactions = ship.Factions.Select(HybridText.Normalize).ToArray();
        if (!string.IsNullOrWhiteSpace(family.FactionHint))
        {
            if (shipFactions.Contains(HybridText.Normalize(family.FactionHint)))
            {
                score += 15;
                reasons.Add("faction matches");
            }
            else
            {
                score -= 35;
                reasons.Add("faction conflicts");
            }
        }

        return new FamilyMatch(family, score, reasons);
    }

    private static IReadOnlyList<string> ShipAliases(FirstEditionShip ship)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            HybridText.Normalize(ship.Id),
            HybridText.Normalize(ship.Name),
            HybridText.FamilyKey(ship.Name)
        };
        foreach (var value in ExpandKnownAliases(ship.Id, ship.Name)) aliases.Add(HybridText.Normalize(value));
        return aliases.Where(x => x.Length >= 2).ToArray();
    }

    private static IEnumerable<string> ExpandKnownAliases(string id, string name)
    {
        var key = HybridText.Normalize(id + name);
        if (key.Contains("xwing") && !key.Contains("t70")) yield return "xwing";
        if (key.Contains("t70")) { yield return "t70xwing"; yield return "t70"; }
        if (key.Contains("tieadvancedx1")) yield return "tieadvanced";
        if (key.Contains("tieadvancedv1")) yield return "tieadvancedprototype";
        if (key.Contains("starviper")) yield return "starviper";
        if (key.Contains("kihraxz")) yield return "kihraxz";
        if (key.Contains("firespray")) yield return "firespray";
        if (key.Contains("hwk290")) yield return "hwk290";
        if (key.Contains("yt1300")) yield return "yt1300";
        if (key.Contains("yt2400")) yield return "yt2400";
        if (key.Contains("z95")) yield return "z95headhunter";
        if (key.Contains("m12lkimogila")) yield return "kimogila";
        if (key.Contains("alphaclassstarwing")) yield return "starwing";
        if (key.Contains("auzituck")) yield return "auzituck";
        if (key.Contains("sheathipede")) yield return "sheathipede";
        if (key.Contains("tiesilencer")) yield return "tiesilencer";
        if (key.Contains("tieaggressor")) yield return "tieaggressor";
    }

    private static IReadOnlyList<EditionAssetReference> MatchEditionAssets(FirstEditionShip ship, IReadOnlyList<LegacyEditionAsset> assets)
    {
        var shipAliases = ShipAliases(ship);
        return assets
            .Select(asset => ScoreEditionAsset(ship, shipAliases, asset))
            .Where(x => x.Score >= 75)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Asset.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => new EditionAssetReference
            {
                AssetId = x.Asset.AssetId,
                SourceGuid = x.Asset.SourceGuid,
                SourceName = x.Asset.DisplayName,
                SourcePath = x.Asset.SourcePath,
                FactionHint = x.Asset.FactionHint,
                TemplateJson = x.Asset.TemplateJson,
                MatchScore = x.Score,
                MatchReasons = x.Reasons
            }).ToArray();
    }

    private static EditionMatch ScoreEditionAsset(FirstEditionShip ship, IReadOnlyList<string> shipAliases, LegacyEditionAsset asset)
    {
        var context = HybridText.Normalize(asset.DisplayName + " " + asset.SourcePath + " " + string.Join(' ', asset.ContextAliases));
        var reasons = new List<string>();
        var score = 0;
        if (shipAliases.Any(alias => alias.Length >= 3 && context.Contains(alias)))
        {
            score += 80;
            reasons.Add("ship alias appears in asset owner or hierarchy");
        }

        var factions = ship.Factions.Select(HybridText.Normalize).ToArray();
        if (!string.IsNullOrWhiteSpace(asset.FactionHint))
        {
            if (factions.Contains(HybridText.Normalize(asset.FactionHint)))
            {
                score += 15;
                reasons.Add("faction matches");
            }
            else
            {
                score -= 40;
                reasons.Add("faction conflicts");
            }
        }
        return new EditionMatch(asset, score, reasons);
    }

    private sealed record FamilyMatch(LegacyShipFamily Family, int Score, IReadOnlyList<string> Reasons);
    private sealed record EditionMatch(LegacyEditionAsset Asset, int Score, IReadOnlyList<string> Reasons);
}
