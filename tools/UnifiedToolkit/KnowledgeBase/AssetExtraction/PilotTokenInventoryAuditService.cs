using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class PilotTokenInventoryAuditService
{
    private static readonly HashSet<string> EpicShipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cr90 corvette", "gr-75 medium transport", "gr75 medium transport", "gozanti-class cruiser",
        "gozanti class cruiser", "c-roc cruiser", "c roc cruiser", "raider-class corvette",
        "raider class corvette", "imperial raider", "transport", "tantive iv"
    };

    public PilotTokenInventoryAuditResult Audit(string repositoryRoot, string? outputFolder = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var pilotsPath = FindRequiredFile(root, "pilots.js", new[]
        {
            Path.Combine(root, "source", "xwing-data", "data", "pilots.js"),
            Path.Combine(root, "assets", "source", "xwing-data", "data", "pilots.js"),
            Path.Combine(root, "..", "xwing-data", "data", "pilots.js")
        });
        var shipsPath = FindOptionalSiblingFile(pilotsPath, "ships.js");
        var xwingDataRoot = Directory.GetParent(Path.GetDirectoryName(pilotsPath)!)!.FullName;
        var pilotImagesRoot = FindRequiredDirectory(root, "xwing-data pilot images", new[]
        {
            Path.Combine(root, "assets", "source", "xwing-data", "images", "pilots"),
            Path.Combine(root, "source", "xwing-data", "images", "pilots"),
            Path.Combine(xwingDataRoot, "images", "pilots"),
            Path.Combine(root, "..", "xwing-data", "images", "pilots")
        });
        var generatedRoot = Path.Combine(root, "assets", "generated", "PilotBaseToken");
        var output = Path.GetFullPath(outputFolder ?? Path.Combine(root, "ukb", "reports", "pilot-token-inventory-audit"));

        Directory.CreateDirectory(output);

        var ships = LoadShips(shipsPath);
        var pilots = LoadPilots(pilotsPath);
        var cardFiles = Directory.EnumerateFiles(pilotImagesRoot, "*.png", SearchOption.AllDirectories).ToList();
        var tokenFiles = Directory.Exists(generatedRoot)
            ? Directory.EnumerateFiles(generatedRoot, "*.png", SearchOption.AllDirectories).ToList()
            : new List<string>();

        var tokenIndex = tokenFiles
            .GroupBy(GetGeneratedPilotKey)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<PilotTokenInventoryRow>();
        foreach (var pilot in pilots)
        {
            var isEpic = IsEpic(pilot.Ship, ships);
            var card = ResolveCardFile(root, xwingDataRoot, pilotImagesRoot, cardFiles, pilot);
            var candidates = ResolveGeneratedTokens(tokenIndex, pilot);
            var generated = ResolveBestGeneratedToken(root, candidates, pilot);
            var status = isEpic
                ? "ExcludedEpic"
                : generated is not null
                    ? "Generated"
                    : card is not null
                        ? "MissingTokenHasPilotCard"
                        : "MissingTokenAndPilotCard";

            rows.Add(new PilotTokenInventoryRow
            {
                PilotKey = BuildPilotKey(pilot),
                Name = pilot.Name,
                Xws = pilot.Xws,
                Faction = pilot.Faction,
                Ship = pilot.Ship,
                Skill = pilot.Skill,
                Points = pilot.Points,
                IsEpic = isEpic,
                PilotCardPath = card is null ? string.Empty : Relative(root, card),
                GeneratedTokenPath = generated is null ? string.Empty : Relative(root, generated),
                Status = status,
                GenerationDonorRecommendation = isEpic || generated is not null
                    ? string.Empty
                    : FindDonor(root, tokenFiles, pilot)
            });
        }

        var orphanCards = cardFiles
            .Where(file => !rows.Any(row => SamePath(row.PilotCardPath, Relative(root, file))))
            .Select(file => Relative(root, file))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orphanTokens = tokenFiles
            .Where(file => !rows.Any(row => SamePath(row.GeneratedTokenPath, Relative(root, file))))
            .Select(file => Relative(root, file))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var nienSearch = SearchRepositoryForPilot(root, "Nien Nunb");
        WriteCsv(Path.Combine(output, "pilot-token-inventory.csv"), rows);
        WriteJson(Path.Combine(output, "pilot-token-inventory.json"), rows);
        WriteLines(Path.Combine(output, "unmatched-pilot-card-files.txt"), orphanCards);
        WriteLines(Path.Combine(output, "unmatched-generated-token-files.txt"), orphanTokens);
        WriteCsv(Path.Combine(output, "nien-nunb-source-search.csv"), nienSearch);
        WriteSummary(Path.Combine(output, "summary.txt"), rows, cardFiles.Count, tokenFiles.Count, orphanCards.Count, orphanTokens.Count, nienSearch.Count);

        return new PilotTokenInventoryAuditResult
        {
            TotalPilotRecords = rows.Count,
            EpicPilotRecords = rows.Count(x => x.IsEpic),
            NonEpicPilotRecords = rows.Count(x => !x.IsEpic),
            GeneratedTokensMatched = rows.Count(x => !x.IsEpic && x.Status == "Generated"),
            MissingTokens = rows.Count(x => !x.IsEpic && x.Status.StartsWith("MissingToken", StringComparison.Ordinal)),
            PilotCardFiles = cardFiles.Count,
            GeneratedTokenFiles = tokenFiles.Count,
            UnmatchedPilotCardFiles = orphanCards.Count,
            UnmatchedGeneratedTokenFiles = orphanTokens.Count,
            NienNunbSearchHits = nienSearch.Count,
            OutputFolder = output,
            InventoryCsv = Path.Combine(output, "pilot-token-inventory.csv"),
            NienNunbSearchCsv = Path.Combine(output, "nien-nunb-source-search.csv")
        };
    }


    private static string FindRequiredFile(string repositoryRoot, string description, IEnumerable<string> candidates)
    {
        var attempted = candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var found = attempted.FirstOrDefault(File.Exists);
        if (found is not null) return found;
        throw new FileNotFoundException($"Could not find {description}. Checked: {string.Join("; ", attempted)}");
    }

    private static string FindOptionalSiblingFile(string requiredFile, string siblingName)
    {
        var candidate = Path.Combine(Path.GetDirectoryName(requiredFile)!, siblingName);
        return File.Exists(candidate) ? candidate : string.Empty;
    }

    private static string FindRequiredDirectory(string repositoryRoot, string description, IEnumerable<string> candidates)
    {
        var attempted = candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var found = attempted.FirstOrDefault(Directory.Exists);
        if (found is not null) return found;
        throw new DirectoryNotFoundException($"Could not find {description}. Checked: {string.Join("; ", attempted)}");
    }

    private static List<PilotRecord> LoadPilots(string path)
    {
        using var doc = JsonDocument.Parse(ReadJsonArray(path), JsonOptions());
        return doc.RootElement.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => new PilotRecord
        {
            Name = ReadString(x, "name"), Xws = ReadString(x, "xws"), Ship = ReadString(x, "ship"),
            Faction = ReadString(x, "faction"), Image = ReadString(x, "image"), Skill = ReadInt(x, "skill"), Points = ReadInt(x, "points")
        }).Where(x => x.Name.Length > 0).ToList();
    }

    private static Dictionary<string, string> LoadShips(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        using var doc = JsonDocument.Parse(ReadJsonArray(path), JsonOptions());
        foreach (var item in doc.RootElement.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
        {
            var name = ReadString(item, "name");
            var xws = ReadString(item, "xws");
            var size = ReadString(item, "size");
            if (name.Length > 0) result[Normalise(name)] = size;
            if (xws.Length > 0) result[Normalise(xws)] = size;
        }
        return result;
    }

    private static bool IsEpic(string ship, IReadOnlyDictionary<string, string> ships)
    {
        var compact = Normalise(ship);
        if (ships.TryGetValue(compact, out var size) && (size.Contains("huge", StringComparison.OrdinalIgnoreCase) || size.Contains("epic", StringComparison.OrdinalIgnoreCase))) return true;
        return EpicShipNames.Any(name => compact == Normalise(name) || compact.Contains(Normalise(name), StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveCardFile(string root, string xwingDataRoot, string imagesRoot, IReadOnlyList<string> files, PilotRecord pilot)
    {
        if (pilot.Image.Length > 0)
        {
            var image = pilot.Image.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            foreach (var candidate in new[]
            {
                Path.Combine(xwingDataRoot, image),
                Path.Combine(root, "assets", "source", "xwing-data", image),
                Path.Combine(root, "source", "xwing-data", image),
                Path.Combine(imagesRoot, image),
                Path.Combine(imagesRoot, Path.GetFileName(image))
            })
                if (File.Exists(candidate)) return candidate;
        }
        var key = Normalise(pilot.Xws.Length > 0 ? pilot.Xws : pilot.Name);
        return files.FirstOrDefault(file => Normalise(Path.GetFileNameWithoutExtension(file)) == key);
    }

    private static List<string> ResolveGeneratedTokens(IReadOnlyDictionary<string, List<string>> index, PilotRecord pilot)
    {
        var keys = new[] { pilot.Xws, pilot.Name }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalise)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return keys
            .SelectMany(key => index.TryGetValue(key, out var files) ? files : Enumerable.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolveBestGeneratedToken(string root, IReadOnlyList<string> candidates, PilotRecord pilot)
    {
        if (candidates.Count == 0) return null;

        var expectedFaction = Normalise(pilot.Faction);
        var expectedShip = Normalise(pilot.Ship);

        // Generated token paths are authoritative identity evidence:
        // assets/generated/PilotBaseToken/<faction>/<ship>/<pilot>__pilot-<hash>.png
        // A same-name token from another faction or ship must never be reused.
        var exactIdentityMatches = candidates
            .Where(path => GeneratedPathMatchesIdentity(path, expectedFaction, expectedShip))
            .ToList();

        if (exactIdentityMatches.Count == 0) return null;
        if (exactIdentityMatches.Count == 1) return exactIdentityMatches[0];

        return exactIdentityMatches
            .OrderBy(path => Relative(root, path), StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static bool GeneratedPathMatchesIdentity(string path, string expectedFaction, string expectedShip)
    {
        var shipFolder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
        var factionFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path) ?? string.Empty) ?? string.Empty);
        return Normalise(factionFolder) == expectedFaction && Normalise(shipFolder) == expectedShip;
    }

    private static string FindDonor(string root, IEnumerable<string> tokens, PilotRecord pilot)
    {
        var ship = Normalise(pilot.Ship); var faction = Normalise(pilot.Faction);
        var sameShip = tokens.Where(path => Normalise(Relative(root, path)).Contains(ship)).ToList();
        var donor = sameShip.FirstOrDefault(path => Normalise(Relative(root, path)).Contains(faction)) ?? sameShip.FirstOrDefault();
        return donor is null ? string.Empty : Relative(root, donor);
    }

    private static List<SourceSearchHit> SearchRepositoryForPilot(string root, string pilotName)
    {
        var terms = new[] { pilotName, pilotName.Replace(" ", ""), pilotName.Replace(" ", "-"), pilotName.Replace(" ", "_") };
        var hits = new List<SourceSearchHit>();
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".csv", ".txt", ".js", ".lua", ".html", ".xml", ".md" };
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var fileNameMatch = terms.Any(term => Path.GetFileName(file).Contains(term, StringComparison.OrdinalIgnoreCase));
            if (fileNameMatch) hits.Add(new SourceSearchHit { Path = Relative(root, file), MatchType = "FileName", Evidence = Path.GetFileName(file) });
            if (!extensions.Contains(Path.GetExtension(file))) continue;
            try
            {
                var info = new FileInfo(file); if (info.Length > 150_000_000) continue;
                var text = File.ReadAllText(file);
                foreach (var term in terms)
                {
                    var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) continue;
                    var start = Math.Max(0, index - 160); var length = Math.Min(420, text.Length - start);
                    hits.Add(new SourceSearchHit { Path = Relative(root, file), MatchType = "Content", Evidence = Regex.Replace(text.Substring(start, length), "\\s+", " ").Trim() });
                    break;
                }
            }
            catch { }
        }
        return hits.DistinctBy(x => (x.Path, x.MatchType, x.Evidence)).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetGeneratedPilotKey(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);

        // Extracted token names use the form:
        //     pilotname__pilot-0123456789ab.png
        // Earlier revisions also used --pilot- and -pilot-, so accept all
        // three separators. The previous implementation did not recognise
        // the current double-underscore form and therefore matched zero files.
        var withoutExtractionSuffix = Regex.Replace(
            fileName,
            @"(?:__|--|-)?pilot-[0-9a-f]+$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Normalise(withoutExtractionSuffix);
    }

    private static string BuildPilotKey(PilotRecord p) => $"{Normalise(p.Faction)}|{Normalise(p.Ship)}|{Normalise(p.Xws.Length > 0 ? p.Xws : p.Name)}|{p.Skill}|{p.Points}";
    private static string Normalise(string value) => Regex.Replace(value ?? string.Empty, "[^a-z0-9]+", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    private static bool SamePath(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static JsonDocumentOptions JsonOptions() => new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static string ReadJsonArray(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8).Trim();
        var start = text.IndexOf('['); var end = text.LastIndexOf(']');
        if (start < 0 || end < start) throw new InvalidDataException($"Could not locate a JSON array in '{path}'.");
        return text.Substring(start, end - start + 1);
    }
    private static string ReadString(JsonElement e, string name) => TryProperty(e, name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString() : "";
    private static int? ReadInt(JsonElement e, string name) { if (!TryProperty(e, name, out var v)) return null; return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : int.TryParse(v.ToString(), out n) ? n : null; }
    private static bool TryProperty(JsonElement e, string name, out JsonElement value) { if (e.TryGetProperty(name, out value)) return true; foreach (var p in e.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; } value = default; return false; }
    private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    private static void WriteLines(string path, IEnumerable<string> values) => File.WriteAllLines(path, values, new UTF8Encoding(false));
    private static string Csv(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    private static void WriteCsv(string path, IEnumerable<PilotTokenInventoryRow> rows)
    {
        var lines = new List<string> { "PilotKey,Name,Xws,Faction,Ship,Skill,Points,IsEpic,Status,PilotCardPath,GeneratedTokenPath,GenerationDonorRecommendation" };
        lines.AddRange(rows.Select(x => string.Join(',', Csv(x.PilotKey), Csv(x.Name), Csv(x.Xws), Csv(x.Faction), Csv(x.Ship), x.Skill?.ToString() ?? "", x.Points?.ToString() ?? "", x.IsEpic, Csv(x.Status), Csv(x.PilotCardPath), Csv(x.GeneratedTokenPath), Csv(x.GenerationDonorRecommendation))));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
    private static void WriteCsv(string path, IEnumerable<SourceSearchHit> rows)
    {
        var lines = new List<string> { "Path,MatchType,Evidence" };
        lines.AddRange(rows.Select(x => string.Join(',', Csv(x.Path), Csv(x.MatchType), Csv(x.Evidence))));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
    private static void WriteSummary(string path, IReadOnlyList<PilotTokenInventoryRow> rows, int cards, int tokens, int orphanCards, int orphanTokens, int nienHits)
    {
        var nonEpic = rows.Where(x => !x.IsEpic).ToList();
        File.WriteAllLines(path, new[]
        {
            $"Pilot records: {rows.Count}", $"Epic excluded: {rows.Count(x => x.IsEpic)}", $"Non-Epic target: {nonEpic.Count}",
            $"Generated matched: {nonEpic.Count(x => x.Status == "Generated")}", $"Missing tokens: {nonEpic.Count(x => x.Status.StartsWith("MissingToken", StringComparison.Ordinal))}",
            $"Pilot card PNG files: {cards}", $"Generated token PNG files: {tokens}", $"Unmatched pilot card files: {orphanCards}",
            $"Unmatched generated token files: {orphanTokens}", $"Nien Nunb source-search hits: {nienHits}"
        }, new UTF8Encoding(false));
    }

    private sealed class PilotRecord { public string Name { get; init; } = ""; public string Xws { get; init; } = ""; public string Ship { get; init; } = ""; public string Faction { get; init; } = ""; public string Image { get; init; } = ""; public int? Skill { get; init; } public int? Points { get; init; } }
}

public sealed class PilotTokenInventoryRow
{
    public string PilotKey { get; init; } = ""; public string Name { get; init; } = ""; public string Xws { get; init; } = ""; public string Faction { get; init; } = ""; public string Ship { get; init; } = "";
    public int? Skill { get; init; } public int? Points { get; init; } public bool IsEpic { get; init; } public string Status { get; init; } = ""; public string PilotCardPath { get; init; } = ""; public string GeneratedTokenPath { get; init; } = ""; public string GenerationDonorRecommendation { get; init; } = "";
}
public sealed class SourceSearchHit { public string Path { get; init; } = ""; public string MatchType { get; init; } = ""; public string Evidence { get; init; } = ""; }
public sealed class PilotTokenInventoryAuditResult
{
    public int TotalPilotRecords { get; init; } public int EpicPilotRecords { get; init; } public int NonEpicPilotRecords { get; init; } public int GeneratedTokensMatched { get; init; } public int MissingTokens { get; init; }
    public int PilotCardFiles { get; init; } public int GeneratedTokenFiles { get; init; } public int UnmatchedPilotCardFiles { get; init; } public int UnmatchedGeneratedTokenFiles { get; init; } public int NienNunbSearchHits { get; init; }
    public string OutputFolder { get; init; } = ""; public string InventoryCsv { get; init; } = ""; public string NienNunbSearchCsv { get; init; } = "";
}
