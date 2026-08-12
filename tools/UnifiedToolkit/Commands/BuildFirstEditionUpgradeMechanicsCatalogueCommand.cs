using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Commands;

/// <summary>Promotes the reviewed First Edition upgrade-mechanics audit into the canonical, non-runtime catalogue.</summary>
public static partial class BuildFirstEditionUpgradeMechanicsCatalogueCommand
{
    private const int ExpectedUpgradeCount = 367;
    private const int ExpectedCategoryCount = 43;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]);
            var auditPath = Path.GetFullPath(Option(args, "--audit") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "upgrade-mechanics", "first-edition-upgrade-mechanics.json"));
            var outputPath = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "assets", "source", "unified1e", "reference", "cards", "upgrade-mechanics.json"));
            var upgradeDataPath = Path.Combine(repository, "source", "xwing-data", "data", "upgrades.js");
            var artworkManifestPath = Path.Combine(repository, "assets", "source", "unified1e", "reference", "cards", "upgrade-cards.json");

            RequireFile(auditPath, "R7 upgrade-mechanics audit");
            RequireFile(upgradeDataPath, "xwing-data upgrade definitions");
            RequireFile(artworkManifestPath, "Canonical upgrade-card manifest");
            var audit = JsonSerializer.Deserialize<PromotionAudit>(File.ReadAllText(auditPath), JsonOptions)
                ?? throw new InvalidDataException("The upgrade-mechanics audit could not be parsed.");
            if (audit.SchemaVersion != 2)
                throw new InvalidDataException($"Expected audit schema version 2; found {audit.SchemaVersion}.");
            if (audit.UpgradeCount != ExpectedUpgradeCount || audit.Upgrades.Count != ExpectedUpgradeCount)
                throw new InvalidDataException($"Expected {ExpectedUpgradeCount} audited upgrades; found {audit.Upgrades.Count}.");
            if (audit.CategorySummary.Count != ExpectedCategoryCount)
                throw new InvalidDataException($"Expected {ExpectedCategoryCount} mechanics categories; found {audit.CategorySummary.Count}.");

            using var definitionsDocument = JsonDocument.Parse(File.ReadAllText(upgradeDataPath));
            var definitions = definitionsDocument.RootElement.EnumerateArray()
                .ToDictionary(item => Int(item, "id"));
            var artworkManifest = JsonSerializer.Deserialize<UpgradeArtworkManifest>(File.ReadAllText(artworkManifestPath), JsonOptions)
                ?? throw new InvalidDataException("The canonical upgrade-card manifest could not be parsed.");
            var artworkById = artworkManifest.UpgradeCards.ToDictionary(card => card.Id);
            var duplicateAuditIds = audit.Upgrades.GroupBy(row => row.Id).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
            if (duplicateAuditIds.Count > 0)
                throw new InvalidDataException($"Duplicate audited upgrade IDs: {string.Join(", ", duplicateAuditIds)}.");

            var entries = new List<CanonicalUpgradeMechanicsEntry>();
            foreach (var row in audit.Upgrades.OrderBy(row => row.Id))
            {
                if (!definitions.TryGetValue(row.Id, out var definition))
                    throw new InvalidDataException($"Upgrade ID {row.Id} ({row.Name}) is absent from upgrades.js.");
                var definitionXws = Text(definition, "xws");
                if (!definitionXws.Equals(row.Xws, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Upgrade ID {row.Id} XWS mismatch: audit '{row.Xws}', source '{definitionXws}'.");
                var sourceText = PlainText(Text(definition, "text"));
                var sourceHash = Sha256(sourceText);
                if (!sourceHash.Equals(row.EffectTextSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Upgrade text changed after audit: {row.Name} ({row.Xws}). Run the audit again.");
                if (!artworkById.TryGetValue(row.Id, out var artwork))
                    throw new InvalidDataException($"Upgrade ID {row.Id} ({row.Name}) is absent from the canonical upgrade-card manifest.");
                var artworkPath = Path.Combine(repository, artwork.FaceRepositoryPath.Replace('/', Path.DirectorySeparatorChar));
                RequireFile(artworkPath, $"Canonical artwork for {row.Name}");
                if (row.Categories.Count == 0)
                    throw new InvalidDataException($"Upgrade {row.Name} has no mechanics categories.");

                entries.Add(new CanonicalUpgradeMechanicsEntry
                {
                    Id = row.Id,
                    Name = row.Name,
                    Xws = row.Xws,
                    Slot = row.Slot,
                    ArtworkRepositoryPath = Relative(repository, artworkPath),
                    EffectText = row.Text,
                    EffectTextSha256 = row.EffectTextSha256,
                    RuntimePriority = row.RuntimePriority,
                    RuntimePriorityReason = row.RuntimePriorityReason,
                    RestrictedShips = row.RestrictedShips,
                    RestrictedFactions = row.RestrictedFactions,
                    RestrictedSizes = row.RestrictedSizes,
                    IsLimited = row.IsLimited,
                    IsSquadLimited = row.IsSquadLimited,
                    Grants = row.Grants,
                    Conditions = row.Conditions,
                    Mechanics = row.Categories.Select(category => new CanonicalMechanicMembership
                    {
                        Id = category.Id,
                        Name = category.Name,
                        Evidence = category.Evidence,
                        ReviewStatus = "review-required",
                        RuntimeStatus = "not-implemented"
                    }).OrderBy(category => category.Id, StringComparer.OrdinalIgnoreCase).ToList()
                });
            }

            var categoryIds = entries.SelectMany(entry => entry.Mechanics).Select(category => category.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (categoryIds.Count != ExpectedCategoryCount)
                throw new InvalidDataException($"Expected {ExpectedCategoryCount} active catalogue categories; found {categoryIds.Count}.");

            var catalogue = new CanonicalUpgradeMechanicsCatalogue
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                SourceAuditSchemaVersion = audit.SchemaVersion,
                Authority = "Canonical First Edition upgrade-mechanics catalogue for discovery, validation and runtime planning.",
                ApprovalPolicy = "Mechanics memberships remain review-required until explicitly approved; catalogue presence does not imply runtime implementation.",
                UpgradeCount = entries.Count,
                CategoryCount = categoryIds.Count,
                MembershipCount = entries.Sum(entry => entry.Mechanics.Count),
                HighPriorityUpgradeCount = entries.Count(entry => entry.RuntimePriority == "high"),
                MediumPriorityUpgradeCount = entries.Count(entry => entry.RuntimePriority == "medium"),
                LowPriorityUpgradeCount = entries.Count(entry => entry.RuntimePriority == "low"),
                RuntimeImplementedUpgradeCount = 0,
                Categories = audit.CategorySummary.OrderBy(category => category.Id, StringComparer.OrdinalIgnoreCase).ToList(),
                Upgrades = entries
            };
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(catalogue, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Upgrade Mechanics Catalogue");
            Console.WriteLine("=========================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                    {repository}");
            Console.WriteLine($"Upgrade cards:                 {catalogue.UpgradeCount}");
            Console.WriteLine($"Mechanics categories:          {catalogue.CategoryCount}");
            Console.WriteLine($"Card/category memberships:     {catalogue.MembershipCount}");
            Console.WriteLine($"High runtime priority:         {catalogue.HighPriorityUpgradeCount}");
            Console.WriteLine($"Medium runtime priority:       {catalogue.MediumPriorityUpgradeCount}");
            Console.WriteLine($"Low runtime priority:          {catalogue.LowPriorityUpgradeCount}");
            Console.WriteLine($"Runtime-implemented upgrades:  {catalogue.RuntimeImplementedUpgradeCount}");
            Console.WriteLine($"Catalogue:                     {outputPath}");
            Console.WriteLine();
            Console.WriteLine("Canonical upgrade-mechanics catalogue built successfully.");
            Console.WriteLine("Memberships remain review-required and no gameplay was implemented or modified.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition upgrade-mechanics catalogue failed: {exception.Message}");
            return 1;
        }
    }

    private static string PlainText(string value) => Whitespace().Replace(Tag().Replace(value.Replace("<br />", " "), " "), " ").Trim();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int Int(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit build-first-edition-upgrade-mechanics-catalogue <repository> [--audit <file>] [--output <file>]");
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tag();
    [GeneratedRegex("\\s+")] private static partial Regex Whitespace();
}

public sealed class PromotionAudit
{
    public int SchemaVersion { get; init; } public int UpgradeCount { get; init; }
    public List<PromotionCategorySummary> CategorySummary { get; init; } = new();
    public List<PromotionUpgradeRow> Upgrades { get; init; } = new();
}
public sealed class UpgradeArtworkManifest
{
    public int SchemaVersion { get; init; } public List<UpgradeArtworkManifestEntry> UpgradeCards { get; init; } = new();
}
public sealed class UpgradeArtworkManifestEntry
{
    public int Id { get; init; } public string FaceRepositoryPath { get; init; } = "";
}
public sealed class PromotionUpgradeRow
{
    public int Id { get; init; } public string Name { get; init; } = ""; public string Xws { get; init; } = "";
    public string Slot { get; init; } = ""; public string Text { get; init; } = ""; public string EffectTextSha256 { get; init; } = "";
    public string RuntimePriority { get; init; } = ""; public string RuntimePriorityReason { get; init; } = "";
    public List<string> RestrictedShips { get; init; } = new(); public List<string> RestrictedFactions { get; init; } = new();
    public List<string> RestrictedSizes { get; init; } = new(); public bool IsLimited { get; init; } public bool IsSquadLimited { get; init; }
    public List<UpgradeMechanicGrant> Grants { get; init; } = new(); public List<string> Conditions { get; init; } = new();
    public List<UpgradeMechanicCategory> Categories { get; init; } = new();
}
public sealed class PromotionCategorySummary
{
    public string Id { get; init; } = ""; public string Name { get; init; } = ""; public int UpgradeCount { get; init; }
}
public sealed class CanonicalUpgradeMechanicsCatalogue
{
    public int SchemaVersion { get; init; } public DateTimeOffset GeneratedUtc { get; init; } public int SourceAuditSchemaVersion { get; init; }
    public string Authority { get; init; } = ""; public string ApprovalPolicy { get; init; } = "";
    public int UpgradeCount { get; init; } public int CategoryCount { get; init; } public int MembershipCount { get; init; }
    public int HighPriorityUpgradeCount { get; init; } public int MediumPriorityUpgradeCount { get; init; } public int LowPriorityUpgradeCount { get; init; }
    public int RuntimeImplementedUpgradeCount { get; init; }
    public List<PromotionCategorySummary> Categories { get; init; } = new();
    public List<CanonicalUpgradeMechanicsEntry> Upgrades { get; init; } = new();
}
public sealed class CanonicalUpgradeMechanicsEntry
{
    public int Id { get; init; } public string Name { get; init; } = ""; public string Xws { get; init; } = ""; public string Slot { get; init; } = "";
    public string ArtworkRepositoryPath { get; init; } = ""; public string EffectText { get; init; } = ""; public string EffectTextSha256 { get; init; } = "";
    public string RuntimePriority { get; init; } = ""; public string RuntimePriorityReason { get; init; } = "";
    public List<string> RestrictedShips { get; init; } = new(); public List<string> RestrictedFactions { get; init; } = new();
    public List<string> RestrictedSizes { get; init; } = new(); public bool IsLimited { get; init; } public bool IsSquadLimited { get; init; }
    public List<UpgradeMechanicGrant> Grants { get; init; } = new(); public List<string> Conditions { get; init; } = new();
    public List<CanonicalMechanicMembership> Mechanics { get; init; } = new();
}
public sealed class CanonicalMechanicMembership
{
    public string Id { get; init; } = ""; public string Name { get; init; } = ""; public List<string> Evidence { get; init; } = new();
    public string ReviewStatus { get; init; } = ""; public string RuntimeStatus { get; init; } = "";
}
