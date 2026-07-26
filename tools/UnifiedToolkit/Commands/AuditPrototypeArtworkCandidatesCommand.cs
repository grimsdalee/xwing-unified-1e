using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12C-3A:
/// Inventories candidate artwork for the remaining visual mapping decisions:
/// K-Wing textures, Sheathipede textures, and First Edition pilot-card backs.
/// No files are modified.
/// </summary>
public static class AuditPrototypeArtworkCandidatesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "Usage: audit-prototype-artwork-candidates <first-edition-repository> [--output <folder>]");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var output = ReadOption(args, "--output")
                ?? Path.Combine(
                    repositoryRoot,
                    "_unifiedtoolkit_reports",
                    "phase12c",
                    "prototype-artwork-audit");
            output = Path.GetFullPath(output);

            var records = new List<ArtworkCandidateRecord>();

            ScanShipCandidates(
                repositoryRoot,
                "KWingTexture",
                new[] { "kwing", "btl-s8kwing", "btls8kwing" },
                records);

            ScanShipCandidates(
                repositoryRoot,
                "SheathipedeTexture",
                new[] { "sheathipede" },
                records);

            ScanCardBackCandidates(
                repositoryRoot,
                records);

            records = records
                .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(item => item.Score)
                .ThenBy(item => item.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Directory.CreateDirectory(output);

            var manifest = new PrototypeArtworkAuditManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = repositoryRoot.Replace('\\', '/'),
                CandidateCount = records.Count,
                Candidates = records
            };

            var jsonPath = Path.Combine(
                output,
                "prototype-artwork-candidates.json");
            var csvPath = Path.Combine(
                output,
                "prototype-artwork-candidates.csv");
            var reportPath = Path.Combine(
                output,
                "PROTOTYPE-ARTWORK-CANDIDATES.md");

            File.WriteAllText(
                jsonPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, records);
            WriteReport(reportPath, manifest);

            Console.WriteLine(
                "UnifiedToolkit Phase 12C-3A Prototype Artwork Candidate Audit");
            Console.WriteLine(
                "===============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:              {repositoryRoot}");
            Console.WriteLine($"Candidates found:        {records.Count}");
            Console.WriteLine($"K-Wing textures:         {records.Count(r => r.Category == "KWingTexture")}");
            Console.WriteLine($"Sheathipede textures:    {records.Count(r => r.Category == "SheathipedeTexture")}");
            Console.WriteLine($"Pilot-card backs:        {records.Count(r => r.Category == "PilotCardBack")}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                {jsonPath}");
            Console.WriteLine($"CSV:                     {csvPath}");
            Console.WriteLine($"Report:                  {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Artwork candidates audited. Repository files were not modified.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Prototype artwork audit failed: {ex.Message}");
            return 1;
        }
    }

    private static void ScanShipCandidates(
        string repositoryRoot,
        string category,
        IReadOnlyList<string> terms,
        ICollection<ArtworkCandidateRecord> records)
    {
        var root = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified25",
            "assets",
            "ships-v2");

        if (!Directory.Exists(root))
            return;

        foreach (var path in Directory.EnumerateFiles(
                     root,
                     "*.*",
                     SearchOption.AllDirectories))
        {
            if (!IsImage(path))
                continue;

            var normalised = Normalise(path);
            if (!terms.Any(term =>
                    normalised.Contains(
                        Normalise(term),
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            records.Add(CreateRecord(
                repositoryRoot,
                category,
                path,
                ScoreShipTexture(path, category)));
        }
    }

    private static void ScanCardBackCandidates(
        string repositoryRoot,
        ICollection<ArtworkCandidateRecord> records)
    {
        var roots = new[]
        {
            Path.Combine(repositoryRoot, "assets", "source", "legacy1e"),
            Path.Combine(repositoryRoot, "assets", "source", "xwing-data"),
            Path.Combine(repositoryRoot, "source", "xwing-data")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(
                         root,
                         "*.*",
                         SearchOption.AllDirectories))
            {
                if (!IsImage(path))
                    continue;

                var name = Normalise(
                    Path.GetFileNameWithoutExtension(path));
                var full = Normalise(path);

                if (!name.Contains("back")
                    && !full.Contains("cardback")
                    && !full.Contains("cardbacks"))
                {
                    continue;
                }

                records.Add(CreateRecord(
                    repositoryRoot,
                    "PilotCardBack",
                    path,
                    ScoreCardBack(path)));
            }
        }
    }

    private static ArtworkCandidateRecord CreateRecord(
        string repositoryRoot,
        string category,
        string path,
        int score)
    {
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(
            SHA256.HashData(stream));

        return new ArtworkCandidateRecord
        {
            Category = category,
            Score = score,
            FileName = Path.GetFileName(path),
            RepositoryPath = Path.GetRelativePath(
                    repositoryRoot,
                    path)
                .Replace('\\', '/'),
            Bytes = new FileInfo(path).Length,
            Sha256 = hash
        };
    }

    private static int ScoreShipTexture(
        string path,
        string category)
    {
        var name = Normalise(
            Path.GetFileNameWithoutExtension(path));
        var score = 10;

        if (name.Contains("standard"))
            score += 50;
        if (name.Contains("red"))
            score += category == "KWingTexture" ? 80 : 20;
        if (name.Contains("white"))
            score += 20;
        if (name.Contains("gold"))
            score -= category == "KWingTexture" ? 20 : 0;
        if (name.Contains("icon")
            || name.Contains("token")
            || name.Contains("card"))
            score -= 100;

        return score;
    }

    private static int ScoreCardBack(string path)
    {
        var normalised = Normalise(path);
        var score = 10;

        if (normalised.Contains("legacy1e"))
            score += 100;
        if (normalised.Contains("cardback"))
            score += 80;
        if (normalised.Contains("pilot"))
            score += 20;
        if (normalised.Contains("upgrade"))
            score -= 20;

        return score;
    }

    private static bool IsImage(string path) =>
        ImageExtensions.Contains(
            Path.GetExtension(path),
            StringComparer.OrdinalIgnoreCase);

    private static string Normalise(string value) =>
        new((value ?? string.Empty)
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string? ReadOption(
        string[] args,
        string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(
                    option,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void WriteCsv(
        string path,
        IEnumerable<ArtworkCandidateRecord> records)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "Category,Score,FileName,RepositoryPath,Bytes,SHA256");

        foreach (var record in records)
        {
            writer.WriteLine(string.Join(',',
                Csv(record.Category),
                record.Score,
                Csv(record.FileName),
                Csv(record.RepositoryPath),
                record.Bytes,
                Csv(record.Sha256)));
        }
    }

    private static void WriteReport(
        string path,
        PrototypeArtworkAuditManifest manifest)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "# Phase 12C-3A – Prototype Artwork Candidates");
        writer.WriteLine();

        foreach (var group in manifest.Candidates
                     .GroupBy(item => item.Category))
        {
            writer.WriteLine($"## {group.Key}");
            writer.WriteLine();
            writer.WriteLine("| Score | File | Repository path |");
            writer.WriteLine("|---:|---|---|");

            foreach (var item in group)
            {
                writer.WriteLine(
                    $"| {item.Score} | `{item.FileName}` | `{item.RepositoryPath}` |");
            }

            writer.WriteLine();
        }
    }

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}

public sealed class PrototypeArtworkAuditManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public List<ArtworkCandidateRecord> Candidates { get; init; } = new();
}

public sealed class ArtworkCandidateRecord
{
    public string Category { get; init; } = string.Empty;
    public int Score { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
