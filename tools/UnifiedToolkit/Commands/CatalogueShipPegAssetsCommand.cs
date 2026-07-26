using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

public static class CatalogueShipPegAssetsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly PegRequirement[] Requirements =
    {
        new("FirstEditionSmallShipPeg", "Small",
            new[] { "small", "smallpeg", "pegsmall" },
            new[] { "bwing", "large", "huge", "epic" }),
        new("FirstEditionBwingShipPeg", "B-Wing",
            new[] { "bwing", "b-wing", "b_wing" },
            Array.Empty<string>()),
        new("FirstEditionLargeShipPeg", "Large",
            new[] { "large", "largepeg", "peglarge" },
            new[] { "huge", "epic" }),
        new("FirstEditionHugeShipPeg", "Huge",
            new[] { "huge", "epic", "hugepeg", "epicpeg" },
            Array.Empty<string>())
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "Usage: catalogue-ship-peg-assets <first-edition-repository> [--output <folder>]");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var sourceFolder = Path.Combine(
                repositoryRoot, "assets", "source", "unified25",
                "assets", "ships-v2", "bases", "pegs");

            if (!Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException(
                    $"Peg source folder was not found: {sourceFolder}");

            var outputFolder = ReadOption(args, "--output")
                ?? Path.Combine(repositoryRoot, "_unifiedtoolkit_reports",
                    "phase12b", "ship-peg-catalogue");
            outputFolder = Path.GetFullPath(outputFolder);

            var files = Directory
                .EnumerateFiles(sourceFolder, "*.obj", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => AnalyseFile(repositoryRoot, path))
                .ToList();

            var pegs = Requirements
                .Select(requirement => Resolve(requirement, files))
                .ToList();

            Directory.CreateDirectory(outputFolder);

            var manifest = new ShipPegAssetCatalogue
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                SourceFolder = NormalisePath(sourceFolder),
                ObjFilesScanned = files.Count,
                ResolvedPegTypes = pegs.Count(peg => peg.Status == "Resolved"),
                AmbiguousPegTypes = pegs.Count(peg => peg.Status == "Ambiguous"),
                MissingPegTypes = pegs.Count(peg => peg.Status == "Missing"),
                Pegs = pegs,
                Files = files
            };

            var jsonPath = Path.Combine(outputFolder, "ship-peg-assets.json");
            var csvPath = Path.Combine(outputFolder, "ship-peg-assets.csv");
            var reportPath = Path.Combine(outputFolder, "SHIP-PEG-ASSET-CATALOGUE.md");

            File.WriteAllText(jsonPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, files, pegs);
            WriteReport(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit Phase 12B-2A Ship Peg Asset Catalogue");
            Console.WriteLine("======================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:            {repositoryRoot}");
            Console.WriteLine($"Peg source folder:     {sourceFolder}");
            Console.WriteLine($"OBJ files scanned:     {files.Count}");
            Console.WriteLine($"Peg types resolved:    {manifest.ResolvedPegTypes}");
            Console.WriteLine($"Peg types ambiguous:   {manifest.AmbiguousPegTypes}");
            Console.WriteLine($"Peg types missing:     {manifest.MissingPegTypes}");
            Console.WriteLine();

            foreach (var peg in pegs)
                Console.WriteLine(
                    $"  {peg.TemplateKey,-30} {peg.Status,-10} {peg.RepositoryPath}");

            Console.WriteLine();
            Console.WriteLine($"Manifest:              {jsonPath}");
            Console.WriteLine($"CSV:                   {csvPath}");
            Console.WriteLine($"Report:                {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Ship peg assets catalogued. Source OBJ files were not modified.");

            return manifest.AmbiguousPegTypes == 0
                && manifest.MissingPegTypes == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ship peg catalogue failed: {ex.Message}");
            return 1;
        }
    }

    private static ShipPegCatalogueEntry Resolve(
        PegRequirement requirement,
        IReadOnlyList<ShipPegFileRecord> files)
    {
        var candidates = files
            .Select(file => new { File = file, Score = Score(requirement, file) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.File.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            return new ShipPegCatalogueEntry
            {
                TemplateKey = requirement.TemplateKey,
                PegType = requirement.PegType,
                Status = "Missing"
            };

        var topScore = candidates[0].Score;
        var top = candidates.Where(item => item.Score == topScore).ToList();

        if (top.Count != 1)
            return new ShipPegCatalogueEntry
            {
                TemplateKey = requirement.TemplateKey,
                PegType = requirement.PegType,
                Status = "Ambiguous",
                CandidatePaths = top.Select(item => item.File.RepositoryPath).ToList()
            };

        var selected = top[0].File;
        return new ShipPegCatalogueEntry
        {
            TemplateKey = requirement.TemplateKey,
            PegType = requirement.PegType,
            Status = "Resolved",
            RepositoryPath = selected.RepositoryPath,
            FileName = selected.FileName,
            Sha256 = selected.Sha256,
            VertexCount = selected.VertexCount,
            FaceCount = selected.FaceCount,
            CandidatePaths = candidates.Take(8)
                .Select(item => item.File.RepositoryPath).ToList()
        };
    }

    private static int Score(PegRequirement requirement, ShipPegFileRecord file)
    {
        var value = Normalise(file.FileName + " " + file.RepositoryPath);

        if (requirement.ExcludedTerms.Any(term =>
                value.Contains(Normalise(term), StringComparison.OrdinalIgnoreCase)))
            return 0;

        var score = requirement.RequiredTerms.Sum(term =>
        {
            var normalised = Normalise(term);
            return value.Contains(normalised, StringComparison.OrdinalIgnoreCase)
                ? normalised.Length * 10 : 0;
        });

        if (value.Contains("peg", StringComparison.OrdinalIgnoreCase))
            score += 5;

        return score;
    }

    private static ShipPegFileRecord AnalyseFile(
        string repositoryRoot, string path)
    {
        var vertices = 0;
        var faces = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("v ", StringComparison.Ordinal)) vertices++;
            else if (line.StartsWith("f ", StringComparison.Ordinal)) faces++;
        }

        using var stream = File.OpenRead(path);
        return new ShipPegFileRecord
        {
            FileName = Path.GetFileName(path),
            RepositoryPath = NormalisePath(Path.GetRelativePath(repositoryRoot, path)),
            Sha256 = Convert.ToHexString(SHA256.HashData(stream)),
            Bytes = new FileInfo(path).Length,
            VertexCount = vertices,
            FaceCount = faces
        };
    }

    private static void WriteCsv(
        string path,
        IReadOnlyList<ShipPegFileRecord> files,
        IReadOnlyList<ShipPegCatalogueEntry> pegs)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(
            "RecordType,TemplateKey,PegType,Status,FileName,RepositoryPath,Bytes,VertexCount,FaceCount,Sha256,CandidatePaths");

        foreach (var peg in pegs)
            writer.WriteLine(string.Join(',',
                Csv("ResolvedPegType"), Csv(peg.TemplateKey), Csv(peg.PegType),
                Csv(peg.Status), Csv(peg.FileName), Csv(peg.RepositoryPath), "",
                peg.VertexCount, peg.FaceCount, Csv(peg.Sha256),
                Csv(string.Join('|', peg.CandidatePaths))));

        foreach (var file in files)
            writer.WriteLine(string.Join(',',
                Csv("SourceFile"), "", "", "", Csv(file.FileName),
                Csv(file.RepositoryPath), file.Bytes, file.VertexCount,
                file.FaceCount, Csv(file.Sha256), ""));
    }

    private static void WriteReport(
        string path, ShipPegAssetCatalogue manifest)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Phase 12B-2A – Ship Peg Asset Catalogue");
        writer.WriteLine();
        writer.WriteLine($"Source: `{manifest.SourceFolder}`");
        writer.WriteLine();
        writer.WriteLine("| Template key | Type | Status | OBJ |");
        writer.WriteLine("|---|---|---|---|");
        foreach (var peg in manifest.Pegs)
            writer.WriteLine(
                $"| `{peg.TemplateKey}` | {peg.PegType} | {peg.Status} | `{peg.RepositoryPath}` |");
        writer.WriteLine();
        writer.WriteLine(
            "Small bases use the Small peg; the B-wing uses its dedicated peg; " +
            "Large bases use the Large peg; Epic bases will use the Huge peg.");
    }

    private static string Normalise(string value) =>
        new((value ?? string.Empty).ToLowerInvariant()
            .Where(char.IsLetterOrDigit).ToArray());

    private static string NormalisePath(string path) =>
        path.Replace('\\', '/');

    private static string? ReadOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
    }

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private sealed record PegRequirement(
        string TemplateKey,
        string PegType,
        string[] RequiredTerms,
        string[] ExcludedTerms);
}

public sealed class ShipPegAssetCatalogue
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string SourceFolder { get; init; } = string.Empty;
    public int ObjFilesScanned { get; init; }
    public int ResolvedPegTypes { get; init; }
    public int AmbiguousPegTypes { get; init; }
    public int MissingPegTypes { get; init; }
    public List<ShipPegCatalogueEntry> Pegs { get; init; } = new();
    public List<ShipPegFileRecord> Files { get; init; } = new();
}

public sealed class ShipPegCatalogueEntry
{
    public string TemplateKey { get; init; } = string.Empty;
    public string PegType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public int VertexCount { get; init; }
    public int FaceCount { get; init; }
    public List<string> CandidatePaths { get; init; } = new();
}

public sealed class ShipPegFileRecord
{
    public string FileName { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public int VertexCount { get; init; }
    public int FaceCount { get; init; }
}
