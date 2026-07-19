using System.Text;
using System.Text.Json;
using SkiaSharp;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class PilotTokenExtractionResult
{
    public int SheetsInPlan { get; init; }
    public int CompleteSheets { get; init; }
    public int SkippedIncompleteSheets { get; init; }
    public int GeneratedTokens { get; init; }
    public int FailedTokens { get; init; }
    public string OutputFolder { get; init; } = string.Empty;
    public string ManifestFile { get; init; } = string.Empty;
    public string ReportFile { get; init; } = string.Empty;
}

public sealed class PilotTokenExtractionManifest
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourcePlan { get; init; } = string.Empty;
    public List<PilotTokenExtractionManifestEntry> Tokens { get; init; } = new();
    public List<PilotTokenExtractionSkippedSheet> SkippedSheets { get; init; } = new();
    public List<PilotTokenExtractionFailure> Failures { get; init; } = new();
}

public sealed class PilotTokenExtractionManifestEntry
{
    public string EntityId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string SourceSheetId { get; init; } = string.Empty;
    public string SourceRepositoryPath { get; init; } = string.Empty;
    public string OutputRepositoryPath { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class PilotTokenExtractionSkippedSheet
{
    public string SheetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public List<string> Pilots { get; init; } = new();
    public string Reason { get; init; } = string.Empty;
}

public sealed class PilotTokenExtractionFailure
{
    public string SheetId { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

public sealed class PilotTokenExtractionService
{
    public PilotTokenExtractionResult Extract(string repositoryRoot, string planFile, string? outputFolder = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Repository folder was not found: {root}");

        var planPath = Path.GetFullPath(planFile);
        if (!File.Exists(planPath))
            throw new FileNotFoundException("Pilot token extraction plan was not found.", planPath);

        var output = Path.GetFullPath(outputFolder ?? Path.Combine(root, "assets", "generated", "PilotBaseToken"));
        Directory.CreateDirectory(output);

        var plan = ShipAssetJson.Read<AssetExtractionPlan>(planPath);
        var layouts = plan.Layouts.ToDictionary(x => x.LayoutId, StringComparer.OrdinalIgnoreCase);
        var manifest = new PilotTokenExtractionManifest
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourcePlan = ToRepositoryRelative(root, planPath)
        };

        var completeSheets = 0;
        foreach (var sheet in plan.Sheets)
        {
            var complete = sheet.Entries.Count > 0 && sheet.Entries.All(IsEntryComplete);
            if (!complete)
            {
                manifest.SkippedSheets.Add(new PilotTokenExtractionSkippedSheet
                {
                    SheetId = sheet.SheetId,
                    RepositoryPath = sheet.RepositoryPath,
                    Pilots = sheet.Entries.Select(x => x.DisplayName).ToList(),
                    Reason = "One or more pilot crop rectangles are incomplete."
                });
                continue;
            }

            completeSheets++;
            if (!layouts.TryGetValue(sheet.LayoutId, out var layout))
            {
                AddSheetFailure(manifest, sheet, $"Layout '{sheet.LayoutId}' was not found.");
                continue;
            }

            var sourcePath = Path.GetFullPath(Path.Combine(root, sheet.RepositoryPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(sourcePath))
            {
                AddSheetFailure(manifest, sheet, $"Source image was not found: {sourcePath}");
                continue;
            }

            try
            {
                using var source = SKBitmap.Decode(sourcePath)
                    ?? throw new InvalidOperationException("The source image format could not be decoded.");
                foreach (var entry in sheet.Entries)
                {
                    try
                    {
                        var rectangle = CalculateRectangle(layout, entry, source.Width, source.Height);
                        var faction = SafePathSegment(entry.Faction);
                        var ship = SafePathSegment(entry.ShipId);
                        var fileName = $"{SafePathSegment(entry.TargetId)}__{SafePathSegment(entry.EntityId)}.png";
                        var destinationFolder = Path.Combine(output, faction, ship);
                        Directory.CreateDirectory(destinationFolder);
                        var destination = Path.Combine(destinationFolder, fileName);

                        using var cropped = new SKBitmap(
                            rectangle.Width,
                            rectangle.Height,
                            SKColorType.Bgra8888,
                            SKAlphaType.Premul);
                        using (var canvas = new SKCanvas(cropped))
                        {
                            canvas.Clear(SKColors.Transparent);
                            var sourceRectangle = new SKRectI(
                                rectangle.X,
                                rectangle.Y,
                                rectangle.X + rectangle.Width,
                                rectangle.Y + rectangle.Height);
                            var destinationRectangle = new SKRectI(0, 0, rectangle.Width, rectangle.Height);
                            canvas.DrawBitmap(source, sourceRectangle, destinationRectangle);
                            canvas.Flush();
                        }

                        using var image = SKImage.FromBitmap(cropped);
                        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
                            ?? throw new InvalidOperationException("The cropped image could not be encoded as PNG.");
                        using var destinationStream = File.Create(destination);
                        encoded.SaveTo(destinationStream);

                        manifest.Tokens.Add(new PilotTokenExtractionManifestEntry
                        {
                            EntityId = entry.EntityId,
                            TargetId = entry.TargetId,
                            DisplayName = entry.DisplayName,
                            Faction = entry.Faction,
                            ShipId = entry.ShipId,
                            SourceSheetId = sheet.SheetId,
                            SourceRepositoryPath = sheet.RepositoryPath,
                            OutputRepositoryPath = ToRepositoryRelative(root, destination),
                            X = rectangle.X,
                            Y = rectangle.Y,
                            Width = rectangle.Width,
                            Height = rectangle.Height
                        });
                    }
                    catch (Exception exception)
                    {
                        manifest.Failures.Add(new PilotTokenExtractionFailure
                        {
                            SheetId = sheet.SheetId,
                            EntityId = entry.EntityId,
                            DisplayName = entry.DisplayName,
                            Error = exception.Message
                        });
                    }
                }
            }
            catch (Exception exception)
            {
                AddSheetFailure(manifest, sheet, $"Could not load source image: {exception.Message}");
            }
        }

        var manifestPath = Path.Combine(output, "pilot-token-extraction-manifest.json");
        ShipAssetJson.Write(manifestPath, manifest);
        var reportPath = Path.Combine(output, "pilot-token-extraction-report.csv");
        WriteCsv(reportPath, manifest);

        return new PilotTokenExtractionResult
        {
            SheetsInPlan = plan.Sheets.Count,
            CompleteSheets = completeSheets,
            SkippedIncompleteSheets = manifest.SkippedSheets.Count,
            GeneratedTokens = manifest.Tokens.Count,
            FailedTokens = manifest.Failures.Count,
            OutputFolder = output,
            ManifestFile = manifestPath,
            ReportFile = reportPath
        };
    }

    private static bool IsEntryComplete(AssetExtractionEntry entry)
        => HasDirectCrop(entry) || (entry.Row.HasValue && entry.Column.HasValue);

    private static bool HasDirectCrop(AssetExtractionEntry entry)
        => entry.CropX.HasValue && entry.CropY.HasValue && entry.CropWidth.HasValue && entry.CropHeight.HasValue
           && entry.CropWidth.Value > 0 && entry.CropHeight.Value > 0;

    private static PixelRectangle CalculateRectangle(AssetExtractionLayout layout, AssetExtractionEntry entry, int imageWidth, int imageHeight)
    {
        if (HasDirectCrop(entry))
        {
            var cropX = (int)Math.Round(entry.CropX!.Value * imageWidth);
            var cropY = (int)Math.Round(entry.CropY!.Value * imageHeight);
            var cropWidth = (int)Math.Round(entry.CropWidth!.Value * imageWidth);
            var cropHeight = (int)Math.Round(entry.CropHeight!.Value * imageHeight);
            cropX = Math.Clamp(cropX, 0, Math.Max(0, imageWidth - 1));
            cropY = Math.Clamp(cropY, 0, Math.Max(0, imageHeight - 1));
            cropWidth = Math.Clamp(cropWidth, 1, imageWidth - cropX);
            cropHeight = Math.Clamp(cropHeight, 1, imageHeight - cropY);
            return new PixelRectangle(cropX, cropY, cropWidth, cropHeight);
        }

        if (layout.Rows < 1 || layout.Columns < 1)
            throw new InvalidOperationException("Rows and columns must both be at least 1.");
        if (!entry.Row.HasValue || !entry.Column.HasValue)
            throw new InvalidOperationException("Pilot row and column are incomplete.");
        if (entry.Row < 0 || entry.Row >= layout.Rows || entry.Column < 0 || entry.Column >= layout.Columns)
            throw new InvalidOperationException($"Position row {entry.Row}, column {entry.Column} is outside the {layout.Rows}x{layout.Columns} grid.");

        var usableWidth = 1d - layout.MarginLeft - layout.MarginRight - ((layout.Columns - 1) * layout.HorizontalGap);
        var usableHeight = 1d - layout.MarginTop - layout.MarginBottom - ((layout.Rows - 1) * layout.VerticalGap);
        if (usableWidth <= 0 || usableHeight <= 0)
            throw new InvalidOperationException("Margins and gaps leave no usable crop area.");

        var cellWidth = usableWidth / layout.Columns;
        var cellHeight = usableHeight / layout.Rows;
        var left = layout.MarginLeft + entry.Column.Value * (cellWidth + layout.HorizontalGap);
        var top = layout.MarginTop + entry.Row.Value * (cellHeight + layout.VerticalGap);

        var x = Math.Clamp((int)Math.Round(left * imageWidth), 0, imageWidth - 1);
        var y = Math.Clamp((int)Math.Round(top * imageHeight), 0, imageHeight - 1);
        var right = Math.Clamp((int)Math.Round((left + cellWidth) * imageWidth), x + 1, imageWidth);
        var bottom = Math.Clamp((int)Math.Round((top + cellHeight) * imageHeight), y + 1, imageHeight);
        return new PixelRectangle(x, y, right - x, bottom - y);
    }

    private static void AddSheetFailure(PilotTokenExtractionManifest manifest, AssetExtractionSheet sheet, string error)
    {
        foreach (var entry in sheet.Entries)
        {
            manifest.Failures.Add(new PilotTokenExtractionFailure
            {
                SheetId = sheet.SheetId,
                EntityId = entry.EntityId,
                DisplayName = entry.DisplayName,
                Error = error
            });
        }
    }

    private static void WriteCsv(string path, PilotTokenExtractionManifest manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("status,sheetId,entityId,targetId,displayName,sourceRepositoryPath,outputRepositoryPath,x,y,width,height,message");
        foreach (var item in manifest.Tokens)
            sb.AppendLine(string.Join(',', "generated", Csv(item.SourceSheetId), Csv(item.EntityId), Csv(item.TargetId), Csv(item.DisplayName), Csv(item.SourceRepositoryPath), Csv(item.OutputRepositoryPath), item.X, item.Y, item.Width, item.Height, ""));
        foreach (var item in manifest.SkippedSheets)
            sb.AppendLine(string.Join(',', "skipped", Csv(item.SheetId), "", "", Csv(string.Join("; ", item.Pilots)), Csv(item.RepositoryPath), "", "", "", "", "", Csv(item.Reason)));
        foreach (var item in manifest.Failures)
            sb.AppendLine(string.Join(',', "failed", Csv(item.SheetId), Csv(item.EntityId), "", Csv(item.DisplayName), "", "", "", "", "", "", Csv(item.Error)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static string SafePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? string.Empty).Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : char.ToLowerInvariant(c)).ToArray();
        var result = new string(chars).Trim('-');
        while (result.Contains("--", StringComparison.Ordinal)) result = result.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    private static string ToRepositoryRelative(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
    private readonly record struct PixelRectangle(int X, int Y, int Width, int Height);

}
