using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class FirstEditionDialStandardisationService
{
    private const int TargetWidth = 250;
    private const int TargetHeight = 250;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg"
    };

    public FirstEditionDialStandardisationResult Run(string repositoryFolder, bool inventoryOnly)
    {
        var repositoryRoot = Path.GetFullPath(repositoryFolder);
        var sourceRoot = Path.Combine(repositoryRoot, "assets", "source", "first-edition-dials");
        var generatedRoot = Path.Combine(repositoryRoot, "assets", "generated", "FirstEditionDialTexture");
        var reportRoot = Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "phase10g", "dial-standardisation");

        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException($"Repository folder not found: {repositoryRoot}");
        }

        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Curated First Edition dial folder not found: {sourceRoot}{Environment.NewLine}" +
                "Copy the curated dial folders into assets\\source\\first-edition-dials before running this command.");
        }

        Directory.CreateDirectory(reportRoot);
        if (!inventoryOnly)
        {
            Directory.CreateDirectory(generatedRoot);
        }

        var records = new List<FirstEditionDialInventoryRecord>();
        var sourceFiles = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sourceFile in sourceFiles)
        {
            records.Add(ProcessFile(repositoryRoot, sourceRoot, generatedRoot, sourceFile, inventoryOnly));
        }

        var inventoryCsv = Path.Combine(reportRoot, "dial-image-inventory.csv");
        var manifestFile = Path.Combine(reportRoot, "first-edition-dial-standardisation.json");
        var reportFile = Path.Combine(reportRoot, "DIAL-STANDARDISATION-REPORT.md");

        WriteInventoryCsv(inventoryCsv, records);
        WriteManifest(manifestFile, repositoryRoot, sourceRoot, generatedRoot, inventoryOnly, records);
        WriteReport(reportFile, sourceRoot, generatedRoot, inventoryOnly, records);

        return new FirstEditionDialStandardisationResult
        {
            ImagesScanned = records.Count,
            AlreadyCompliant = records.Count(record => record.AlreadyCompliant),
            FormatConversionRequired = records.Count(record => record.NeedsFormatConversion),
            ResizeRequired = records.Count(record => record.NeedsResize),
            Generated = records.Count(record => record.OutputStatus == "Generated"),
            UnchangedOutputs = records.Count(record => record.OutputStatus == "Unchanged"),
            Warnings = records.Count(record => record.Severity == "Warning"),
            Errors = records.Count(record => record.Severity == "Error"),
            SourceRoot = sourceRoot,
            GeneratedRoot = generatedRoot,
            InventoryCsv = inventoryCsv,
            ManifestFile = manifestFile,
            ReportFile = reportFile
        };
    }

    private static FirstEditionDialInventoryRecord ProcessFile(
        string repositoryRoot,
        string sourceRoot,
        string generatedRoot,
        string sourceFile,
        bool inventoryOnly)
    {
        var relativeSourcePath = NormalisePath(Path.GetRelativePath(sourceRoot, sourceFile));
        var relativeParts = relativeSourcePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var faction = relativeParts.Length > 1 ? relativeParts[0] : string.Empty;
        var shipId = Path.GetFileNameWithoutExtension(sourceFile);
        var extension = Path.GetExtension(sourceFile);
        var sourceFormat = extension.TrimStart('.').ToUpperInvariant();

        var record = new FirstEditionDialInventoryRecord
        {
            Faction = faction,
            ShipId = shipId,
            SourcePath = NormalisePath(Path.GetRelativePath(repositoryRoot, sourceFile)),
            SourceFormat = sourceFormat
        };

        try
        {
            using var codec = SKCodec.Create(sourceFile);
            if (codec is null)
            {
                record.Severity = "Error";
                record.Message = "SkiaSharp could not decode the image.";
                record.OutputStatus = "NotGenerated";
                return record;
            }

            record.Width = codec.Info.Width;
            record.Height = codec.Info.Height;
            record.NeedsResize = record.Width != TargetWidth || record.Height != TargetHeight;
            record.NeedsFormatConversion = !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase);
            record.AlreadyCompliant = !record.NeedsResize && !record.NeedsFormatConversion;
            record.Action = DetermineAction(record);

            if (inventoryOnly)
            {
                record.OutputStatus = "InventoryOnly";
                return record;
            }

            var outputBytes = CreateStandardisedPng(sourceFile, record.AlreadyCompliant);
            var contentHash = Convert.ToHexString(SHA256.HashData(outputBytes)).ToLowerInvariant();
            var shortHash = contentHash[..12];
            var safeFaction = string.IsNullOrWhiteSpace(faction) ? "unclassified" : faction;
            var outputDirectory = Path.Combine(generatedRoot, safeFaction);
            var outputFile = Path.Combine(outputDirectory, $"{shipId}__dial-{shortHash}.png");

            Directory.CreateDirectory(outputDirectory);

            var outputStatus = "Generated";
            if (File.Exists(outputFile) && File.ReadAllBytes(outputFile).AsSpan().SequenceEqual(outputBytes))
            {
                outputStatus = "Unchanged";
            }
            else
            {
                File.WriteAllBytes(outputFile, outputBytes);
            }

            record.OutputPath = NormalisePath(Path.GetRelativePath(repositoryRoot, outputFile));
            record.OutputFormat = "PNG";
            record.OutputWidth = TargetWidth;
            record.OutputHeight = TargetHeight;
            record.Sha256 = contentHash;
            record.OutputStatus = outputStatus;
            record.Severity = "Information";
        }
        catch (Exception exception)
        {
            record.Severity = "Error";
            record.Message = exception.Message;
            record.OutputStatus = "NotGenerated";
        }

        return record;
    }

    private static byte[] CreateStandardisedPng(string sourceFile, bool alreadyCompliant)
    {
        if (alreadyCompliant)
        {
            return File.ReadAllBytes(sourceFile);
        }

        using var sourceBitmap = SKBitmap.Decode(sourceFile)
            ?? throw new InvalidOperationException("SkiaSharp could not decode the image.");

        var targetInfo = new SKImageInfo(
            TargetWidth,
            TargetHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var destinationBitmap = sourceBitmap.Width == TargetWidth && sourceBitmap.Height == TargetHeight
            ? sourceBitmap.Copy(targetInfo.ColorType)
            : sourceBitmap.Resize(
                targetInfo,
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        if (destinationBitmap is null)
        {
            throw new InvalidOperationException("SkiaSharp could not resize the image.");
        }

        using var image = SKImage.FromBitmap(destinationBitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("SkiaSharp could not encode the standardised PNG image.");

        return encoded.ToArray();
    }

    private static string DetermineAction(FirstEditionDialInventoryRecord record)
    {
        if (record.AlreadyCompliant)
        {
            return "Copy unchanged";
        }

        if (record.NeedsResize && record.NeedsFormatConversion)
        {
            return "Resize and convert to PNG";
        }

        if (record.NeedsResize)
        {
            return "Resize";
        }

        return "Convert to PNG";
    }

    private static void WriteInventoryCsv(string path, IReadOnlyCollection<FirstEditionDialInventoryRecord> records)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Faction,ShipId,SourcePath,SourceFormat,Width,Height,AlreadyCompliant,NeedsResize,NeedsFormatConversion,Action,OutputPath,OutputFormat,OutputWidth,OutputHeight,Sha256,OutputStatus,Severity,Message");

        foreach (var record in records)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(record.Faction),
                Csv(record.ShipId),
                Csv(record.SourcePath),
                Csv(record.SourceFormat),
                record.Width.ToString(CultureInfo.InvariantCulture),
                record.Height.ToString(CultureInfo.InvariantCulture),
                record.AlreadyCompliant.ToString(CultureInfo.InvariantCulture),
                record.NeedsResize.ToString(CultureInfo.InvariantCulture),
                record.NeedsFormatConversion.ToString(CultureInfo.InvariantCulture),
                Csv(record.Action),
                Csv(record.OutputPath),
                Csv(record.OutputFormat),
                record.OutputWidth?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                record.OutputHeight?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Csv(record.Sha256),
                Csv(record.OutputStatus),
                Csv(record.Severity),
                Csv(record.Message)
            }));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static void WriteManifest(
        string path,
        string repositoryRoot,
        string sourceRoot,
        string generatedRoot,
        bool inventoryOnly,
        IReadOnlyCollection<FirstEditionDialInventoryRecord> records)
    {
        var manifest = new FirstEditionDialStandardisationManifest
        {
            Phase = "10G",
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = NormalisePath(repositoryRoot),
            SourceRoot = NormalisePath(Path.GetRelativePath(repositoryRoot, sourceRoot)),
            GeneratedRoot = NormalisePath(Path.GetRelativePath(repositoryRoot, generatedRoot)),
            InventoryOnly = inventoryOnly,
            TargetFormat = "PNG",
            TargetWidth = TargetWidth,
            TargetHeight = TargetHeight,
            Images = records.ToList()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        File.WriteAllText(path, JsonSerializer.Serialize(manifest, options), new UTF8Encoding(false));
    }

    private static void WriteReport(
        string path,
        string sourceRoot,
        string generatedRoot,
        bool inventoryOnly,
        IReadOnlyCollection<FirstEditionDialInventoryRecord> records)
    {
        var errors = records.Where(record => record.Severity == "Error").ToList();
        var nonCompliant = records.Where(record => !record.AlreadyCompliant).ToList();
        var builder = new StringBuilder();

        builder.AppendLine("# Phase 10G – First Edition Dial Standardisation");
        builder.AppendLine();
        builder.AppendLine($"- Mode: **{(inventoryOnly ? "Inventory only" : "Inventory and standardise")}**");
        builder.AppendLine($"- Source: `{NormalisePath(sourceRoot)}`");
        builder.AppendLine($"- Generated output: `{NormalisePath(generatedRoot)}`");
        builder.AppendLine("- Required format: **PNG**");
        builder.AppendLine($"- Required dimensions: **{TargetWidth} × {TargetHeight} pixels**");
        builder.AppendLine($"- Images scanned: **{records.Count}**");
        builder.AppendLine($"- Already compliant: **{records.Count(record => record.AlreadyCompliant)}**");
        builder.AppendLine($"- Format conversion required: **{records.Count(record => record.NeedsFormatConversion)}**");
        builder.AppendLine($"- Resize required: **{records.Count(record => record.NeedsResize)}**");
        builder.AppendLine($"- Errors: **{errors.Count}**");
        builder.AppendLine();

        builder.AppendLine("## Processing policy");
        builder.AppendLine();
        builder.AppendLine("Curated images in `assets/source/first-edition-dials` are never modified. Compliant PNG files are copied byte-for-byte. Other images are decoded, resized to exactly 250 × 250 pixels with high-quality cubic sampling, and encoded as PNG files.");
        builder.AppendLine();

        builder.AppendLine("## Images requiring processing");
        builder.AppendLine();
        if (nonCompliant.Count == 0)
        {
            builder.AppendLine("All images already meet the required format and dimensions.");
        }
        else
        {
            builder.AppendLine("| Faction | Ship | Source | Dimensions | Format | Action |");
            builder.AppendLine("|---|---|---|---:|---|---|");
            foreach (var record in nonCompliant)
            {
                builder.AppendLine($"| {Markdown(record.Faction)} | {Markdown(record.ShipId)} | `{Markdown(record.SourcePath)}` | {record.Width} × {record.Height} | {Markdown(record.SourceFormat)} | {Markdown(record.Action)} |");
            }
        }

        if (errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Errors");
            builder.AppendLine();
            foreach (var error in errors)
            {
                builder.AppendLine($"- `{Markdown(error.SourcePath)}`: {Markdown(error.Message)}");
            }
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static string Markdown(string? value) => (value ?? string.Empty).Replace("|", "\\|");

    private static string NormalisePath(string path) => path.Replace('\\', '/');
}

public sealed class FirstEditionDialStandardisationResult
{
    public int ImagesScanned { get; init; }
    public int AlreadyCompliant { get; init; }
    public int FormatConversionRequired { get; init; }
    public int ResizeRequired { get; init; }
    public int Generated { get; init; }
    public int UnchangedOutputs { get; init; }
    public int Warnings { get; init; }
    public int Errors { get; init; }
    public string SourceRoot { get; init; } = string.Empty;
    public string GeneratedRoot { get; init; } = string.Empty;
    public string InventoryCsv { get; init; } = string.Empty;
    public string ManifestFile { get; init; } = string.Empty;
    public string ReportFile { get; init; } = string.Empty;
}

public sealed class FirstEditionDialStandardisationManifest
{
    public string Phase { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string SourceRoot { get; init; } = string.Empty;
    public string GeneratedRoot { get; init; } = string.Empty;
    public bool InventoryOnly { get; init; }
    public string TargetFormat { get; init; } = string.Empty;
    public int TargetWidth { get; init; }
    public int TargetHeight { get; init; }
    public List<FirstEditionDialInventoryRecord> Images { get; init; } = new();
}

public sealed class FirstEditionDialInventoryRecord
{
    public string Faction { get; set; } = string.Empty;
    public string ShipId { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public bool AlreadyCompliant { get; set; }
    public bool NeedsResize { get; set; }
    public bool NeedsFormatConversion { get; set; }
    public string Action { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string OutputStatus { get; set; } = string.Empty;
    public string Severity { get; set; } = "Information";
    public string Message { get; set; } = string.Empty;
}
