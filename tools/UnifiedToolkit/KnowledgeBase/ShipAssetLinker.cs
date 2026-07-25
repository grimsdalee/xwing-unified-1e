using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.KnowledgeBase;

public sealed class ShipAssetLinker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] Roles =
    {
        "ShipModel", "ShipTexture", "BaseToken", "DialTexture", "DialModel", "ShipScript"
    };

    public ShipAssetLinkResult Link(string repositoryRoot, string? shipsFile = null, string? outputFolder = null, int candidatesPerRole = 8)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var ukbPath = Path.Combine(repositoryRoot, "ukb", "knowledge-base.json");
        if (!File.Exists(ukbPath))
            throw new FileNotFoundException("Knowledge base not found. Run build-knowledge-base first.", ukbPath);

        shipsFile ??= FindShipsFile(repositoryRoot);
        if (!File.Exists(shipsFile))
            throw new FileNotFoundException("First Edition ships.json was not found. Use --ships <file>.", shipsFile);

        var ukb = Read<UnifiedKnowledgeBase>(ukbPath);
        var ships = Read<List<FirstEditionShipRecord>>(shipsFile);
        var linkedShips = new List<KnowledgeBaseShip>();

        foreach (var ship in ships.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var aliases = BuildAliases(ship);
            var roleLinks = new List<KnowledgeBaseShipAssetRole>();

            foreach (var role in Roles)
            {
                var candidates = ukb.Domains.Assets
                    .Where(asset => IsEligible(asset, role))
                    .Select(asset => Score(asset, role, aliases))
                    .Where(candidate => candidate.Score >= 35)
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                    .Take(Math.Max(1, candidatesPerRole))
                    .ToList();

                roleLinks.Add(new KnowledgeBaseShipAssetRole
                {
                    Role = role,
                    Required = role is "ShipModel" or "ShipTexture" or "BaseToken" or "DialTexture",
                    Status = Classify(candidates),
                    Candidates = candidates
                });
            }

            linkedShips.Add(new KnowledgeBaseShip
            {
                ShipId = $"SHIP-{StableId(ship.TargetId)}",
                SourceId = ship.SourceId,
                TargetId = ship.TargetId,
                Name = ship.Name,
                BaseSize = NormalizeBaseSize(ship.Size),
                Factions = ship.Factions,
                AssetRoles = roleLinks
            });
        }

        ukb.Domains.Ships.Clear();
        ukb.Domains.Ships.AddRange(linkedShips);
        foreach (var asset in ukb.Domains.Assets)
            asset.ReferencedBy.RemoveAll(reference => reference.EntityType.Equals("ship", StringComparison.OrdinalIgnoreCase));

        foreach (var ship in linkedShips)
        {
            foreach (var role in ship.AssetRoles)
            {
                foreach (var candidate in role.Candidates)
                {
                    var asset = ukb.Domains.Assets.First(item =>
                        item.AssetId.Equals(candidate.AssetId, StringComparison.OrdinalIgnoreCase) &&
                        item.RepositoryPath.Equals(candidate.RepositoryPath, StringComparison.OrdinalIgnoreCase));
                    asset.ReferencedBy.Add(new KnowledgeBaseEntityReference
                    {
                        EntityType = "ship",
                        EntityId = ship.ShipId,
                        Role = $"candidate:{role.Role}:{candidate.Score}"
                    });
                }
            }
        }

        var outputRoot = outputFolder is null ? Path.Combine(repositoryRoot, "ukb") : Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(Path.Combine(outputRoot, "reports"));

        Write(Path.Combine(outputRoot, "knowledge-base.json"), ukb);
        Write(Path.Combine(outputRoot, "ship-links.json"), new KnowledgeBaseShipDomain
        {
            SchemaVersion = "1.1.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            Ships = linkedShips
        });
        WriteCsv(Path.Combine(outputRoot, "reports", "ship-link-review.csv"), linkedShips);
        WriteMarkdown(Path.Combine(outputRoot, "reports", "SHIP-LINK-SUMMARY.md"), linkedShips);

        return new ShipAssetLinkResult
        {
            Ships = linkedShips.Count,
            CandidateLinks = linkedShips.Sum(ship => ship.AssetRoles.Sum(role => role.Candidates.Count)),
            ClearRoles = linkedShips.Sum(ship => ship.AssetRoles.Count(role => role.Status == "clear")),
            ReviewRoles = linkedShips.Sum(ship => ship.AssetRoles.Count(role => role.Status == "review")),
            MissingRequiredRoles = linkedShips.Sum(ship => ship.AssetRoles.Count(role => role.Required && role.Candidates.Count == 0)),
            OutputRoot = outputRoot
        };
    }

    private static string FindShipsFile(string repositoryRoot)
    {
        // The live repository mapping file must be authoritative.
        //
        // AppContext.BaseDirectory contains a build-output copy of ConversionData.
        // That copy can be stale when mappings are changed and conversion is rerun
        // without rebuilding the toolkit. It is retained only as a final fallback.
        var candidates = new[]
        {
            Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition", "ships.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "ConversionData", "first-edition", "ships.json"),
            Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition", "ships.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static List<string> BuildAliases(FirstEditionShipRecord ship)
    {
        var aliases = new[] { ship.SourceId, ship.TargetId, ship.Name }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => new[] { Normalize(value), Compact(value) })
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var word in Regex.Split(ship.Name ?? string.Empty, "[^A-Za-z0-9]+"))
        {
            var normalized = Normalize(word);
            if (normalized.Length >= 4 && !StopWords.Contains(normalized)) aliases.Add(normalized);
        }
        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    { "class", "fighter", "assault", "light", "freighter", "star", "wing", "ship" };

    private static KnowledgeBaseShipAssetCandidate Score(KnowledgeBaseAsset asset, string role, IReadOnlyCollection<string> aliases)
    {
        var path = Normalize(asset.RepositoryPath);
        var compactPath = Compact(path);
        var score = 0;
        var reasons = new List<string>();

        foreach (var alias in aliases.OrderByDescending(item => item.Length))
        {
            if (alias.Length < 3) continue;
            if (compactPath.Contains(Compact(alias), StringComparison.OrdinalIgnoreCase))
            {
                var points = alias.Length >= 10 ? 70 : alias.Length >= 6 ? 55 : 35;
                if (points > score) { score = points; reasons.Add($"ship alias '{alias}' in path"); }
            }
        }

        if (path.Contains("/ships-v2/", StringComparison.OrdinalIgnoreCase)) { score += 8; reasons.Add("ships-v2 location"); }
        else if (path.Contains("/ships/", StringComparison.OrdinalIgnoreCase)) { score += 6; reasons.Add("ships location"); }

        var roleBonus = RoleBonus(path, asset.Extension, role);
        score += roleBonus.Points;
        if (roleBonus.Points > 0) reasons.Add(roleBonus.Reason);
        if (asset.Warehouse.Equals("legacy1e", StringComparison.OrdinalIgnoreCase) && (role is "BaseToken" or "DialTexture"))
        { score += 12; reasons.Add("First Edition warehouse preference"); }

        return new KnowledgeBaseShipAssetCandidate
        {
            AssetId = asset.AssetId,
            RepositoryPath = asset.RepositoryPath,
            Warehouse = asset.Warehouse,
            Score = Math.Min(100, score),
            Confidence = score >= 85 ? "high" : score >= 60 ? "medium" : "low",
            Reasons = reasons.Distinct().ToList()
        };
    }

    private static (int Points, string Reason) RoleBonus(string path, string extension, string role) => role switch
    {
        "ShipModel" when extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) => (25, "OBJ model"),
        "ShipTexture" when IsImage(extension) && (path.Contains("texture") || path.Contains("/ships")) => (22, "ship image/texture"),
        "BaseToken" when IsImage(extension) && (path.Contains("token") || path.Contains("base")) => (28, "token/base image"),
        "DialTexture" when IsImage(extension) && path.Contains("dial") => (30, "dial image"),
        "DialModel" when extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) && path.Contains("dial") => (30, "dial model"),
        "ShipScript" when extension.Equals(".lua", StringComparison.OrdinalIgnoreCase) => (20, "Lua script"),
        _ => (0, string.Empty)
    };

    private static bool IsEligible(KnowledgeBaseAsset asset, string role) => role switch
    {
        "ShipModel" or "DialModel" => asset.Extension.Equals(".obj", StringComparison.OrdinalIgnoreCase),
        "ShipTexture" or "BaseToken" or "DialTexture" => IsImage(asset.Extension),
        "ShipScript" => asset.Extension.Equals(".lua", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static bool IsImage(string extension) => extension is ".png" or ".jpg" or ".jpeg" or ".webp";
    private static string Classify(IReadOnlyList<KnowledgeBaseShipAssetCandidate> candidates) =>
        candidates.Count == 0 ? "missing" : candidates[0].Score >= 85 && (candidates.Count == 1 || candidates[0].Score - candidates[1].Score >= 12) ? "clear" : "review";
    private static string NormalizeBaseSize(string value) => value.Equals("medium", StringComparison.OrdinalIgnoreCase) ? "large" : value.Equals("huge", StringComparison.OrdinalIgnoreCase) ? "epic" : value.ToLowerInvariant();
    private static string Normalize(string value) => value.Replace('\\', '/').ToLowerInvariant();
    private static string Compact(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", string.Empty);
    private static string StableId(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException($"Could not parse {path}");
    private static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));

    private static void WriteCsv(string path, IEnumerable<KnowledgeBaseShip> ships)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("ShipId,TargetId,ShipName,BaseSize,Role,Required,Status,Rank,Score,Confidence,Warehouse,AssetId,RepositoryPath,Reasons");
        foreach (var ship in ships)
        foreach (var role in ship.AssetRoles)
        {
            if (role.Candidates.Count == 0)
                writer.WriteLine(string.Join(',', Csv(ship.ShipId), Csv(ship.TargetId), Csv(ship.Name), Csv(ship.BaseSize), Csv(role.Role), role.Required, Csv(role.Status), "", "", "", "", "", "", ""));
            for (var i = 0; i < role.Candidates.Count; i++)
            {
                var c = role.Candidates[i];
                writer.WriteLine(string.Join(',', Csv(ship.ShipId), Csv(ship.TargetId), Csv(ship.Name), Csv(ship.BaseSize), Csv(role.Role), role.Required, Csv(role.Status), i + 1, c.Score, Csv(c.Confidence), Csv(c.Warehouse), Csv(c.AssetId), Csv(c.RepositoryPath), Csv(string.Join("; ", c.Reasons))));
            }
        }
    }

    private static void WriteMarkdown(string path, IReadOnlyCollection<KnowledgeBaseShip> ships)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Ship Asset Link Summary\n");
        writer.WriteLine($"Ships: **{ships.Count}**  ");
        writer.WriteLine($"Candidate links: **{ships.Sum(ship => ship.AssetRoles.Sum(role => role.Candidates.Count))}**  ");
        writer.WriteLine($"Clear role matches: **{ships.Sum(ship => ship.AssetRoles.Count(role => role.Status == "clear"))}**  ");
        writer.WriteLine($"Missing required roles: **{ships.Sum(ship => ship.AssetRoles.Count(role => role.Required && role.Candidates.Count == 0))}**");
        writer.WriteLine();
        writer.WriteLine("No candidate is approved by this command. Review `ship-link-review.csv` first.");
    }

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
