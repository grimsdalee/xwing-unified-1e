using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace UnifiedToolkit.Commands;

/// <summary>Runs and aggregates the read-only Phase 16 asset and semantic contract audits.</summary>
public static class AuditFirstEditionPhase16CompletenessCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            if (!Directory.Exists(repository))
                throw new DirectoryNotFoundException($"Repository not found: {repository}");

            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "completeness"));
            var reportRoot = Path.Combine(repository, "_unifiedtoolkit_reports", "phase16");

            Console.WriteLine("UnifiedToolkit Phase 16 First Edition Completeness Audit");
            Console.WriteLine("=========================================================");
            Console.WriteLine();
            Console.WriteLine("Refreshing the three read-only source audits...");
            Console.WriteLine();

            var cardExitCode = AuditFirstEditionCardAndTokenAssetsCommand.Run(new[] { repository });
            var gameplayExitCode = AuditFirstEditionGameplayObjectsCommand.Run(new[] { repository });
            var loadoutExitCode = VerifyFirstEditionLoadoutContractCommand.Run(new[] { repository });

            var cardPath = Path.Combine(reportRoot, "card-token-audit", "first-edition-card-token-assets.json");
            var gameplayPath = Path.Combine(reportRoot, "gameplay-object-inventory", "first-edition-gameplay-objects.json");
            var loadoutPath = Path.Combine(reportRoot, "loadout-contract", "first-edition-loadout-contract-verification.json");
            RequireFile(cardPath, "Card and token audit");
            RequireFile(gameplayPath, "Gameplay object inventory");
            RequireFile(loadoutPath, "Loadout contract verification");

            using var cardDocument = JsonDocument.Parse(File.ReadAllText(cardPath));
            using var gameplayDocument = JsonDocument.Parse(File.ReadAllText(gameplayPath));
            using var loadoutDocument = JsonDocument.Parse(File.ReadAllText(loadoutPath));

            var card = SummarizeCards(cardDocument.RootElement);
            var gameplay = SummarizeGameplay(gameplayDocument.RootElement);
            var loadout = SummarizeLoadout(loadoutDocument.RootElement);
            var auditExitCodes = new Phase16AuditExitCodes(cardExitCode, gameplayExitCode, loadoutExitCode);

            var assetAndSemanticReady = auditExitCodes.AllSuccessful
                && card.IsComplete
                && gameplay.RequiredObjectsComplete
                && loadout.IsComplete;
            var deferred = gameplay.DeferredOptionalObjects
                .Select(item => new Phase16DeferredItem(item.Id, item.Name, "optional-gameplay-object",
                    "Optional/review object; not required for the approved Phase 16 asset scope."))
                .Append(new Phase16DeferredItem("reference-cards", "Rules reference cards", "reference-material",
                    "Player reference material is non-integral and may be imported later."))
                .Append(new Phase16DeferredItem("obstacle-high-resolution-remap", "High-resolution obstacle artwork",
                    "artwork-quality", "Current obstacle sets are canonical; higher-resolution artwork and UV remapping may be revisited later."))
                .Append(new Phase16DeferredItem("upgrade-runtime-effects", "Upgrade runtime effects", "runtime",
                    "Runtime implementation is outside this asset and semantic completeness audit."))
                .ToList();

            var result = new Phase16CompletenessResult(
                "1.0",
                DateTimeOffset.UtcNow,
                repository,
                assetAndSemanticReady,
                RuntimeReady: false,
                ReadyForReviewedRuntimeArchitectureStep: assetAndSemanticReady,
                auditExitCodes,
                card,
                gameplay,
                loadout,
                deferred,
                new[]
                {
                    Relative(repository, cardPath),
                    Relative(repository, gameplayPath),
                    Relative(repository, loadoutPath)
                });

            Directory.CreateDirectory(output);
            var jsonPath = Path.Combine(output, "first-edition-phase16-completeness.json");
            var markdownPath = Path.Combine(output, "FIRST-EDITION-PHASE16-COMPLETENESS.md");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(markdownPath, Markdown(result), new UTF8Encoding(false));

            Console.WriteLine();
            Console.WriteLine("Phase 16 completeness summary");
            Console.WriteLine("================================");
            Console.WriteLine();
            Console.WriteLine($"Asset and semantic scope ready: {result.AssetAndSemanticReady}");
            Console.WriteLine($"Required gameplay objects:       {gameplay.CanonicalRequiredObjectCount}/{gameplay.RequiredObjectCount}");
            Console.WriteLine($"Optional objects deferred:       {gameplay.DeferredOptionalObjects.Count}");
            Console.WriteLine($"Upgrade cards and artwork:       {card.UpgradeArtworkAvailable}/{card.UpgradeCardCount}");
            Console.WriteLine($"Condition card pairs:            {card.CompleteConditionCardPairs}/{card.ConditionCardCount}");
            Console.WriteLine($"Standard damage decks:           {card.CompleteStandardDamageDecks}/{card.StandardDamageDeckCount}");
            Console.WriteLine($"Epic section decks:              {card.CompleteEpicSectionDecks}/{card.EpicSectionDeckCount}");
            Console.WriteLine($"Loadout contract valid:          {loadout.IsValid}");
            Console.WriteLine($"Runtime effects implemented:     Not assessed by this audit");
            Console.WriteLine();
            Console.WriteLine($"Audit:  {jsonPath}");
            Console.WriteLine($"Report: {markdownPath}");
            Console.WriteLine();
            Console.WriteLine("Completeness audit completed. No source assets, mappings, Lua scripts or gameplay state were modified.");
            return assetAndSemanticReady ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Phase 16 completeness audit failed: {exception.Message}");
            return 1;
        }
    }

    private static Phase16CardSummary SummarizeCards(JsonElement root)
    {
        var conditions = root.GetProperty("conditions").EnumerateArray().ToList();
        var upgrades = root.GetProperty("upgrades").EnumerateArray().ToList();
        var backs = root.GetProperty("upgradeCardBacks").EnumerateArray().ToList();
        var standardDecks = root.GetProperty("damageDecks").EnumerateArray()
            .Where(item => Bool(item, "canonicalImportExpected")).ToList();
        var epicDecks = root.GetProperty("epicDamageDecks").EnumerateArray().ToList();

        return new Phase16CardSummary(
            conditions.Count,
            conditions.Count(item => Bool(item, "canonicalArtworkAvailable") && Bool(item, "backArtworkAvailable")),
            conditions.Where(item => Bool(item, "tokenExpected")).Select(item => Text(item, "tokenRepositoryPath"))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            conditions.Where(item => Bool(item, "tokenArtworkAvailable")).Select(item => Text(item, "tokenRepositoryPath"))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            upgrades.Count,
            upgrades.Count(item => Bool(item, "artworkAvailable")),
            backs.Count,
            backs.Count(item => Bool(item, "artworkAvailable")),
            standardDecks.Count,
            standardDecks.Count(item => Bool(item, "canonicalArtworkComplete")),
            standardDecks.Where(item => Bool(item, "canonicalArtworkComplete")).Sum(item => Int(item, "physicalCardCount")),
            epicDecks.Select(item => Text(item, "shipId")).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            epicDecks.Count,
            epicDecks.Count(item => Bool(item, "artworkComplete")),
            epicDecks.Sum(item => Int(item, "physicalCardCount")),
            root.GetProperty("missingEpicDamageDeckArtwork").GetArrayLength());
    }

    private static Phase16GameplaySummary SummarizeGameplay(JsonElement root)
    {
        var requirements = root.GetProperty("requirements").EnumerateArray().ToList();
        var required = requirements.Where(item => Text(item, "policy") == "required").ToList();
        var optional = requirements.Where(item => Text(item, "policy") == "optional").ToList();
        var deferred = optional.Where(item => Text(item, "status") != "canonical")
            .Select(item => new Phase16OptionalObject(Text(item, "id"), Text(item, "name"), Text(item, "status")))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToList();

        return new Phase16GameplaySummary(
            Int(root, "requiredObjectCount"),
            required.Count(item => Text(item, "status") == "canonical"),
            Int(root, "optionalObjectCount"),
            Int(root, "canonicalCount"),
            Int(root, "partialCanonicalCount"),
            Int(root, "candidateReviewCount"),
            Int(root, "missingCount"),
            deferred);
    }

    private static Phase16LoadoutSummary SummarizeLoadout(JsonElement root)
    {
        var issues = root.GetProperty("issues").EnumerateArray().ToList();
        return new Phase16LoadoutSummary(
            Int(root, "pilotCount"), Int(root, "shipCount"), Int(root, "upgradeCount"),
            Int(root, "mechanicsUpgradeCount"), Int(root, "conditionAssignmentCount"),
            Int(root, "printedSlotCount"), Int(root, "distinctSlotTypeCount"),
            Int(root, "acceptanceScenarioCount"), Int(root, "acceptanceScenarioFailureCount"),
            issues.Count(item => Text(item, "severity") == "error"),
            issues.Count(item => Text(item, "severity") == "warning"), Bool(root, "isValid"));
    }

    private static string Markdown(Phase16CompletenessResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# First Edition Phase 16 Completeness Audit");
        builder.AppendLine();
        builder.AppendLine($"Generated: {result.GeneratedUtc:O}");
        builder.AppendLine();
        builder.AppendLine("## Outcome");
        builder.AppendLine();
        builder.AppendLine($"- Asset and semantic scope ready: **{result.AssetAndSemanticReady}**");
        builder.AppendLine($"- Ready for reviewed runtime architecture step: **{result.ReadyForReviewedRuntimeArchitectureStep}**");
        builder.AppendLine($"- Runtime ready: **{result.RuntimeReady}** - runtime effects are deliberately outside this audit.");
        builder.AppendLine();
        builder.AppendLine("## Verified scope");
        builder.AppendLine();
        builder.AppendLine($"- Upgrade artwork: {result.Cards.UpgradeArtworkAvailable}/{result.Cards.UpgradeCardCount}");
        builder.AppendLine($"- Upgrade-card backs: {result.Cards.UpgradeCardBacksAvailable}/{result.Cards.UpgradeCardBackCount}");
        builder.AppendLine($"- Complete condition card pairs: {result.Cards.CompleteConditionCardPairs}/{result.Cards.ConditionCardCount}");
        builder.AppendLine($"- Physical condition-token designs: {result.Cards.ConditionTokenDesignsAvailable}/{result.Cards.ConditionTokenDesignCount}");
        builder.AppendLine($"- Standard damage decks: {result.Cards.CompleteStandardDamageDecks}/{result.Cards.StandardDamageDeckCount} ({result.Cards.StandardPhysicalCards} physical cards)");
        builder.AppendLine($"- Epic damage sections: {result.Cards.CompleteEpicSectionDecks}/{result.Cards.EpicSectionDeckCount} ({result.Cards.EpicPhysicalCards} physical cards)");
        builder.AppendLine($"- Required gameplay objects: {result.GameplayObjects.CanonicalRequiredObjectCount}/{result.GameplayObjects.RequiredObjectCount}");
        builder.AppendLine($"- Loadout acceptance scenarios: {result.LoadoutContract.AcceptanceScenarioCount - result.LoadoutContract.AcceptanceScenarioFailureCount}/{result.LoadoutContract.AcceptanceScenarioCount}");
        builder.AppendLine($"- Loadout contract errors: {result.LoadoutContract.ErrorCount}");
        builder.AppendLine($"- Loadout contract warnings: {result.LoadoutContract.WarningCount}");
        builder.AppendLine();
        builder.AppendLine("## Deferred without blocking Phase 16 asset readiness");
        builder.AppendLine();
        foreach (var item in result.DeferredItems)
            builder.AppendLine($"- **{item.Name}** (`{item.Id}`): {item.Reason}");
        builder.AppendLine();
        builder.AppendLine("## Source audits");
        builder.AppendLine();
        foreach (var path in result.SourceReports)
            builder.AppendLine($"- `{path}`");
        builder.AppendLine();
        builder.AppendLine("This command regenerated reports only. It did not modify source assets, mappings, Lua scripts or gameplay state.");
        return builder.ToString();
    }

    private static int Int(JsonElement element, string name) => element.GetProperty(name).GetInt32();
    private static bool Bool(JsonElement element, string name) => element.GetProperty(name).GetBoolean();
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;
    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found", path);
    }
    private static string Relative(string repository, string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(index => args[index + 1]).FirstOrDefault();
    private static void ShowUsage() => Console.WriteLine(
        "Usage: UnifiedToolkit audit-first-edition-phase16-completeness <repository> [--output <folder>]");
}

public sealed record Phase16CompletenessResult(
    string SchemaVersion,
    DateTimeOffset GeneratedUtc,
    string Repository,
    bool AssetAndSemanticReady,
    bool RuntimeReady,
    bool ReadyForReviewedRuntimeArchitectureStep,
    Phase16AuditExitCodes AuditExitCodes,
    Phase16CardSummary Cards,
    Phase16GameplaySummary GameplayObjects,
    Phase16LoadoutSummary LoadoutContract,
    List<Phase16DeferredItem> DeferredItems,
    string[] SourceReports);

public sealed record Phase16AuditExitCodes(int CardAndTokenAssets, int GameplayObjects, int LoadoutContract)
{
    public bool AllSuccessful => CardAndTokenAssets == 0 && GameplayObjects == 0 && LoadoutContract == 0;
}

public sealed record Phase16CardSummary(
    int ConditionCardCount, int CompleteConditionCardPairs, int ConditionTokenDesignCount,
    int ConditionTokenDesignsAvailable, int UpgradeCardCount, int UpgradeArtworkAvailable,
    int UpgradeCardBackCount, int UpgradeCardBacksAvailable, int StandardDamageDeckCount,
    int CompleteStandardDamageDecks, int StandardPhysicalCards, int EpicShipCount,
    int EpicSectionDeckCount, int CompleteEpicSectionDecks, int EpicPhysicalCards,
    int IncompleteEpicShipCount)
{
    public bool IsComplete => ConditionCardCount == CompleteConditionCardPairs
        && ConditionTokenDesignCount == ConditionTokenDesignsAvailable
        && UpgradeCardCount == UpgradeArtworkAvailable
        && UpgradeCardBackCount == UpgradeCardBacksAvailable
        && StandardDamageDeckCount == CompleteStandardDamageDecks
        && EpicSectionDeckCount == CompleteEpicSectionDecks
        && IncompleteEpicShipCount == 0;
}

public sealed record Phase16GameplaySummary(
    int RequiredObjectCount, int CanonicalRequiredObjectCount, int OptionalObjectCount,
    int CanonicalObjectCount, int PartialCanonicalCount, int CandidateReviewCount,
    int MissingCount, List<Phase16OptionalObject> DeferredOptionalObjects)
{
    public bool RequiredObjectsComplete => RequiredObjectCount == CanonicalRequiredObjectCount
        && PartialCanonicalCount == 0 && MissingCount == 0;
}

public sealed record Phase16OptionalObject(string Id, string Name, string Status);

public sealed record Phase16LoadoutSummary(
    int PilotCount, int ShipCount, int UpgradeCount, int MechanicsUpgradeCount,
    int ConditionAssignmentCount, int PrintedSlotCount, int DistinctSlotTypeCount,
    int AcceptanceScenarioCount, int AcceptanceScenarioFailureCount,
    int ErrorCount, int WarningCount, bool IsValid)
{
    public bool IsComplete => IsValid && ErrorCount == 0 && AcceptanceScenarioFailureCount == 0
        && UpgradeCount == MechanicsUpgradeCount;
}

public sealed record Phase16DeferredItem(string Id, string Name, string Category, string Reason);
