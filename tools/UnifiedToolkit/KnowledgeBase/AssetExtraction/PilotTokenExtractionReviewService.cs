using System.Text;
using System.Text.Json;
using SkiaSharp;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class PilotTokenExtractionReviewService
{
    private const string ReviewAssetsRelativePath = "KnowledgeBase/AssetExtraction/ReviewAssets";
    private const string CorrectionsRelativePath = "KnowledgeBase/AssetExtraction/Corrections/pilot-token-sheet-entry-corrections.json";

    private sealed record LearnedCrop(
        string SheetId,
        int Width,
        int Height,
        double X,
        double Y,
        double CropWidth,
        double CropHeight);

    private sealed class CorrectionDocument
    {
        public string SchemaVersion { get; set; } = "1.0.0";
        public List<SheetEntryCorrection> Corrections { get; set; } = new();
    }

    private sealed class SheetEntryCorrection
    {
        public string RepositoryPath { get; set; } = string.Empty;
        public List<string> ExcludeTargetIds { get; set; } = new();
        public string Reason { get; set; } = string.Empty;
    }

    private sealed record AppliedCorrection(
        string SheetId,
        string RepositoryPath,
        string TargetId,
        string DisplayName,
        string Reason);

    private sealed record MappingValidationRow(
        string TargetId,
        string DisplayName,
        string ShipId,
        string Faction,
        string Status,
        string RepositoryPath,
        string SheetId,
        string Details,
        bool IsMissing);

    public AssetExtractionPreparationResult Prepare(
        string repositoryRoot,
        string existingPlanFile,
        string? outputFolder = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var sourcePlanPath = Path.GetFullPath(existingPlanFile);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Repository folder was not found: {root}");
        }

        if (!File.Exists(sourcePlanPath))
        {
            throw new FileNotFoundException("Existing extraction plan was not found.", sourcePlanPath);
        }

        var output = Path.GetFullPath(
            outputFolder ?? Path.Combine(root, "ukb", "reports", "pilot-token-extraction-v2"));

        Directory.CreateDirectory(output);

        var old = ShipAssetJson.Read<AssetExtractionPlan>(sourcePlanPath);
        var originalAssignments = SnapshotAssignments(old);
        var corrections = LoadCorrections();
        var appliedCorrections = new List<AppliedCorrection>();
        ApplyCorrections(old, corrections, appliedCorrections);

        var layouts = old.Layouts.ToDictionary(x => x.LayoutId, StringComparer.OrdinalIgnoreCase);
        var learned = BuildLearnedCrops(root, old, layouts);

        var sheets = old.Sheets
            .Select(sheet =>
            {
                var size = ReadSize(root, sheet.RepositoryPath);

                return new AssetExtractionSheet
                {
                    SheetId = sheet.SheetId,
                    AssetId = sheet.AssetId,
                    RepositoryPath = sheet.RepositoryPath,
                    LayoutId = sheet.LayoutId,
                    Entries = sheet.Entries
                        .Select(entry => BuildEntry(
                            entry,
                            layouts.GetValueOrDefault(sheet.LayoutId),
                            size,
                            learned))
                        .ToList()
                };
            })
            .ToList();

        var plan = new AssetExtractionPlan
        {
            SchemaVersion = "1.1.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            AssetRole = old.AssetRole,
            Layouts = old.Layouts,
            Sheets = sheets
        };

        var planPath = Path.Combine(output, "pilot-token-extraction-plan.v2.json");
        ShipAssetJson.Write(planPath, plan);

        CopyReviewAssets(output);
        WriteReviewData(root, output, plan);
        WriteCorrectionReport(output, appliedCorrections);
        var validation = BuildMappingValidation(old, originalAssignments, appliedCorrections);
        WriteMappingValidation(output, validation);

        var htmlPath = Path.Combine(output, "index.html");

        return new AssetExtractionPreparationResult
        {
            Sheets = sheets.Count,
            Entries = sheets.Sum(x => x.Entries.Count),
            UnresolvedSheets = sheets.Count(x => x.Entries.Any(e => !HasCrop(e))),
            UnassignedMappings = validation.Count(x => x.Status == "UnassignedNeedsSource"),
            PlanFile = planPath,
            HtmlFile = htmlPath,
            OutputFolder = output
        };
    }

    private static Dictionary<string, List<(string SheetId, string RepositoryPath, AssetExtractionEntry Entry)>> SnapshotAssignments(
        AssetExtractionPlan plan)
    {
        return plan.Sheets
            .SelectMany(sheet => sheet.Entries.Select(entry => (sheet.SheetId, sheet.RepositoryPath, Entry: entry)))
            .GroupBy(x => x.Entry.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static List<MappingValidationRow> BuildMappingValidation(
        AssetExtractionPlan correctedPlan,
        IReadOnlyDictionary<string, List<(string SheetId, string RepositoryPath, AssetExtractionEntry Entry)>> originalAssignments,
        IReadOnlyCollection<AppliedCorrection> appliedCorrections)
    {
        var active = SnapshotAssignments(correctedPlan);
        var rows = new List<MappingValidationRow>();

        foreach (var sheet in correctedPlan.Sheets)
        {
            foreach (var entry in sheet.Entries)
            {
                var assignmentCount = active.TryGetValue(entry.TargetId, out var assignments)
                    ? assignments.Count
                    : 0;

                var status = assignmentCount > 1
                    ? "DuplicateAssignment"
                    : sheet.Entries.Count > 1
                        ? "SharedSourceNeedsVisualConfirmation"
                        : "UniqueAssignment";

                var details = assignmentCount > 1
                    ? $"Pilot is assigned to {assignmentCount} source sheets."
                    : sheet.Entries.Count > 1
                        ? $"Source sheet has {sheet.Entries.Count} pilot entries; confirm each token is visibly present."
                        : "One pilot is assigned to one source sheet.";

                rows.Add(new MappingValidationRow(
                    entry.TargetId,
                    entry.DisplayName,
                    entry.ShipId,
                    entry.Faction,
                    status,
                    sheet.RepositoryPath,
                    sheet.SheetId,
                    details,
                    IsMissing: false));
            }
        }

        foreach (var correction in appliedCorrections)
        {
            if (active.TryGetValue(correction.TargetId, out var reassigned) && reassigned.Count > 0)
            {
                rows.Add(new MappingValidationRow(
                    correction.TargetId,
                    correction.DisplayName,
                    reassigned[0].Entry.ShipId,
                    reassigned[0].Entry.Faction,
                    "ReassignedAfterCorrection",
                    reassigned[0].RepositoryPath,
                    reassigned[0].SheetId,
                    $"Removed from {correction.SheetId}; another active source assignment remains. {correction.Reason}",
                    IsMissing: false));
                continue;
            }

            var original = originalAssignments.TryGetValue(correction.TargetId, out var prior)
                ? prior.FirstOrDefault()
                : default;

            rows.Add(new MappingValidationRow(
                correction.TargetId,
                correction.DisplayName,
                original.Entry?.ShipId ?? string.Empty,
                original.Entry?.Faction ?? string.Empty,
                "UnassignedNeedsSource",
                string.Empty,
                string.Empty,
                $"Incorrect assignment removed from {correction.SheetId}. No replacement source is currently assigned. This is unresolved, not confirmed missing. {correction.Reason}",
                IsMissing: false));
        }

        return rows
            .OrderBy(x => x.Status, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void WriteMappingValidation(
        string outputFolder,
        IReadOnlyCollection<MappingValidationRow> rows)
    {
        static string Csv(string value) =>
            $"\"{value.Replace("\"", "\"\"")}\"";

        var csv = new List<string>
        {
            "targetId,displayName,shipId,faction,status,repositoryPath,sheetId,isMissing,details"
        };

        csv.AddRange(rows.Select(x => string.Join(',',
            Csv(x.TargetId),
            Csv(x.DisplayName),
            Csv(x.ShipId),
            Csv(x.Faction),
            Csv(x.Status),
            Csv(x.RepositoryPath),
            Csv(x.SheetId),
            x.IsMissing ? "true" : "false",
            Csv(x.Details))));

        File.WriteAllLines(
            Path.Combine(outputFolder, "pilot-token-mapping-validation.csv"),
            csv,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(outputFolder, "pilot-token-mapping-validation.json"),
            JsonSerializer.Serialize(rows, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }),
            new UTF8Encoding(false));
    }

    private static CorrectionDocument LoadCorrections()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            CorrectionsRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Pilot-token sheet correction data was not copied to the build output.",
                path);
        }

        var document = JsonSerializer.Deserialize<CorrectionDocument>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return document ?? new CorrectionDocument();
    }

    private static void ApplyCorrections(
        AssetExtractionPlan plan,
        CorrectionDocument document,
        ICollection<AppliedCorrection> applied)
    {
        var corrections = document.Corrections
            .Where(x => !string.IsNullOrWhiteSpace(x.RepositoryPath))
            .ToDictionary(
                x => NormalizePath(x.RepositoryPath),
                x => x,
                StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in plan.Sheets)
        {
            if (!corrections.TryGetValue(NormalizePath(sheet.RepositoryPath), out var correction))
            {
                continue;
            }

            var excluded = correction.ExcludeTargetIds
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in sheet.Entries.Where(x => excluded.Contains(x.TargetId)).ToList())
            {
                applied.Add(new AppliedCorrection(
                    sheet.SheetId,
                    sheet.RepositoryPath,
                    entry.TargetId,
                    entry.DisplayName,
                    correction.Reason));

                sheet.Entries.Remove(entry);
            }
        }

        plan.Sheets.RemoveAll(x => x.Entries.Count == 0);
    }

    private static string NormalizePath(string value) =>
        value.Replace('\\', '/').Trim().TrimStart('/');

    private static void WriteCorrectionReport(
        string outputFolder,
        IReadOnlyCollection<AppliedCorrection> corrections)
    {
        static string Csv(string value) =>
            $"\"{value.Replace("\"", "\"\"")}\"";

        var lines = new List<string>
        {
            "sheetId,repositoryPath,targetId,displayName,reason"
        };

        lines.AddRange(corrections.Select(x => string.Join(',',
            Csv(x.SheetId),
            Csv(x.RepositoryPath),
            Csv(x.TargetId),
            Csv(x.DisplayName),
            Csv(x.Reason))));

        File.WriteAllLines(
            Path.Combine(outputFolder, "pilot-token-extraction-corrections-applied.csv"),
            lines,
            new UTF8Encoding(false));
    }

    private static List<LearnedCrop> BuildLearnedCrops(
        string repositoryRoot,
        AssetExtractionPlan plan,
        IReadOnlyDictionary<string, AssetExtractionLayout> layouts)
    {
        var learned = new List<LearnedCrop>();

        foreach (var sheet in plan.Sheets.Where(x => x.Entries.Count == 1))
        {
            var entry = sheet.Entries[0];

            if (!TryGetCrop(entry, layouts.GetValueOrDefault(sheet.LayoutId), out var crop))
            {
                continue;
            }

            var size = ReadSize(repositoryRoot, sheet.RepositoryPath);
            if (size is null)
            {
                continue;
            }

            learned.Add(new LearnedCrop(
                sheet.SheetId,
                size.Value.Width,
                size.Value.Height,
                crop.X,
                crop.Y,
                crop.Width,
                crop.Height));
        }

        return learned;
    }

    private static AssetExtractionEntry BuildEntry(
        AssetExtractionEntry entry,
        AssetExtractionLayout? layout,
        (int Width, int Height)? size,
        IReadOnlyCollection<LearnedCrop> learned)
    {
        TryGetCrop(entry, layout, out var existing);

        LearnedCrop? suggestion = null;
        double confidence = 0;

        if (existing.Width <= 0 && size is not null)
        {
            suggestion = learned.FirstOrDefault(x =>
                x.Width == size.Value.Width &&
                x.Height == size.Value.Height);

            if (suggestion is not null)
            {
                confidence = 1.0;
            }
            else
            {
                var ratio = size.Value.Width / (double)size.Value.Height;

                suggestion = learned
                    .OrderBy(x => Math.Abs((x.Width / (double)x.Height) - ratio))
                    .FirstOrDefault();

                if (suggestion is not null)
                {
                    var delta = Math.Abs(
                        (suggestion.Width / (double)suggestion.Height) - ratio);

                    if (delta <= 0.002)
                    {
                        confidence = 0.85;
                    }
                    else
                    {
                        suggestion = null;
                    }
                }
            }
        }

        return new AssetExtractionEntry
        {
            EntityId = entry.EntityId,
            TargetId = entry.TargetId,
            DisplayName = entry.DisplayName,
            ShipId = entry.ShipId,
            Faction = entry.Faction,
            PilotSkill = entry.PilotSkill,
            SquadPointCost = entry.SquadPointCost,
            Row = entry.Row,
            Column = entry.Column,
            CropX = existing.Width > 0 ? existing.X : entry.CropX,
            CropY = existing.Height > 0 ? existing.Y : entry.CropY,
            CropWidth = existing.Width > 0 ? existing.Width : entry.CropWidth,
            CropHeight = existing.Height > 0 ? existing.Height : entry.CropHeight,
            SuggestionSource = suggestion?.SheetId,
            SuggestionConfidence = suggestion is null ? null : confidence,
            SuggestedCropX = suggestion?.X,
            SuggestedCropY = suggestion?.Y,
            SuggestedCropWidth = suggestion?.CropWidth,
            SuggestedCropHeight = suggestion?.CropHeight
        };
    }

    private static bool TryGetCrop(
        AssetExtractionEntry entry,
        AssetExtractionLayout? layout,
        out (double X, double Y, double Width, double Height) crop)
    {
        if (HasCrop(entry))
        {
            crop = (
                entry.CropX!.Value,
                entry.CropY!.Value,
                entry.CropWidth!.Value,
                entry.CropHeight!.Value);

            return true;
        }

        if (layout is not null &&
            entry.Row.HasValue &&
            entry.Column.HasValue &&
            layout.Rows > 0 &&
            layout.Columns > 0)
        {
            var width =
                (1 - layout.MarginLeft - layout.MarginRight -
                 (layout.Columns - 1) * layout.HorizontalGap) /
                layout.Columns;

            var height =
                (1 - layout.MarginTop - layout.MarginBottom -
                 (layout.Rows - 1) * layout.VerticalGap) /
                layout.Rows;

            crop = (
                layout.MarginLeft + entry.Column.Value * (width + layout.HorizontalGap),
                layout.MarginTop + entry.Row.Value * (height + layout.VerticalGap),
                width,
                height);

            return width > 0 && height > 0;
        }

        crop = default;
        return false;
    }

    private static bool HasCrop(AssetExtractionEntry entry) =>
        entry.CropX.HasValue &&
        entry.CropY.HasValue &&
        entry.CropWidth > 0 &&
        entry.CropHeight > 0;

    private static (int Width, int Height)? ReadSize(
        string repositoryRoot,
        string repositoryPath)
    {
        var path = ResolveRepositoryFile(repositoryRoot, repositoryPath);
        if (!File.Exists(path))
        {
            return null;
        }

        using var codec = SKCodec.Create(path);
        return codec is null
            ? null
            : (codec.Info.Width, codec.Info.Height);
    }

    private static string ResolveRepositoryFile(
        string repositoryRoot,
        string repositoryPath)
    {
        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            repositoryPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void CopyReviewAssets(string outputFolder)
    {
        var sourceFolder = Path.Combine(
            AppContext.BaseDirectory,
            ReviewAssetsRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException(
                "Pilot-token review UI assets were not copied to the build output. " +
                $"Expected folder: {sourceFolder}");
        }

        foreach (var fileName in new[] { "index.html", "review.css", "review.js" })
        {
            var source = Path.Combine(sourceFolder, fileName);
            var destination = Path.Combine(outputFolder, fileName);

            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    $"Required pilot-token review UI asset was not found: {source}",
                    source);
            }

            File.Copy(source, destination, overwrite: true);
        }
    }

    private static void WriteReviewData(
        string repositoryRoot,
        string outputFolder,
        AssetExtractionPlan plan)
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in plan.Sheets)
        {
            var absolute = ResolveRepositoryFile(repositoryRoot, sheet.RepositoryPath);
            sources[sheet.SheetId] = Path.GetRelativePath(outputFolder, absolute)
                .Replace(Path.DirectorySeparatorChar, '/');
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var planJson = JsonSerializer.Serialize(plan, options);
        var sourcesJson = JsonSerializer.Serialize(sources, options);

        var script = new StringBuilder()
            .Append("window.pilotTokenReviewData = ")
            .Append("{\"plan\":")
            .Append(planJson)
            .Append(",\"sources\":")
            .Append(sourcesJson)
            .Append("};")
            .AppendLine()
            .ToString();

        File.WriteAllText(
            Path.Combine(outputFolder, "review-data.js"),
            script,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
