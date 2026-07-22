using System.Globalization;
using System.Text;
using System.Text.Json;
using UnifiedToolkit.KnowledgeBase.Assets.Images;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class PilotTokenImageAuditResult
{
    public int ImagesScanned { get; init; }
    public int UniqueCanvasSizes { get; init; }
    public int ImagesWithWarnings { get; init; }
    public int ExactDuplicateGroups { get; init; }
    public string RecommendedSmallCanvas { get; init; } = "Unknown";
    public string RecommendedLargeCanvas { get; init; } = "Unknown";
    public string OutputFolder { get; init; } = string.Empty;
    public string AuditCsv { get; init; } = string.Empty;
    public string SummaryFile { get; init; } = string.Empty;
}

public sealed class PilotTokenImageAuditService
{
    public PilotTokenImageAuditResult Audit(string repositoryRoot, string? outputFolder = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Repository folder was not found: {root}");

        var tokenRoot = Path.Combine(root, "assets", "generated", "PilotBaseToken");
        if (!Directory.Exists(tokenRoot)) throw new DirectoryNotFoundException($"Pilot token folder was not found: {tokenRoot}");

        var output = Path.GetFullPath(outputFolder ?? Path.Combine(root, "ukb", "reports", "pilot-token-image-audit"));
        Directory.CreateDirectory(output);

        var shipSizes = LoadShipSizes(root);
        var analyzer = new ImageAnalyzer();
        var rows = new List<AuditRow>();

        foreach (var file in Directory.EnumerateFiles(tokenRoot, "*.png", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Relative(root, file);
            var shipFolder = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
            var factionFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file) ?? string.Empty) ?? string.Empty);
            var baseSize = ResolveBaseSize(shipFolder, shipSizes);
            var analysis = analyzer.Analyze(file);

            rows.Add(new AuditRow
            {
                Path = relative,
                FileName = Path.GetFileName(file),
                Faction = factionFolder,
                Ship = shipFolder,
                BaseSize = baseSize,
                Width = analysis.Width,
                Height = analysis.Height,
                VisibleLeft = analysis.VisibleBounds.Left,
                VisibleTop = analysis.VisibleBounds.Top,
                VisibleWidth = analysis.VisibleBounds.Width,
                VisibleHeight = analysis.VisibleBounds.Height,
                PaddingLeft = analysis.PaddingLeft,
                PaddingRight = analysis.PaddingRight,
                PaddingTop = analysis.PaddingTop,
                PaddingBottom = analysis.PaddingBottom,
                HorizontalCentreOffset = analysis.HorizontalCentreOffset,
                VerticalCentreOffset = analysis.VerticalCentreOffset,
                HasAlpha = analysis.HasAlphaChannel,
                TransparentPixels = analysis.TransparentPixels,
                TranslucentPixels = analysis.TranslucentPixels,
                OpaquePixels = analysis.OpaquePixels,
                Sha256 = analysis.Sha256,
                Warnings = analysis.Warnings
            });
        }

        if (rows.Count == 0) throw new InvalidOperationException($"No PNG files were found under: {tokenRoot}");

        var duplicateHashes = rows.GroupBy(row => row.Sha256, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToList();
        foreach (var group in duplicateHashes)
            foreach (var row in group)
                row.Warnings = AppendWarning(row.Warnings, "ExactDuplicate");

        var small = RecommendCanvas(rows.Where(row => row.BaseSize.Equals("Small", StringComparison.OrdinalIgnoreCase)));
        var large = RecommendCanvas(rows.Where(row => row.BaseSize.Equals("Large", StringComparison.OrdinalIgnoreCase)));
        var all = RecommendCanvas(rows);

        WriteAuditCsv(Path.Combine(output, "pilot-token-image-audit.csv"), rows);
        WriteCanvasCsv(Path.Combine(output, "canvas-sizes.csv"), rows);
        WriteAuditCsv(Path.Combine(output, "warnings.csv"), rows.Where(row => row.Warnings.Length > 0));
        WriteDuplicateCsv(Path.Combine(output, "exact-duplicates.csv"), duplicateHashes);
        WriteSummary(Path.Combine(output, "summary.txt"), rows, duplicateHashes.Count, small, large, all);

        return new PilotTokenImageAuditResult
        {
            ImagesScanned = rows.Count,
            UniqueCanvasSizes = rows.Select(row => $"{row.Width}x{row.Height}").Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            ImagesWithWarnings = rows.Count(row => row.Warnings.Length > 0),
            ExactDuplicateGroups = duplicateHashes.Count,
            RecommendedSmallCanvas = small,
            RecommendedLargeCanvas = large,
            OutputFolder = output,
            AuditCsv = Path.Combine(output, "pilot-token-image-audit.csv"),
            SummaryFile = Path.Combine(output, "summary.txt")
        };
    }

    private static Dictionary<string, string> LoadShipSizes(string root)
    {
        var candidates = new[]
        {
            Path.Combine(root, "assets", "source", "xwing-data", "data", "ships.js"),
            Path.Combine(root, "source", "xwing-data", "data", "ships.js"),
            Path.GetFullPath(Path.Combine(root, "..", "xwing-data", "data", "ships.js"))
        };
        var path = candidates.FirstOrDefault(File.Exists);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (path is null) return result;

        var text = File.ReadAllText(path, Encoding.UTF8);
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return result;
        using var document = JsonDocument.Parse(text[start..(end + 1)], new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        foreach (var ship in document.RootElement.EnumerateArray())
        {
            var name = ReadString(ship, "name");
            var xws = ReadString(ship, "xws");
            var size = ReadString(ship, "size");
            if (name.Length > 0) result[Normalise(name)] = NormaliseBaseSize(size);
            if (xws.Length > 0) result[Normalise(xws)] = NormaliseBaseSize(size);
        }
        return result;
    }

    private static string ResolveBaseSize(string shipFolder, IReadOnlyDictionary<string, string> sizes)
        => sizes.TryGetValue(Normalise(shipFolder), out var size) ? size : "Unknown";

    private static string NormaliseBaseSize(string value)
    {
        if (value.Contains("large", StringComparison.OrdinalIgnoreCase)) return "Large";
        if (value.Contains("small", StringComparison.OrdinalIgnoreCase)) return "Small";
        if (value.Contains("huge", StringComparison.OrdinalIgnoreCase) || value.Contains("epic", StringComparison.OrdinalIgnoreCase)) return "Epic";
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    }

    private static string RecommendCanvas(IEnumerable<AuditRow> source)
    {
        var group = source.GroupBy(row => (row.Width, row.Height))
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key.Width * group.Key.Height)
            .FirstOrDefault();
        return group is null ? "Unknown" : $"{group.Key.Width}x{group.Key.Height} ({group.Count()} images)";
    }

    private static void WriteAuditCsv(string path, IEnumerable<AuditRow> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("Path,FileName,Faction,Ship,BaseSize,Width,Height,VisibleLeft,VisibleTop,VisibleWidth,VisibleHeight,PaddingLeft,PaddingRight,PaddingTop,PaddingBottom,HorizontalCentreOffset,VerticalCentreOffset,HasAlpha,TransparentPixels,TranslucentPixels,OpaquePixels,Sha256,Warnings");
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',', new[]
            {
                Csv(row.Path), Csv(row.FileName), Csv(row.Faction), Csv(row.Ship), Csv(row.BaseSize), row.Width.ToString(), row.Height.ToString(),
                row.VisibleLeft.ToString(), row.VisibleTop.ToString(), row.VisibleWidth.ToString(), row.VisibleHeight.ToString(),
                row.PaddingLeft.ToString(), row.PaddingRight.ToString(), row.PaddingTop.ToString(), row.PaddingBottom.ToString(),
                row.HorizontalCentreOffset.ToString("0.###", CultureInfo.InvariantCulture), row.VerticalCentreOffset.ToString("0.###", CultureInfo.InvariantCulture),
                row.HasAlpha.ToString(), row.TransparentPixels.ToString(), row.TranslucentPixels.ToString(), row.OpaquePixels.ToString(), Csv(row.Sha256), Csv(row.Warnings)
            }));
        }
    }

    private static void WriteCanvasCsv(string path, IEnumerable<AuditRow> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("BaseSize,Width,Height,Count,PercentageWithinBaseSize");
        foreach (var baseGroup in rows.GroupBy(row => row.BaseSize).OrderBy(group => group.Key))
            foreach (var canvas in baseGroup.GroupBy(row => (row.Width, row.Height)).OrderByDescending(group => group.Count()))
                writer.WriteLine($"{Csv(baseGroup.Key)},{canvas.Key.Width},{canvas.Key.Height},{canvas.Count()},{(100.0 * canvas.Count() / baseGroup.Count()).ToString("0.00", CultureInfo.InvariantCulture)}");
    }

    private static void WriteDuplicateCsv(string path, IEnumerable<IGrouping<string, AuditRow>> groups)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("Sha256,Count,Path");
        foreach (var group in groups)
            foreach (var row in group.OrderBy(row => row.Path))
                writer.WriteLine($"{Csv(group.Key)},{group.Count()},{Csv(row.Path)}");
    }

    private static void WriteSummary(string path, IReadOnlyCollection<AuditRow> rows, int duplicateGroups, string small, string large, string all)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Pilot Token Image Audit");
        builder.AppendLine("=======================");
        builder.AppendLine();
        builder.AppendLine($"Images scanned:                 {rows.Count}");
        builder.AppendLine($"Unique canvas sizes:            {rows.Select(row => $"{row.Width}x{row.Height}").Distinct().Count()}");
        builder.AppendLine($"Images with warnings:           {rows.Count(row => row.Warnings.Length > 0)}");
        builder.AppendLine($"Images touching any edge:       {rows.Count(row => row.Warnings.Contains("Touches", StringComparison.Ordinal))}");
        builder.AppendLine($"Images without transparency:    {rows.Count(row => row.Warnings.Contains("NoTransparency", StringComparison.Ordinal))}");
        builder.AppendLine($"Exact duplicate groups:         {duplicateGroups}");
        builder.AppendLine();
        builder.AppendLine($"Recommended small canvas:       {small}");
        builder.AppendLine($"Recommended large canvas:       {large}");
        builder.AppendLine($"Most common canvas overall:     {all}");
        builder.AppendLine();
        builder.AppendLine("Recommendations are the most common observed canvas dimensions, not an automatic instruction to resize artwork.");
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static string AppendWarning(string existing, string warning) => existing.Length == 0 ? warning : existing + ";" + warning;
    private static string ReadString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static string Normalise(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Csv(string value) => value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private sealed class AuditRow
    {
        public string Path { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string Faction { get; init; } = string.Empty;
        public string Ship { get; init; } = string.Empty;
        public string BaseSize { get; init; } = string.Empty;
        public int Width { get; init; }
        public int Height { get; init; }
        public int VisibleLeft { get; init; }
        public int VisibleTop { get; init; }
        public int VisibleWidth { get; init; }
        public int VisibleHeight { get; init; }
        public int PaddingLeft { get; init; }
        public int PaddingRight { get; init; }
        public int PaddingTop { get; init; }
        public int PaddingBottom { get; init; }
        public double HorizontalCentreOffset { get; init; }
        public double VerticalCentreOffset { get; init; }
        public bool HasAlpha { get; init; }
        public long TransparentPixels { get; init; }
        public long TranslucentPixels { get; init; }
        public long OpaquePixels { get; init; }
        public string Sha256 { get; init; } = string.Empty;
        public string Warnings { get; set; } = string.Empty;
    }
}
