using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using UnifiedToolkit.KnowledgeBase;
using UnifiedToolkit.RepositoryAssets;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class GeneratedPilotTokenImportResult
{
    public int ImagesScanned { get; init; }
    public int Imported { get; init; }
    public int Updated { get; init; }
    public int Warnings { get; init; }
    public int Errors { get; init; }
    public string OutputFolder { get; init; } = string.Empty;
    public string ManifestFile { get; init; } = string.Empty;
    public string ReportFile { get; init; } = string.Empty;
    public string AssetManifestRoot { get; init; } = string.Empty;
    public string KnowledgeBaseRoot { get; init; } = string.Empty;
}

public sealed class GeneratedPilotTokenImportManifest
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceRoot { get; init; } = string.Empty;
    public string DestinationRoot { get; init; } = string.Empty;
    public List<GeneratedPilotTokenImportEntry> Tokens { get; init; } = new();
}

public sealed class GeneratedPilotTokenImportEntry
{
    public string Status { get; init; } = string.Empty;
    public string PilotId { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string Ship { get; init; } = string.Empty;
    public string Pilot { get; init; } = string.Empty;
    public string SourceRepositoryPath { get; init; } = string.Empty;
    public string OutputRepositoryPath { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class GeneratedPilotTokenImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GeneratedPilotTokenImportResult Import(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Repository folder was not found: {root}");

        var sourceRoot = Path.Combine(root, "assets", "source", "generated-pilot-tokens");
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Generated pilot token source folder was not found: {sourceRoot}");

        var destinationRoot = Path.Combine(root, "assets", "generated", "PilotBaseToken");
        Directory.CreateDirectory(destinationRoot);

        var reportRoot = Path.Combine(root, "_unifiedtoolkit_reports", "generated-pilot-token-import");
        Directory.CreateDirectory(reportRoot);

        var requiredIds = LoadRequiredPilotIds(destinationRoot);
        var manifest = new GeneratedPilotTokenImportManifest
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceRoot = RepositoryRelative(root, sourceRoot),
            DestinationRoot = RepositoryRelative(root, destinationRoot)
        };

        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.png", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sourceFile in sourceFiles)
        {
            manifest.Tokens.Add(ImportOne(root, sourceRoot, destinationRoot, sourceFile, requiredIds));
        }

        var manifestPath = Path.Combine(reportRoot, "generated-pilot-token-import-manifest.json");
        WriteJson(manifestPath, manifest);

        var reportPath = Path.Combine(reportRoot, "generated-pilot-token-import-report.csv");
        WriteCsv(reportPath, manifest.Tokens);

        var catalogueResult = new AssetRepositoryCatalogueBuilder().Build(root);
        var knowledgeBaseResult = new KnowledgeBaseBuilder().Build(root, null, refreshCatalogue: false);

        return new GeneratedPilotTokenImportResult
        {
            ImagesScanned = sourceFiles.Count,
            Imported = manifest.Tokens.Count(entry => entry.Status == "imported"),
            Updated = manifest.Tokens.Count(entry => entry.Status == "updated"),
            Warnings = manifest.Tokens.Count(entry => entry.Status == "warning"),
            Errors = manifest.Tokens.Count(entry => entry.Status == "error"),
            OutputFolder = reportRoot,
            ManifestFile = manifestPath,
            ReportFile = reportPath,
            AssetManifestRoot = catalogueResult.ManifestRoot,
            KnowledgeBaseRoot = knowledgeBaseResult.OutputRoot
        };
    }

    private static GeneratedPilotTokenImportEntry ImportOne(
        string root,
        string sourceRoot,
        string destinationRoot,
        string sourceFile,
        IReadOnlyList<string> requiredIds)
    {
        var relative = Path.GetRelativePath(sourceRoot, sourceFile);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (segments.Length != 3)
            return Failure(root, sourceFile, "error", "Expected path <faction>/<ship>/<pilot>.png.");

        var faction = SafePathSegment(segments[0]);
        var ship = SafePathSegment(segments[1]);
        var pilot = SafePathSegment(Path.GetFileNameWithoutExtension(segments[2]));
        var pathKey = $"{faction}::{ship}::{pilot}";

        var matches = requiredIds
            .Where(id => PilotIdMatchesPath(id, faction, ship, pilot))
            .ToList();

        if (matches.Count != 1)
        {
            var message = matches.Count == 0
                ? $"No unique pilot ID matched '{pathKey}'."
                : $"Multiple pilot IDs matched '{pathKey}': {string.Join("; ", matches)}";
            return Failure(root, sourceFile, "error", message, faction, ship, pilot);
        }

        var pilotId = matches[0];
        try
        {
            using var bitmap = SKBitmap.Decode(sourceFile)
                ?? throw new InvalidDataException("PNG could not be decoded.");

            var destinationFolder = Path.Combine(destinationRoot, faction, ship);
            Directory.CreateDirectory(destinationFolder);

            var existing = Directory.EnumerateFiles(destinationFolder, "*.png", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => GetPilotFileKey(path).Equals(Normalise(pilot), StringComparison.OrdinalIgnoreCase));

            var destination = existing ?? Path.Combine(destinationFolder, $"{pilot}__pilot-{ShortHash(pilotId)}.png");
            var status = File.Exists(destination) ? "updated" : "imported";

            // Deliberately preserve the finished artwork byte-for-byte. No resize, re-encode or image processing.
            File.Copy(sourceFile, destination, overwrite: true);

            var info = new FileInfo(destination);
            var sizeWarning = IsCanonicalSize(bitmap.Width, bitmap.Height)
                ? string.Empty
                : $"Unexpected dimensions {bitmap.Width} x {bitmap.Height}; expected 431 x 495 or 955 x 998. The image was copied unchanged.";

            return new GeneratedPilotTokenImportEntry
            {
                Status = string.IsNullOrEmpty(sizeWarning) ? status : "warning",
                PilotId = pilotId,
                Faction = faction,
                Ship = ship,
                Pilot = pilot,
                SourceRepositoryPath = RepositoryRelative(root, sourceFile),
                OutputRepositoryPath = RepositoryRelative(root, destination),
                Width = bitmap.Width,
                Height = bitmap.Height,
                SizeBytes = info.Length,
                Sha256 = CalculateSha256(destination),
                Message = sizeWarning
            };
        }
        catch (Exception exception)
        {
            return Failure(root, sourceFile, "error", exception.Message, faction, ship, pilot, pilotId);
        }
    }

    private static IReadOnlyList<string> LoadRequiredPilotIds(string destinationRoot)
    {
        var path = Path.Combine(destinationRoot, "pilot-token-generation-required.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The pilot-token-generation-required.json register was not found.", path);

        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        if (!document.RootElement.TryGetProperty("pilotIds", out var idsElement) || idsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("pilot-token-generation-required.json does not contain a pilotIds array.");

        return idsElement.EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool PilotIdMatchesPath(string pilotId, string faction, string ship, string pilot)
    {
        var parts = pilotId.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return false;
        if (!Normalise(parts[0]).Equals(faction, StringComparison.OrdinalIgnoreCase)) return false;
        if (!Normalise(parts[1]).Equals(ship, StringComparison.OrdinalIgnoreCase)) return false;

        var idPilot = Normalise(parts[2]);
        return idPilot.Equals(pilot, StringComparison.OrdinalIgnoreCase)
            || idPilot.StartsWith(pilot, StringComparison.OrdinalIgnoreCase);
    }

    private static GeneratedPilotTokenImportEntry Failure(
        string root,
        string sourceFile,
        string status,
        string message,
        string faction = "",
        string ship = "",
        string pilot = "",
        string pilotId = "")
        => new()
        {
            Status = status,
            PilotId = pilotId,
            Faction = faction,
            Ship = ship,
            Pilot = pilot,
            SourceRepositoryPath = RepositoryRelative(root, sourceFile),
            Message = message
        };

    private static bool IsCanonicalSize(int width, int height)
        => (width == 431 && height == 495) || (width == 955 && height == 998);

    private static string ShortHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];

    private static string CalculateSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string GetPilotFileKey(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var marker = fileName.LastIndexOf("__pilot-", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) fileName = fileName[..marker];
        return Normalise(fileName);
    }

    private static string SafePathSegment(string value)
    {
        var result = Normalise(value);
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    private static string Normalise(string value)
        => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string RepositoryRelative(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static void WriteJson<T>(string path, T value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));

    private static void WriteCsv(string path, IEnumerable<GeneratedPilotTokenImportEntry> entries)
    {
        var lines = new List<string>
        {
            "status,pilotId,faction,ship,pilot,sourceRepositoryPath,outputRepositoryPath,width,height,sizeBytes,sha256,message"
        };
        lines.AddRange(entries.Select(entry => string.Join(',',
            Csv(entry.Status), Csv(entry.PilotId), Csv(entry.Faction), Csv(entry.Ship), Csv(entry.Pilot),
            Csv(entry.SourceRepositoryPath), Csv(entry.OutputRepositoryPath),
            entry.Width.ToString(CultureInfo.InvariantCulture), entry.Height.ToString(CultureInfo.InvariantCulture),
            entry.SizeBytes.ToString(CultureInfo.InvariantCulture), Csv(entry.Sha256), Csv(entry.Message))));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string Csv(string value)
        => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
