using System.Globalization;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

public static class OptimiseShipTexturesCommand
{
    private const int DefaultQuality = 95;
    private const double DefaultMinimumSavingsPercent = 20.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
            var repositoryRoot = Path.GetFullPath(args[0]);
            var textureRoot = Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified25",
                "assets",
                "ships-v2");

            var outputRoot = Path.GetFullPath(ReadOption(args, "--output")
                ?? Path.Combine(
                    repositoryRoot,
                    "_unifiedtoolkit_reports",
                    "texture-optimisation"));

            var quality = ReadIntOption(args, "--quality", DefaultQuality, 1, 100);
            var minimumSavingsPercent = ReadDoubleOption(
                args,
                "--minimum-savings-percent",
                DefaultMinimumSavingsPercent,
                0.0,
                100.0);

            var apply = HasFlag(args, "--apply");

            if (!Directory.Exists(repositoryRoot))
                throw new DirectoryNotFoundException($"Repository not found: {repositoryRoot}");

            if (!Directory.Exists(textureRoot))
                throw new DirectoryNotFoundException($"ships-v2 asset folder not found: {textureRoot}");

            Directory.CreateDirectory(outputRoot);

            var records = AnalyseTextures(
                repositoryRoot,
                textureRoot,
                quality,
                minimumSavingsPercent,
                apply);

            var manifest = BuildManifest(
                repositoryRoot,
                textureRoot,
                outputRoot,
                quality,
                minimumSavingsPercent,
                apply,
                records);

            var manifestPath = Path.Combine(outputRoot, "ship-texture-optimisation.json");
            var csvPath = Path.Combine(outputRoot, "ship-texture-optimisation.csv");
            var reportPath = Path.Combine(outputRoot, "SHIP-TEXTURE-OPTIMISATION.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine,
                new UTF8Encoding(false));

            WriteCsv(csvPath, records);
            WriteReport(reportPath, manifest, records);

            Console.WriteLine("UnifiedToolkit Ship Texture Optimisation");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                {repositoryRoot}");
            Console.WriteLine($"Texture root:              {textureRoot}");
            Console.WriteLine($"Mode:                      {(apply ? "Apply" : "Preview")}");
            Console.WriteLine($"JPEG quality:              {quality}");
            Console.WriteLine($"Minimum saving:            {minimumSavingsPercent:0.##}%");
            Console.WriteLine();
            Console.WriteLine($"PNG textures scanned:      {manifest.PngTexturesScanned}");
            Console.WriteLine($"Eligible opaque PNGs:      {manifest.EligibleOpaquePngs}");
            Console.WriteLine($"Skipped transparency:      {manifest.SkippedTransparency}");
            Console.WriteLine($"Already generated JPEGs:   {manifest.ExistingGeneratedJpegs}");
            Console.WriteLine($"Below saving threshold:    {manifest.BelowSavingsThreshold}");
            Console.WriteLine($"Conversion candidates:     {manifest.ConversionCandidates}");
            Console.WriteLine($"JPEGs written:             {manifest.JpegsWritten}");
            Console.WriteLine($"Errors:                    {manifest.Errors}");
            Console.WriteLine();
            Console.WriteLine($"Candidate PNG bytes:       {FormatBytes(manifest.CandidatePngBytes)}");
            Console.WriteLine($"Estimated JPEG bytes:      {FormatBytes(manifest.EstimatedCandidateJpegBytes)}");
            Console.WriteLine($"Estimated saving:          {FormatBytes(manifest.EstimatedSavingsBytes)} ({manifest.EstimatedSavingsPercent:0.##}%)");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                  {manifestPath}");
            Console.WriteLine($"CSV:                       {csvPath}");
            Console.WriteLine($"Report:                    {reportPath}");
            Console.WriteLine();

            if (apply)
            {
                Console.WriteLine(
                    "JPEG files were written alongside their PNG sources. " +
                    "No PNG files or repository references were removed or changed.");
            }
            else
            {
                Console.WriteLine(
                    "Preview completed. No textures or repository references were modified.");
            }

            return manifest.Errors == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Ship texture optimisation failed: {exception.Message}");
            return 1;
        }
    }

    private static List<ShipTextureOptimisationRecord> AnalyseTextures(
        string repositoryRoot,
        string textureRoot,
        int quality,
        double minimumSavingsPercent,
        bool apply)
    {
        var pngPaths = Directory
            .EnumerateFiles(textureRoot, "*.png", SearchOption.AllDirectories)
            .Where(IsUnderTexturesFolder)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var records = new List<ShipTextureOptimisationRecord>(pngPaths.Count);

        foreach (var pngPath in pngPaths)
        {
            var relativePngPath = NormalisePath(Path.GetRelativePath(repositoryRoot, pngPath));
            var preferredJpegPath = Path.ChangeExtension(pngPath, ".jpg");
            var relativePreferredJpegPath = NormalisePath(
                Path.GetRelativePath(repositoryRoot, preferredJpegPath));
            var pngBytes = new FileInfo(pngPath).Length;

            try
            {
                using var bitmap = SKBitmap.Decode(pngPath)
                    ?? throw new InvalidDataException("SkiaSharp could not decode the PNG.");

                var hasTransparency = HasTransparency(bitmap);
                if (hasTransparency)
                {
                    records.Add(new ShipTextureOptimisationRecord
                    {
                        PngPath = relativePngPath,
                        JpegPath = relativePreferredJpegPath,
                        Width = bitmap.Width,
                        Height = bitmap.Height,
                        PngBytes = pngBytes,
                        Status = "SkippedTransparency",
                        Note = "PNG contains at least one pixel with alpha below 255."
                    });
                    continue;
                }

                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality)
                    ?? throw new InvalidDataException("SkiaSharp could not encode the JPEG preview.");

                var estimatedJpegBytes = encoded.Size;
                var savingsBytes = pngBytes - estimatedJpegBytes;
                var savingsPercent = pngBytes == 0
                    ? 0.0
                    : savingsBytes * 100.0 / pngBytes;

                if (savingsBytes <= 0 || savingsPercent < minimumSavingsPercent)
                {
                    records.Add(new ShipTextureOptimisationRecord
                    {
                        PngPath = relativePngPath,
                        JpegPath = relativePreferredJpegPath,
                        Width = bitmap.Width,
                        Height = bitmap.Height,
                        PngBytes = pngBytes,
                        EstimatedJpegBytes = estimatedJpegBytes,
                        SavingsBytes = savingsBytes,
                        SavingsPercent = savingsPercent,
                        Status = "BelowSavingsThreshold",
                        Note = $"Estimated saving is below {minimumSavingsPercent:0.##}%."
                    });
                    continue;
                }

                var encodedBytes = encoded.ToArray();
                var destination = ResolveJpegDestination(
                    preferredJpegPath,
                    encodedBytes);

                var jpegPath = destination.Path;
                var relativeJpegPath = NormalisePath(
                    Path.GetRelativePath(repositoryRoot, jpegPath));

                if (destination.AlreadyGenerated)
                {
                    records.Add(new ShipTextureOptimisationRecord
                    {
                        PngPath = relativePngPath,
                        JpegPath = relativeJpegPath,
                        Width = bitmap.Width,
                        Height = bitmap.Height,
                        PngBytes = pngBytes,
                        EstimatedJpegBytes = estimatedJpegBytes,
                        SavingsBytes = savingsBytes,
                        SavingsPercent = savingsPercent,
                        Status = "ExistingGeneratedJpeg",
                        Note = "An identical JPEG generated at the selected quality already exists."
                    });
                    continue;
                }

                var status = "Candidate";
                var note = destination.SequenceNumber == 1
                    ? "Opaque ship diffuse texture suitable for JPEG review."
                    : $"Base JPEG name already exists; the generated JPEG will use suffix {destination.SequenceNumber}.";

                if (apply)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(jpegPath)!);
                    var temporaryPath = jpegPath + ".tmp";

                    try
                    {
                        File.WriteAllBytes(temporaryPath, encodedBytes);

                        File.Move(temporaryPath, jpegPath, overwrite: true);
                        status = "Written";
                        note = "JPEG written alongside PNG. Source and references were not changed.";
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath))
                            File.Delete(temporaryPath);
                    }
                }

                records.Add(new ShipTextureOptimisationRecord
                {
                    PngPath = relativePngPath,
                    JpegPath = relativeJpegPath,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    PngBytes = pngBytes,
                    EstimatedJpegBytes = estimatedJpegBytes,
                    SavingsBytes = savingsBytes,
                    SavingsPercent = savingsPercent,
                    Status = status,
                    Note = note
                });
            }
            catch (Exception exception)
            {
                records.Add(new ShipTextureOptimisationRecord
                {
                    PngPath = relativePngPath,
                    JpegPath = relativePreferredJpegPath,
                    PngBytes = pngBytes,
                    Status = "Error",
                    Note = exception.Message
                });
            }
        }

        return records;
    }

    private static ShipTextureOptimisationManifest BuildManifest(
        string repositoryRoot,
        string textureRoot,
        string outputRoot,
        int quality,
        double minimumSavingsPercent,
        bool apply,
        IReadOnlyList<ShipTextureOptimisationRecord> records)
    {
        var candidates = records
            .Where(record => record.Status is "Candidate" or "Written")
            .ToList();

        var candidatePngBytes = candidates.Sum(record => record.PngBytes);
        var estimatedJpegBytes = candidates.Sum(record => record.EstimatedJpegBytes);
        var estimatedSavingsBytes = candidatePngBytes - estimatedJpegBytes;

        return new ShipTextureOptimisationManifest
        {
            SchemaVersion = "1.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            RepositoryRoot = NormalisePath(repositoryRoot),
            TextureRoot = NormalisePath(textureRoot),
            OutputRoot = NormalisePath(outputRoot),
            Mode = apply ? "Apply" : "Preview",
            JpegQuality = quality,
            MinimumSavingsPercent = minimumSavingsPercent,
            ExistingJpegsAreNeverOverwritten = true,
            PngTexturesScanned = records.Count,
            EligibleOpaquePngs = records.Count(record =>
                record.Status is "Candidate" or "Written" or
                "ExistingGeneratedJpeg" or "BelowSavingsThreshold"),
            SkippedTransparency = records.Count(record => record.Status == "SkippedTransparency"),
            ExistingGeneratedJpegs = records.Count(record => record.Status == "ExistingGeneratedJpeg"),
            BelowSavingsThreshold = records.Count(record => record.Status == "BelowSavingsThreshold"),
            ConversionCandidates = candidates.Count,
            JpegsWritten = records.Count(record => record.Status == "Written"),
            Errors = records.Count(record => record.Status == "Error"),
            CandidatePngBytes = candidatePngBytes,
            EstimatedCandidateJpegBytes = estimatedJpegBytes,
            EstimatedSavingsBytes = estimatedSavingsBytes,
            EstimatedSavingsPercent = candidatePngBytes == 0
                ? 0.0
                : estimatedSavingsBytes * 100.0 / candidatePngBytes,
            Records = records.ToList()
        };
    }

    private static JpegDestination ResolveJpegDestination(
        string preferredPath,
        byte[] encodedBytes)
    {
        var directory = Path.GetDirectoryName(preferredPath)
            ?? throw new InvalidDataException(
                $"JPEG destination has no parent folder: {preferredPath}");

        var stem = Path.GetFileNameWithoutExtension(preferredPath);
        var extension = Path.GetExtension(preferredPath);

        for (var sequenceNumber = 1; sequenceNumber < 10_000; sequenceNumber++)
        {
            var fileName = sequenceNumber == 1
                ? stem + extension
                : stem + sequenceNumber.ToString(CultureInfo.InvariantCulture) + extension;

            var candidatePath = Path.Combine(directory, fileName);

            if (!File.Exists(candidatePath))
            {
                return new JpegDestination(
                    candidatePath,
                    sequenceNumber,
                    AlreadyGenerated: false);
            }

            if (FileContentsEqual(candidatePath, encodedBytes))
            {
                return new JpegDestination(
                    candidatePath,
                    sequenceNumber,
                    AlreadyGenerated: true);
            }
        }

        throw new IOException(
            $"Could not find a free JPEG destination for {preferredPath}.");
    }

    private static bool FileContentsEqual(
        string path,
        byte[] expectedBytes)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length != expectedBytes.LongLength)
            return false;

        const int bufferSize = 81920;
        var buffer = new byte[bufferSize];
        var offset = 0;

        using var stream = File.OpenRead(path);

        while (offset < expectedBytes.Length)
        {
            var bytesToRead = Math.Min(
                buffer.Length,
                expectedBytes.Length - offset);

            var bytesRead = stream.Read(buffer, 0, bytesToRead);
            if (bytesRead == 0)
                return false;

            for (var index = 0; index < bytesRead; index++)
            {
                if (buffer[index] != expectedBytes[offset + index])
                    return false;
            }

            offset += bytesRead;
        }

        return stream.ReadByte() == -1;
    }

    private static bool HasTransparency(SKBitmap bitmap)
    {
        if (bitmap.AlphaType == SKAlphaType.Opaque)
            return false;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha < 255)
                    return true;
            }
        }

        return false;
    }

    private static bool IsUnderTexturesFolder(string path)
    {
        var parts = Path.GetRelativePath(Path.GetPathRoot(path) ?? string.Empty, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return parts.Any(part =>
            part.Equals("Textures", StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteCsv(
        string path,
        IReadOnlyList<ShipTextureOptimisationRecord> records)
    {
        var lines = new List<string>
        {
            "PngPath,JpegPath,Width,Height,PngBytes,EstimatedJpegBytes,SavingsBytes,SavingsPercent,Status,Note"
        };

        lines.AddRange(records.Select(record => string.Join(",",
            Csv(record.PngPath),
            Csv(record.JpegPath),
            record.Width.ToString(CultureInfo.InvariantCulture),
            record.Height.ToString(CultureInfo.InvariantCulture),
            record.PngBytes.ToString(CultureInfo.InvariantCulture),
            record.EstimatedJpegBytes.ToString(CultureInfo.InvariantCulture),
            record.SavingsBytes.ToString(CultureInfo.InvariantCulture),
            record.SavingsPercent.ToString("0.####", CultureInfo.InvariantCulture),
            Csv(record.Status),
            Csv(record.Note))));

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteReport(
        string path,
        ShipTextureOptimisationManifest manifest,
        IReadOnlyList<ShipTextureOptimisationRecord> records)
    {
        var report = new StringBuilder();
        report.AppendLine("# Ship Texture Optimisation");
        report.AppendLine();
        report.AppendLine($"- Mode: **{manifest.Mode}**");
        report.AppendLine($"- JPEG quality: **{manifest.JpegQuality}**");
        report.AppendLine($"- Minimum saving: **{manifest.MinimumSavingsPercent:0.##}%**");
        report.AppendLine();
        report.AppendLine("| Metric | Value |");
        report.AppendLine("|---|---:|");
        report.AppendLine($"| PNG textures scanned | {manifest.PngTexturesScanned} |");
        report.AppendLine($"| Conversion candidates | {manifest.ConversionCandidates} |");
        report.AppendLine($"| Skipped: transparency | {manifest.SkippedTransparency} |");
        report.AppendLine($"| Already generated identical JPEGs | {manifest.ExistingGeneratedJpegs} |");
        report.AppendLine($"| Below saving threshold | {manifest.BelowSavingsThreshold} |");
        report.AppendLine($"| Errors | {manifest.Errors} |");
        report.AppendLine($"| Candidate PNG size | {FormatBytes(manifest.CandidatePngBytes)} |");
        report.AppendLine($"| Estimated JPEG size | {FormatBytes(manifest.EstimatedCandidateJpegBytes)} |");
        report.AppendLine($"| Estimated saving | {FormatBytes(manifest.EstimatedSavingsBytes)} ({manifest.EstimatedSavingsPercent:0.##}%) |");
        report.AppendLine();
        report.AppendLine(
            "> Apply mode writes JPEG files alongside PNG files. Existing JPEG files are never overwritten. When the preferred name exists, a numeric suffix is used. PNG files and repository references are not changed.");
        report.AppendLine();

        foreach (var group in records
                     .GroupBy(record => record.Status)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            report.AppendLine($"## {group.Key} ({group.Count()})");
            report.AppendLine();
            report.AppendLine("| PNG | JPEG | Dimensions | Saving | Note |");
            report.AppendLine("|---|---|---:|---:|---|");

            foreach (var record in group.OrderBy(
                         record => record.PngPath,
                         StringComparer.OrdinalIgnoreCase))
            {
                report.AppendLine(
                    $"| `{EscapeMarkdown(record.PngPath)}` | " +
                    $"`{EscapeMarkdown(record.JpegPath)}` | " +
                    $"{record.Width}×{record.Height} | " +
                    $"{FormatBytes(record.SavingsBytes)} ({record.SavingsPercent:0.##}%) | " +
                    $"{EscapeMarkdown(record.Note)} |");
            }

            report.AppendLine();
        }

        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static int ReadIntOption(
        string[] args,
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var value = ReadOption(args, name);
        if (value is null)
            return defaultValue;

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new ArgumentException(
                $"{name} must be an integer from {minimum} to {maximum}.");
        }

        return parsed;
    }

    private static double ReadDoubleOption(
        string[] args,
        string name,
        double defaultValue,
        double minimum,
        double maximum)
    {
        var value = ReadOption(args, name);
        if (value is null)
            return defaultValue;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new ArgumentException(
                $"{name} must be a number from {minimum} to {maximum}.");
        }

        return parsed;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Skip(1).Any(argument =>
            argument.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string Csv(string value)
    {
        value ??= string.Empty;
        var escaped = value.Replace("\"", "\"\"");
        return escaped.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{escaped}\""
            : escaped;
    }

    private static string EscapeMarkdown(string value) =>
        (value ?? string.Empty)
            .Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", " ");

    private static string NormalisePath(string path) =>
        path.Replace('\\', '/');

    private static string FormatBytes(long bytes)
    {
        var absolute = Math.Abs((double)bytes);
        if (absolute >= 1024 * 1024 * 1024)
            return $"{bytes / (1024d * 1024d * 1024d):0.##} GiB";
        if (absolute >= 1024 * 1024)
            return $"{bytes / (1024d * 1024d):0.##} MiB";
        if (absolute >= 1024)
            return $"{bytes / 1024d:0.##} KiB";
        return $"{bytes} B";
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  optimise-ship-textures <first-edition-repo-folder> " +
            "[--quality <1-100>] [--minimum-savings-percent <0-100>] " +
            "[--output <folder>] [--apply]");
    }
}

public sealed class ShipTextureOptimisationManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string TextureRoot { get; init; } = string.Empty;
    public string OutputRoot { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public int JpegQuality { get; init; }
    public double MinimumSavingsPercent { get; init; }
    public bool ExistingJpegsAreNeverOverwritten { get; init; }
    public int PngTexturesScanned { get; init; }
    public int EligibleOpaquePngs { get; init; }
    public int SkippedTransparency { get; init; }
    public int ExistingGeneratedJpegs { get; init; }
    public int BelowSavingsThreshold { get; init; }
    public int ConversionCandidates { get; init; }
    public int JpegsWritten { get; init; }
    public int Errors { get; init; }
    public long CandidatePngBytes { get; init; }
    public long EstimatedCandidateJpegBytes { get; init; }
    public long EstimatedSavingsBytes { get; init; }
    public double EstimatedSavingsPercent { get; init; }
    public List<ShipTextureOptimisationRecord> Records { get; init; } = new();
}

public sealed class ShipTextureOptimisationRecord
{
    public string PngPath { get; init; } = string.Empty;
    public string JpegPath { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public long PngBytes { get; init; }
    public long EstimatedJpegBytes { get; init; }
    public long SavingsBytes { get; init; }
    public double SavingsPercent { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}


internal sealed record JpegDestination(
    string Path,
    int SequenceNumber,
    bool AlreadyGenerated);
