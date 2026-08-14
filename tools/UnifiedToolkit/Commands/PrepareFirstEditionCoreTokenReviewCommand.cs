using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 16E-R3 read-only preparation of a labelled Tabletop Simulator review
/// save and editable selection manifest for the core First Edition token set.
/// No candidate is approved, copied, converted or enabled by this command.
/// </summary>
public static class PrepareFirstEditionCoreTokenReviewCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ReviewRequirementIds =
    {
        "focus", "evade", "stress", "ion", "target-lock", "shield",
        "cloak", "tractor", "reinforce", "energy", "weapons-disabled",
        "jam", "ordnance"
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
            RequireDirectory(repository, "Repository");

            var inventoryPath = Path.GetFullPath(Option(args, "--inventory") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "gameplay-object-inventory",
                "first-edition-gameplay-objects.json"));
            var referenceSavePath = Path.GetFullPath(Option(args, "--reference-save") ?? Path.Combine(
                repository, "source", "unified-2.5", "Unified2.5_Reference.json"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "gameplay-object-review"));
            var assetBaseUrl = (Option(args, "--asset-base-url") ??
                "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/").TrimEnd('/') + "/";
            var maximumCandidates = IntegerOption(args, "--max-candidates", 8, 1, 20);

            RequireFile(inventoryPath, "Phase 16E gameplay-object inventory");
            RequireFile(referenceSavePath, "TTS reference save");
            var inventory = JsonSerializer.Deserialize<FirstEditionGameplayObjectInventory>(
                File.ReadAllText(inventoryPath), JsonOptions)
                ?? throw new InvalidDataException($"Could not parse inventory: {inventoryPath}");

            var requirementIndex = inventory.Requirements.ToDictionary(
                item => item.Id, StringComparer.OrdinalIgnoreCase);
            var rows = new List<CoreTokenReviewRow>();
            var warnings = new List<string>();

            foreach (var requirementId in ReviewRequirementIds)
            {
                if (!requirementIndex.TryGetValue(requirementId, out var requirement))
                {
                    warnings.Add($"Inventory does not contain requirement '{requirementId}'.");
                    continue;
                }

                var eligible = requirement.Candidates
                    .Where(IsRasterImage)
                    .Where(candidate => File.Exists(RepositoryFile(repository, candidate.RepositoryPath)))
                    .GroupBy(candidate => candidate.Sha256, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderBy(candidate => CandidateRank(requirement.Id, candidate))
                        .ThenBy(candidate => SourceRank(candidate.Source))
                        .ThenBy(candidate => candidate.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                        .First())
                    .OrderBy(candidate => CandidateRank(requirement.Id, candidate))
                    .ThenBy(candidate => SourceRank(candidate.Source))
                    .ThenBy(candidate => candidate.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                    .Take(maximumCandidates)
                    .ToList();

                if (eligible.Count == 0)
                    warnings.Add($"{requirement.Name} has no locally available raster candidates.");

                rows.Add(new CoreTokenReviewRow
                {
                    RequirementId = requirement.Id,
                    RequirementName = requirement.Name,
                    InventoryCandidateCount = requirement.Candidates.Count,
                    ReviewCandidateCount = eligible.Count,
                    Candidates = eligible.Select((candidate, index) => new CoreTokenReviewCandidate
                    {
                        CandidateNumber = index + 1,
                        Source = candidate.Source,
                        NameEvidence = candidate.NameEvidence,
                        RepositoryPath = candidate.RepositoryPath,
                        Extension = candidate.Extension,
                        SizeBytes = candidate.SizeBytes,
                        Sha256 = candidate.Sha256,
                        SourceUrl = candidate.SourceUrl,
                        ReviewImageUrl = AssetUrl(assetBaseUrl, candidate.RepositoryPath),
                        Decision = string.Empty,
                        Notes = string.Empty
                    }).ToList()
                });
            }

            Directory.CreateDirectory(output);
            var savePath = Path.Combine(output, "first-edition-core-token-candidate-review.json");
            var manifestPath = Path.Combine(output, "first-edition-core-token-candidate-review-manifest.json");
            var selectionsPath = Path.Combine(output, "first-edition-core-token-candidate-selections.csv");
            var reportPath = Path.Combine(output, "FIRST-EDITION-CORE-TOKEN-CANDIDATE-REVIEW.md");

            var manifest = new CoreTokenReviewManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Policy = "Review only. Blank decisions approve nothing and cause no asset or gameplay changes.",
                RepositoryRoot = NormalisePath(repository),
                InventoryPath = NormalisePath(inventoryPath),
                ReferenceSavePath = NormalisePath(referenceSavePath),
                AssetBaseUrl = assetBaseUrl,
                MaximumCandidatesPerRequirement = maximumCandidates,
                RequirementCount = rows.Count,
                CandidateCount = rows.Sum(row => row.ReviewCandidateCount),
                WarningCount = warnings.Count,
                Warnings = warnings,
                Rows = rows
            };

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(savePath, BuildSave(manifest, referenceSavePath).ToJsonString(JsonOptions), new UTF8Encoding(false));
            WriteSelections(selectionsPath, rows);
            WriteReport(reportPath, manifest, savePath, selectionsPath);

            Console.WriteLine("UnifiedToolkit Phase 16E-R3 Core Token Candidate Review");
            Console.WriteLine("========================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                    {repository}");
            Console.WriteLine($"Inventory:                     {inventoryPath}");
            Console.WriteLine($"Reference save:                {referenceSavePath}");
            Console.WriteLine($"Asset base URL:                {assetBaseUrl}");
            Console.WriteLine($"Requirements:                  {manifest.RequirementCount}");
            Console.WriteLine($"Review candidates:             {manifest.CandidateCount}");
            Console.WriteLine($"Maximum candidates per type:   {maximumCandidates}");
            Console.WriteLine($"Warnings:                      {manifest.WarningCount}");
            Console.WriteLine();
            Console.WriteLine($"TTS review save: {savePath}");
            Console.WriteLine($"Selections:      {selectionsPath}");
            Console.WriteLine($"Manifest:        {manifestPath}");
            Console.WriteLine($"Report:          {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Review package prepared. No candidates were approved and no source assets or gameplay state were modified.");
            return warnings.Count == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Core token candidate review preparation failed: {exception.Message}");
            return 1;
        }
    }

    private static JsonObject BuildSave(CoreTokenReviewManifest manifest, string referenceSavePath)
    {
        const float columnSpacing = 3.4f;
        const float rowSpacing = 4.2f;
        const float tileY = 1.1f;
        var objects = new JsonArray();
        var guidCounter = 1;
        var rowCount = manifest.Rows.Count;
        var startZ = ((rowCount - 1) * rowSpacing) / 2.0f;

        for (var rowIndex = 0; rowIndex < manifest.Rows.Count; rowIndex++)
        {
            var row = manifest.Rows[rowIndex];
            var z = startZ - rowIndex * rowSpacing;
            var candidateCount = Math.Max(1, row.Candidates.Count);
            var startX = -((candidateCount - 1) * columnSpacing) / 2.0f;
            var labelX = startX - 5.0f;

            objects.Add(BuildLabel(
                Guid(guidCounter++), row.RequirementName,
                $"{row.ReviewCandidateCount} displayed / {row.InventoryCandidateCount} inventory candidates",
                labelX, z));

            for (var candidateIndex = 0; candidateIndex < row.Candidates.Count; candidateIndex++)
            {
                var candidate = row.Candidates[candidateIndex];
                var x = startX + candidateIndex * columnSpacing;
                objects.Add(BuildCandidateTile(
                    Guid(guidCounter++), row, candidate, x, tileY, z));
            }
        }

        var envelope = JsonNode.Parse(File.ReadAllText(referenceSavePath))?.AsObject()
            ?? throw new InvalidDataException($"Could not parse TTS reference save: {referenceSavePath}");
        envelope["SaveName"] = "X-Wing Unified 1E - Phase 16E-R3 Core Token Candidate Review";
        envelope["GameMode"] = string.Empty;
        envelope["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
        envelope["Note"] = "Review only. Hover over each candidate for its source and repository path. No candidate is approved by this save.";
        envelope["Rules"] = string.Empty;
        envelope["XmlUI"] = string.Empty;
        envelope["LuaScript"] = string.Empty;
        envelope["LuaScriptState"] = string.Empty;
        envelope["ObjectStates"] = objects;
        return envelope;
    }

    private static JsonObject BuildCandidateTile(
        string guid,
        CoreTokenReviewRow row,
        CoreTokenReviewCandidate candidate,
        float x,
        float y,
        float z)
    {
        var gmNotes = JsonSerializer.Serialize(new
        {
            phase = "16E-R3",
            requirementId = row.RequirementId,
            candidateNumber = candidate.CandidateNumber,
            source = candidate.Source,
            repositoryPath = candidate.RepositoryPath,
            sha256 = candidate.Sha256,
            decision = string.Empty
        });

        return new JsonObject
        {
            ["GUID"] = guid,
            ["Name"] = "Custom_Tile",
            ["Transform"] = Transform(x, y, z, 0.0f, 180.0f, 0.0f, 1.6f, 1.0f, 1.6f),
            ["Nickname"] = $"{row.RequirementName} — candidate {candidate.CandidateNumber}",
            ["Description"] = $"Source: {candidate.Source}\nEvidence: {candidate.NameEvidence}\nPath: {candidate.RepositoryPath}\nSHA-256: {candidate.Sha256}",
            ["GMNotes"] = gmNotes,
            ["AltLookAngle"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0 },
            ["ColorDiffuse"] = Color(1.0f, 1.0f, 1.0f),
            ["LayoutGroupSortIndex"] = 0,
            ["Value"] = 0,
            ["Locked"] = true,
            ["Grid"] = true,
            ["Snap"] = true,
            ["IgnoreFoW"] = false,
            ["MeasureMovement"] = false,
            ["DragSelectable"] = true,
            ["Autoraise"] = true,
            ["Sticky"] = true,
            ["Tooltip"] = true,
            ["GridProjection"] = false,
            ["HideWhenFaceDown"] = false,
            ["Hands"] = false,
            ["CustomImage"] = new JsonObject
            {
                ["ImageURL"] = candidate.ReviewImageUrl,
                ["ImageSecondaryURL"] = string.Empty,
                ["ImageScalar"] = 1.0,
                ["WidthScale"] = 0.0,
                ["CustomTile"] = new JsonObject
                {
                    ["Type"] = 0,
                    ["Thickness"] = 0.1,
                    ["Stackable"] = false,
                    ["Stretch"] = true
                }
            },
            ["LuaScript"] = string.Empty,
            ["LuaScriptState"] = string.Empty,
            ["XmlUI"] = string.Empty
        };
    }

    private static JsonObject BuildLabel(string guid, string title, string detail, float x, float z) => new()
    {
        ["GUID"] = guid,
        ["Name"] = "Notecard",
        ["Transform"] = Transform(x, 1.0f, z, 0.0f, 180.0f, 0.0f, 1.5f, 1.0f, 1.5f),
        ["Nickname"] = title,
        ["Description"] = detail,
        ["GMNotes"] = "Phase 16E-R3 review row label",
        ["AltLookAngle"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0 },
        ["ColorDiffuse"] = Color(0.25f, 0.35f, 0.55f),
        ["LayoutGroupSortIndex"] = 0,
        ["Value"] = 0,
        ["Locked"] = true,
        ["Grid"] = true,
        ["Snap"] = true,
        ["IgnoreFoW"] = false,
        ["MeasureMovement"] = false,
        ["DragSelectable"] = true,
        ["Autoraise"] = true,
        ["Sticky"] = true,
        ["Tooltip"] = true,
        ["GridProjection"] = false,
        ["HideWhenFaceDown"] = false,
        ["Hands"] = false,
        ["Memo"] = $"{title}\n\n{detail}\n\nReview only; this label approves nothing.",
        ["LuaScript"] = string.Empty,
        ["LuaScriptState"] = string.Empty,
        ["XmlUI"] = string.Empty
    };

    private static JsonObject Transform(
        float positionX, float positionY, float positionZ,
        float rotationX, float rotationY, float rotationZ,
        float scaleX, float scaleY, float scaleZ) => new()
    {
        ["posX"] = positionX, ["posY"] = positionY, ["posZ"] = positionZ,
        ["rotX"] = rotationX, ["rotY"] = rotationY, ["rotZ"] = rotationZ,
        ["scaleX"] = scaleX, ["scaleY"] = scaleY, ["scaleZ"] = scaleZ
    };

    private static JsonObject Color(float r, float g, float b) => new()
    {
        ["r"] = r, ["g"] = g, ["b"] = b
    };

    private static void WriteSelections(string path, IEnumerable<CoreTokenReviewRow> rows)
    {
        var lines = new List<string>
        {
            "RequirementId,RequirementName,CandidateNumber,Source,NameEvidence,RepositoryPath,Extension,SizeBytes,Sha256,Decision,Notes"
        };
        foreach (var row in rows)
        foreach (var candidate in row.Candidates)
        {
            lines.Add(string.Join(',', new[]
            {
                Quote(row.RequirementId), Quote(row.RequirementName), candidate.CandidateNumber.ToString(CultureInfo.InvariantCulture),
                Quote(candidate.Source), Quote(candidate.NameEvidence), Quote(candidate.RepositoryPath), Quote(candidate.Extension),
                candidate.SizeBytes.ToString(CultureInfo.InvariantCulture), Quote(candidate.Sha256), Quote(candidate.Decision), Quote(candidate.Notes)
            }));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteReport(
        string path,
        CoreTokenReviewManifest manifest,
        string savePath,
        string selectionsPath)
    {
        var lines = new List<string>
        {
            "# Phase 16E-R3 First Edition Core Token Candidate Review", "",
            $"- Requirements: **{manifest.RequirementCount}**",
            $"- Review candidates: **{manifest.CandidateCount}**",
            $"- Maximum candidates per requirement: **{manifest.MaximumCandidatesPerRequirement}**",
            $"- Warnings: **{manifest.WarningCount}**", "",
            "This package is for visual review only. Blank decisions approve nothing and no source asset was copied, converted or enabled.", "",
            $"- TTS review save: `{NormalisePath(savePath)}`",
            $"- Editable selections: `{NormalisePath(selectionsPath)}`", "",
            "## Review rows", ""
        };
        lines.AddRange(manifest.Rows.Select(row =>
            $"- **{row.RequirementName}** — {row.ReviewCandidateCount} displayed from {row.InventoryCandidateCount} inventory candidates"));
        lines.AddRange(new[] { "", "## Decision values", "", "Use `approve`, `reject`, or `defer` in the Decision column. Leave all other fields unchanged.", "", "## Warnings", "" });
        lines.AddRange(manifest.Warnings.Count == 0 ? new[] { "- None." } : manifest.Warnings.Select(warning => "- " + warning));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static bool IsRasterImage(GameplayObjectAssetCandidate candidate) =>
        candidate.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
        || candidate.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || candidate.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static int SourceRank(string source) => source switch
    {
        "unified1e" => 0,
        "xwvassal" => 1,
        "legacy1e-sorted" => 2,
        "legacy1e" => 3,
        "unified25" => 4,
        _ => 5
    };

    private static int CandidateRank(string requirementId, GameplayObjectAssetCandidate candidate)
    {
        var target = Normalise(requirementId);
        var evidence = Normalise(candidate.NameEvidence);
        var path = candidate.RepositoryPath.Replace('\\', '/').ToLowerInvariant();
        var tokenTarget = "token" + target;
        var score = evidence == tokenTarget || evidence == target + "token"
            ? 0
            : evidence.StartsWith(tokenTarget, StringComparison.Ordinal)
                ? 5
                : evidence.Contains(tokenTarget, StringComparison.Ordinal)
                    ? 10
                    : evidence.Contains(target, StringComparison.Ordinal)
                        ? 30
                        : 60;

        if (evidence.Contains("2e", StringComparison.Ordinal) || evidence.StartsWith("u2e", StringComparison.Ordinal)) score += 200;
        if (evidence.StartsWith("action", StringComparison.Ordinal)) score += 80;
        if (evidence.StartsWith("ref", StringComparison.Ordinal)) score += 60;
        if (path.Contains("/upgrade-cards/") || path.Contains("/pilot-cards/") || path.Contains("/card-backs/")) score += 150;
        return score;
    }

    private static string Normalise(string value) => new(
        (value ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string AssetUrl(string baseUrl, string repositoryPath) =>
        baseUrl + string.Join('/', repositoryPath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

    private static string RepositoryFile(string repository, string repositoryPath) =>
        Path.GetFullPath(Path.Combine(repository, repositoryPath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Guid(int value) => value.ToString("x6", CultureInfo.InvariantCulture)[^6..];
    private static string NormalisePath(string value) => value.Replace('\\', '/');
    private static string Quote(object? value) => $"\"{(value?.ToString() ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static int IntegerOption(string[] args, string name, int fallback, int minimum, int maximum)
    {
        var value = Option(args, name);
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
            throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum} and {maximum}.");
        return parsed;
    }

    private static string? Option(string[] args, string name) =>
        Enumerable.Range(0, Math.Max(0, args.Length - 1))
            .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(index => args[index + 1])
            .FirstOrDefault();

    private static void RequireDirectory(string path, string label)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}");
    }

    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path);
    }

    private static void ShowUsage() => Console.WriteLine(
        "Usage: UnifiedToolkit prepare-first-edition-core-token-review <repository> " +
        "[--inventory <file>] [--reference-save <file>] [--asset-base-url <url>] " +
        "[--max-candidates <1-20>] [--output <folder>]");
}

public sealed class CoreTokenReviewManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string Policy { get; init; } = string.Empty;
    public string RepositoryRoot { get; init; } = string.Empty;
    public string InventoryPath { get; init; } = string.Empty;
    public string ReferenceSavePath { get; init; } = string.Empty;
    public string AssetBaseUrl { get; init; } = string.Empty;
    public int MaximumCandidatesPerRequirement { get; init; }
    public int RequirementCount { get; init; }
    public int CandidateCount { get; init; }
    public int WarningCount { get; init; }
    public List<string> Warnings { get; init; } = new();
    public List<CoreTokenReviewRow> Rows { get; init; } = new();
}

public sealed class CoreTokenReviewRow
{
    public string RequirementId { get; init; } = string.Empty;
    public string RequirementName { get; init; } = string.Empty;
    public int InventoryCandidateCount { get; init; }
    public int ReviewCandidateCount { get; init; }
    public List<CoreTokenReviewCandidate> Candidates { get; init; } = new();
}

public sealed class CoreTokenReviewCandidate
{
    public int CandidateNumber { get; init; }
    public string Source { get; init; } = string.Empty;
    public string NameEvidence { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string ReviewImageUrl { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}
