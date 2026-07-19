using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class PilotSheetExplorerResult
{
    public int CandidateImages { get; init; }
    public int KnownSourceSheets { get; init; }
    public int KnownPilotCrops { get; init; }
    public int MissingPilots { get; init; }
    public int DuplicateImagesSkipped { get; init; }
    public string OutputFolder { get; init; } = string.Empty;
    public string HtmlFile { get; init; } = string.Empty;
    public string CatalogueFile { get; init; } = string.Empty;
}

public sealed class PilotSheetExplorerCatalogue
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string InventoryFile { get; init; } = string.Empty;
    public string ExtractionPlanFile { get; init; } = string.Empty;
    public List<PilotSheetExplorerImage> Images { get; init; } = new();
    public List<PilotSheetExplorerPilot> MissingPilots { get; init; } = new();
}

public sealed class PilotSheetExplorerImage
{
    public string ImageId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string BrowserPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public long Bytes { get; init; }
    public bool IsKnownSourceSheet { get; set; }
    public int KnownCropCount { get; set; }
    public int PriorityScore { get; set; }
    public List<PilotSheetExplorerKnownCrop> KnownCrops { get; init; } = new();
}

public sealed class PilotSheetExplorerKnownCrop
{
    public string PilotId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string Ship { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed class PilotSheetExplorerPilot
{
    public string PilotId { get; init; } = string.Empty;
    public string Xws { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string Ship { get; init; } = string.Empty;
    public int Skill { get; init; }
    public int Points { get; init; }
    public string Status { get; init; } = string.Empty;
    public string DonorRecommendation { get; init; } = string.Empty;
}

public sealed class PilotSheetExplorerService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp"
    };

    public PilotSheetExplorerResult Prepare(
        string repositoryRoot,
        string? inventoryFile = null,
        string? completedPlanFile = null,
        string? outputFolder = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Repository folder was not found: {root}");

        var inventory = ResolveRequiredFile(root, inventoryFile,
            Path.Combine(root, "ukb", "reports", "pilot-token-inventory-audit", "pilot-token-inventory.csv"),
            "pilot-token inventory report");

        var plan = ResolveRequiredFile(root, completedPlanFile,
            Path.Combine(root, "ukb", "extraction-layouts", "pilot-token-extraction-plan.v2.completed.json"),
            "completed pilot-token extraction plan");

        var legacyRoot = Path.Combine(root, "assets", "source", "legacy1e");
        if (!Directory.Exists(legacyRoot))
            throw new DirectoryNotFoundException($"Imported legacy asset folder was not found: {legacyRoot}");

        var output = Path.GetFullPath(outputFolder ?? Path.Combine(root, "ukb", "reports", "pilot-sheet-explorer"));
        Directory.CreateDirectory(output);

        var extractionPlan = ShipAssetJson.Read<AssetExtractionPlan>(plan);
        var knownByPath = BuildKnownCropIndex(extractionPlan);
        var missingPilots = ReadMissingPilots(inventory);
        var (images, duplicateCount) = IndexImages(root, output, legacyRoot, knownByPath);

        var catalogue = new PilotSheetExplorerCatalogue
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = root,
            InventoryFile = Relative(root, inventory),
            ExtractionPlanFile = Relative(root, plan),
            Images = images
                .OrderByDescending(x => x.PriorityScore)
                .ThenByDescending(x => x.IsKnownSourceSheet)
                .ThenBy(x => x.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MissingPilots = missingPilots
                .OrderBy(x => x.Faction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Ship, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        var catalogueFile = Path.Combine(output, "pilot-sheet-catalogue.json");
        File.WriteAllText(catalogueFile, JsonSerializer.Serialize(catalogue, JsonOptions()), Encoding.UTF8);
        File.WriteAllText(Path.Combine(output, "explorer-data.js"),
            "window.PILOT_SHEET_EXPLORER_DATA = " + JsonSerializer.Serialize(catalogue, JsonOptions()) + ";\n",
            Encoding.UTF8);

        CopyExplorerAssets(output);
        WriteCandidateCsv(Path.Combine(output, "pilot-sheet-candidates.csv"), catalogue.Images);
        WriteMissingCsv(Path.Combine(output, "missing-pilots.csv"), catalogue.MissingPilots);
        WriteSummary(Path.Combine(output, "summary.txt"), catalogue, duplicateCount);

        return new PilotSheetExplorerResult
        {
            CandidateImages = catalogue.Images.Count,
            KnownSourceSheets = catalogue.Images.Count(x => x.IsKnownSourceSheet),
            KnownPilotCrops = catalogue.Images.Sum(x => x.KnownCropCount),
            MissingPilots = catalogue.MissingPilots.Count,
            DuplicateImagesSkipped = duplicateCount,
            OutputFolder = output,
            HtmlFile = Path.Combine(output, "index.html"),
            CatalogueFile = catalogueFile
        };
    }

    private static Dictionary<string, List<PilotSheetExplorerKnownCrop>> BuildKnownCropIndex(AssetExtractionPlan plan)
    {
        var result = new Dictionary<string, List<PilotSheetExplorerKnownCrop>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in plan.Sheets)
        {
            var key = NormalisePath(sheet.RepositoryPath);
            if (!result.TryGetValue(key, out var crops))
            {
                crops = new List<PilotSheetExplorerKnownCrop>();
                result[key] = crops;
            }

            foreach (var entry in sheet.Entries)
            {
                if (entry.CropX is null || entry.CropY is null || entry.CropWidth is null || entry.CropHeight is null)
                    continue;

                crops.Add(new PilotSheetExplorerKnownCrop
                {
                    PilotId = entry.EntityId,
                    DisplayName = entry.DisplayName,
                    Faction = entry.Faction,
                    Ship = entry.ShipId,
                    X = entry.CropX.Value,
                    Y = entry.CropY.Value,
                    Width = entry.CropWidth.Value,
                    Height = entry.CropHeight.Value
                });
            }
        }
        return result;
    }

    private static (List<PilotSheetExplorerImage> Images, int Duplicates) IndexImages(
        string root,
        string reportFolder,
        string legacyRoot,
        IReadOnlyDictionary<string, List<PilotSheetExplorerKnownCrop>> knownByPath)
    {
        var images = new List<PilotSheetExplorerImage>();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;
        var sequence = 0;

        foreach (var file in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories)
                     .Where(x => ImageExtensions.Contains(Path.GetExtension(x))))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length == 0) continue;

                var hash = CalculateSha256(file);
                if (!hashes.Add(hash))
                {
                    duplicates++;
                    continue;
                }

                using var stream = File.OpenRead(file);
                using var codec = SKCodec.Create(stream);
                if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) continue;

                var repositoryPath = Relative(root, file);
                knownByPath.TryGetValue(NormalisePath(repositoryPath), out var knownCrops);
                knownCrops ??= new List<PilotSheetExplorerKnownCrop>();

                var score = CalculatePriority(repositoryPath, codec.Info.Width, codec.Info.Height, knownCrops.Count);
                images.Add(new PilotSheetExplorerImage
                {
                    ImageId = $"legacy-image-{++sequence:0000}",
                    RepositoryPath = repositoryPath,
                    BrowserPath = MakeBrowserRelative(reportFolder, file),
                    FileName = Path.GetFileName(file),
                    Sha256 = hash,
                    Width = codec.Info.Width,
                    Height = codec.Info.Height,
                    Bytes = info.Length,
                    IsKnownSourceSheet = knownCrops.Count > 0,
                    KnownCropCount = knownCrops.Count,
                    PriorityScore = score,
                    KnownCrops = knownCrops
                });
            }
            catch
            {
                // Imported legacy folders can include corrupt or unsupported images. They are omitted from the visual catalogue.
            }
        }

        return (images, duplicates);
    }

    private static int CalculatePriority(string path, int width, int height, int knownCropCount)
    {
        var normalised = path.ToLowerInvariant();
        var score = knownCropCount > 0 ? 1000 : 0;
        if (normalised.Contains("pilot")) score += 250;
        if (normalised.Contains("token")) score += 220;
        if (normalised.Contains("ship")) score += 80;
        if (width >= 512 && height >= 512) score += 50;
        if (width == height) score += 20;
        if (width > height * 4 || height > width * 4) score -= 80;
        return score;
    }

    private static List<PilotSheetExplorerPilot> ReadMissingPilots(string csvFile)
    {
        var rows = ReadCsv(csvFile);
        if (rows.Count == 0) return new List<PilotSheetExplorerPilot>();

        var headers = rows[0];
        var result = new List<PilotSheetExplorerPilot>();
        foreach (var values in rows.Skip(1))
        {
            string Get(params string[] names)
            {
                foreach (var name in names)
                {
                    var index = headers.FindIndex(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0 && index < values.Count) return values[index];
                }
                return string.Empty;
            }

            var status = Get("Status");
            var isEpicText = Get("IsEpic");
            var isEpic = bool.TryParse(isEpicText, out var parsedEpic) && parsedEpic;
            if (isEpic || !status.StartsWith("MissingToken", StringComparison.OrdinalIgnoreCase)) continue;

            var name = Get("PilotName", "Name", "DisplayName");
            var xws = Get("PilotXws", "Xws", "TargetId");
            var ship = Get("Ship", "ShipName", "ShipId");
            var faction = Get("Faction");
            result.Add(new PilotSheetExplorerPilot
            {
                PilotId = BuildPilotId(faction, ship, xws.Length > 0 ? xws : name),
                Xws = xws,
                DisplayName = name,
                Faction = faction,
                Ship = ship,
                Skill = ParseInt(Get("Skill", "PilotSkill")),
                Points = ParseInt(Get("Points", "SquadPointCost")),
                Status = status,
                DonorRecommendation = Get("GenerationDonorRecommendation", "DonorRecommendation")
            });
        }

        return result.DistinctBy(x => x.PilotId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<List<string>> ReadCsv(string path)
    {
        var rows = new List<List<string>>();
        foreach (var line in File.ReadLines(path))
        {
            var row = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    row.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }
            row.Add(current.ToString());
            rows.Add(row);
        }
        return rows;
    }

    private static void CopyExplorerAssets(string output)
    {
        var source = LocateExplorerAssets();
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(output, Path.GetFileName(file)), true);
    }

    private static string LocateExplorerAssets()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "KnowledgeBase", "AssetExtraction", "ExplorerAssets"),
            Path.Combine(Directory.GetCurrentDirectory(), "KnowledgeBase", "AssetExtraction", "ExplorerAssets")
        };
        return candidates.FirstOrDefault(Directory.Exists)
               ?? throw new DirectoryNotFoundException("Pilot Sheet Explorer UI assets were not found.");
    }

    private static string ResolveRequiredFile(string root, string? supplied, string defaultPath, string description)
    {
        var path = Path.GetFullPath(supplied ?? defaultPath);
        if (!File.Exists(path)) throw new FileNotFoundException($"The {description} was not found.", path);
        return path;
    }

    private static string CalculateSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string MakeBrowserRelative(string reportFolder, string file) =>
        Path.GetRelativePath(reportFolder, file).Replace('\\', '/').Split('/').Select(Uri.EscapeDataString).Aggregate((a, b) => a + "/" + b);

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string NormalisePath(string path) => path.Replace('\\', '/').TrimStart('/');
    private static int ParseInt(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static string BuildPilotId(string faction, string ship, string pilot) => $"{Slug(faction)}::{Slug(ship)}::{Slug(pilot)}";
    private static string Slug(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

    private static void WriteCandidateCsv(string path, IEnumerable<PilotSheetExplorerImage> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("ImageId,RepositoryPath,Width,Height,Bytes,Sha256,IsKnownSourceSheet,KnownCropCount,PriorityScore");
        foreach (var row in rows)
            writer.WriteLine(string.Join(',', new[] { Csv(row.ImageId), Csv(row.RepositoryPath), row.Width.ToString(CultureInfo.InvariantCulture), row.Height.ToString(CultureInfo.InvariantCulture), row.Bytes.ToString(CultureInfo.InvariantCulture), Csv(row.Sha256), row.IsKnownSourceSheet.ToString(), row.KnownCropCount.ToString(CultureInfo.InvariantCulture), row.PriorityScore.ToString(CultureInfo.InvariantCulture) }));
    }

    private static void WriteMissingCsv(string path, IEnumerable<PilotSheetExplorerPilot> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("PilotId,DisplayName,Xws,Faction,Ship,Skill,Points,Status,DonorRecommendation");
        foreach (var row in rows)
            writer.WriteLine(string.Join(',', new[] { Csv(row.PilotId), Csv(row.DisplayName), Csv(row.Xws), Csv(row.Faction), Csv(row.Ship), row.Skill.ToString(CultureInfo.InvariantCulture), row.Points.ToString(CultureInfo.InvariantCulture), Csv(row.Status), Csv(row.DonorRecommendation) }));
    }

    private static void WriteSummary(string path, PilotSheetExplorerCatalogue catalogue, int duplicates)
    {
        File.WriteAllLines(path, new[]
        {
            "Pilot Sheet Explorer catalogue",
            "==============================",
            $"Generated UTC:          {catalogue.GeneratedUtc:O}",
            $"Candidate images:       {catalogue.Images.Count}",
            $"Known source sheets:    {catalogue.Images.Count(x => x.IsKnownSourceSheet)}",
            $"Known pilot crops:      {catalogue.Images.Sum(x => x.KnownCropCount)}",
            $"Missing pilots:         {catalogue.MissingPilots.Count}",
            $"Duplicate images omitted: {duplicates}",
            "",
            "This catalogue is for source recovery. Browser assignments are exported as pilot-token-source-recovery-plan.json."
        }, Encoding.UTF8);
    }
}
