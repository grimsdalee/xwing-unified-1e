using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class PilotTokenRecoveryResult
{
    public int AssignmentsInPlan { get; init; }
    public int RecoveredTokens { get; init; }
    public int ExistingTokensSkipped { get; init; }
    public int FailedTokens { get; init; }
    public int GenerationRequiredPilots { get; init; }
    public string OutputFolder { get; init; } = string.Empty;
    public string ManifestFile { get; init; } = string.Empty;
    public string ReportFile { get; init; } = string.Empty;
    public string GenerationRequiredFile { get; init; } = string.Empty;
}

public sealed class PilotTokenRecoveryPlan
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset? GeneratedUtc { get; init; }
    public string SourceCatalogue { get; init; } = string.Empty;
    public List<PilotTokenRecoveryAssignment> Assignments { get; init; } = new();
    public List<string> GenerationRequiredPilotIds { get; init; } = new();
    public List<string> RemainingPilotIds { get; init; } = new();
}

public sealed class PilotTokenRecoveryAssignment
{
    public string AssignmentId { get; init; } = string.Empty;
    public string PilotId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string Ship { get; init; } = string.Empty;
    public int? Skill { get; init; }
    public int? Points { get; init; }
    public string ImageId { get; init; } = string.Empty;
    public string SourceRepositoryPath { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed class PilotTokenRecoveryManifest
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourcePlan { get; init; } = string.Empty;
    public List<PilotTokenRecoveryManifestEntry> Tokens { get; init; } = new();
    public List<PilotTokenRecoverySkippedEntry> Skipped { get; init; } = new();
    public List<PilotTokenRecoveryFailure> Failures { get; init; } = new();
    public List<string> GenerationRequiredPilotIds { get; init; } = new();
}

public sealed class PilotTokenRecoveryManifestEntry
{
    public string AssignmentId { get; init; } = string.Empty;
    public string PilotId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string Ship { get; init; } = string.Empty;
    public string SourceRepositoryPath { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public string OutputRepositoryPath { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class PilotTokenRecoverySkippedEntry
{
    public string PilotId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ExistingRepositoryPath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class PilotTokenRecoveryFailure
{
    public string AssignmentId { get; init; } = string.Empty;
    public string PilotId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string SourceRepositoryPath { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

public sealed class PilotTokenRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    public PilotTokenRecoveryResult Recover(
        string repositoryRoot,
        string recoveryPlanFile,
        string? outputFolder = null,
        bool overwrite = false)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Repository folder was not found: {root}");

        var planPath = Path.GetFullPath(recoveryPlanFile);
        if (!File.Exists(planPath))
            throw new FileNotFoundException("Pilot token recovery plan was not found.", planPath);

        var plan = JsonSerializer.Deserialize<PilotTokenRecoveryPlan>(File.ReadAllText(planPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("The pilot token recovery plan could not be parsed.");

        var output = Path.GetFullPath(outputFolder ?? Path.Combine(root, "assets", "generated", "PilotBaseToken"));
        Directory.CreateDirectory(output);

        var manifest = new PilotTokenRecoveryManifest
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourcePlan = RepositoryRelative(root, planPath),
            GenerationRequiredPilotIds = plan.GenerationRequiredPilotIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        foreach (var assignment in plan.Assignments)
        {
            try
            {
                RecoverAssignment(root, output, assignment, overwrite, manifest);
            }
            catch (Exception exception)
            {
                manifest.Failures.Add(new PilotTokenRecoveryFailure
                {
                    AssignmentId = assignment.AssignmentId,
                    PilotId = assignment.PilotId,
                    DisplayName = assignment.DisplayName,
                    SourceRepositoryPath = assignment.SourceRepositoryPath,
                    Error = exception.Message
                });
            }
        }

        var manifestPath = Path.Combine(output, "pilot-token-recovery-manifest.json");
        WriteJson(manifestPath, manifest);

        var reportPath = Path.Combine(output, "pilot-token-recovery-report.csv");
        WriteReport(reportPath, manifest);

        var generationRequiredPath = Path.Combine(output, "pilot-token-generation-required.json");
        WriteJson(generationRequiredPath, new
        {
            schemaVersion = "1.0.0",
            generatedUtc = DateTimeOffset.UtcNow,
            sourcePlan = RepositoryRelative(root, planPath),
            pilotIds = manifest.GenerationRequiredPilotIds
        });

        return new PilotTokenRecoveryResult
        {
            AssignmentsInPlan = plan.Assignments.Count,
            RecoveredTokens = manifest.Tokens.Count,
            ExistingTokensSkipped = manifest.Skipped.Count,
            FailedTokens = manifest.Failures.Count,
            GenerationRequiredPilots = manifest.GenerationRequiredPilotIds.Count,
            OutputFolder = output,
            ManifestFile = manifestPath,
            ReportFile = reportPath,
            GenerationRequiredFile = generationRequiredPath
        };
    }

    private static void RecoverAssignment(
        string root,
        string output,
        PilotTokenRecoveryAssignment assignment,
        bool overwrite,
        PilotTokenRecoveryManifest manifest)
    {
        if (!assignment.Status.Equals("RecoveredSource", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Assignment status '{assignment.Status}' is not recoverable.");
        if (string.IsNullOrWhiteSpace(assignment.PilotId))
            throw new InvalidDataException("PilotId is empty.");
        if (string.IsNullOrWhiteSpace(assignment.TargetId))
            throw new InvalidDataException("TargetId is empty.");
        if (string.IsNullOrWhiteSpace(assignment.Faction) || string.IsNullOrWhiteSpace(assignment.Ship))
            throw new InvalidDataException("Faction or ship is empty.");
        if (assignment.Width <= 0 || assignment.Height <= 0)
            throw new InvalidDataException("Crop width and height must be greater than zero.");

        var sourcePath = ResolveRepositoryPath(root, assignment.SourceRepositoryPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source image was not found.", sourcePath);

        if (!string.IsNullOrWhiteSpace(assignment.SourceSha256))
        {
            var actualHash = CalculateSha256(sourcePath);
            if (!actualHash.Equals(assignment.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Source SHA-256 mismatch. Expected {assignment.SourceSha256}; found {actualHash}.");
        }

        var factionFolder = SafePathSegment(assignment.Faction);
        var shipFolder = SafePathSegment(assignment.Ship);
        var targetName = SafePathSegment(assignment.TargetId);
        var destinationFolder = Path.Combine(output, factionFolder, shipFolder);
        Directory.CreateDirectory(destinationFolder);

        var existing = Directory.EnumerateFiles(destinationFolder, "*.png", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => GetPilotFileKey(path).Equals(Normalise(assignment.TargetId), StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !overwrite)
        {
            manifest.Skipped.Add(new PilotTokenRecoverySkippedEntry
            {
                PilotId = assignment.PilotId,
                DisplayName = assignment.DisplayName,
                ExistingRepositoryPath = RepositoryRelative(root, existing),
                Reason = "A token for this pilot already exists. Use --overwrite to replace it."
            });
            return;
        }

        var deterministicId = ShortHash(assignment.PilotId);
        var destination = existing ?? Path.Combine(destinationFolder, $"{targetName}__pilot-{deterministicId}.png");

        using var source = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidOperationException("The source image format could not be decoded.");
        var rectangle = CalculateRectangle(assignment, source.Width, source.Height);

        using var cropped = new SKBitmap(rectangle.Width, rectangle.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                source,
                new SKRectI(rectangle.X, rectangle.Y, rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height),
                new SKRectI(0, 0, rectangle.Width, rectangle.Height));
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(cropped);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The cropped image could not be encoded as PNG.");
        using var stream = File.Create(destination);
        encoded.SaveTo(stream);

        manifest.Tokens.Add(new PilotTokenRecoveryManifestEntry
        {
            AssignmentId = assignment.AssignmentId,
            PilotId = assignment.PilotId,
            TargetId = assignment.TargetId,
            DisplayName = assignment.DisplayName,
            Faction = assignment.Faction,
            Ship = assignment.Ship,
            SourceRepositoryPath = assignment.SourceRepositoryPath,
            SourceSha256 = assignment.SourceSha256,
            OutputRepositoryPath = RepositoryRelative(root, destination),
            X = rectangle.X,
            Y = rectangle.Y,
            Width = rectangle.Width,
            Height = rectangle.Height
        });
    }

    private static PixelRectangle CalculateRectangle(PilotTokenRecoveryAssignment assignment, int imageWidth, int imageHeight)
    {
        var left = Clamp01(assignment.X);
        var top = Clamp01(assignment.Y);
        var right = Clamp01(assignment.X + assignment.Width);
        var bottom = Clamp01(assignment.Y + assignment.Height);

        var x = Math.Clamp((int)Math.Round(left * imageWidth, MidpointRounding.AwayFromZero), 0, imageWidth - 1);
        var y = Math.Clamp((int)Math.Round(top * imageHeight, MidpointRounding.AwayFromZero), 0, imageHeight - 1);
        var rightPixel = Math.Clamp((int)Math.Round(right * imageWidth, MidpointRounding.AwayFromZero), x + 1, imageWidth);
        var bottomPixel = Math.Clamp((int)Math.Round(bottom * imageHeight, MidpointRounding.AwayFromZero), y + 1, imageHeight);
        return new PixelRectangle(x, y, rightPixel - x, bottomPixel - y);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

    private static string ResolveRepositoryPath(string root, string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            throw new InvalidDataException("SourceRepositoryPath is empty.");
        var combined = Path.GetFullPath(Path.Combine(root, repositoryPath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SourceRepositoryPath resolves outside the repository root.");
        return combined;
    }

    private static string CalculateSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ShortHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];

    private static string GetPilotFileKey(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var marker = fileName.LastIndexOf("__pilot-", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) fileName = fileName[..marker];
        return Normalise(fileName);
    }

    private static string SafePathSegment(string value)
    {
        // PilotBaseToken uses the original compact folder convention:
        // firstorder/tiesilencer rather than first-order/tie-silencer.
        var result = new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    private static string Normalise(string value)
        => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string RepositoryRelative(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static void WriteJson<T>(string path, T value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));

    private static void WriteReport(string path, PilotTokenRecoveryManifest manifest)
    {
        var lines = new List<string>
        {
            "status,pilotId,targetId,displayName,faction,ship,sourceRepositoryPath,outputRepositoryPath,x,y,width,height,message"
        };
        lines.AddRange(manifest.Tokens.Select(entry => string.Join(',',
            "recovered", Csv(entry.PilotId), Csv(entry.TargetId), Csv(entry.DisplayName), Csv(entry.Faction), Csv(entry.Ship),
            Csv(entry.SourceRepositoryPath), Csv(entry.OutputRepositoryPath), entry.X.ToString(CultureInfo.InvariantCulture),
            entry.Y.ToString(CultureInfo.InvariantCulture), entry.Width.ToString(CultureInfo.InvariantCulture),
            entry.Height.ToString(CultureInfo.InvariantCulture), "")));
        lines.AddRange(manifest.Skipped.Select(entry => string.Join(',',
            "skipped", Csv(entry.PilotId), "", Csv(entry.DisplayName), "", "", "", Csv(entry.ExistingRepositoryPath),
            "", "", "", "", Csv(entry.Reason))));
        lines.AddRange(manifest.Failures.Select(entry => string.Join(',',
            "failed", Csv(entry.PilotId), "", Csv(entry.DisplayName), "", "", Csv(entry.SourceRepositoryPath), "",
            "", "", "", "", Csv(entry.Error))));
        lines.AddRange(manifest.GenerationRequiredPilotIds.Select(pilotId => string.Join(',',
            "generation-required", Csv(pilotId), "", "", "", "", "", "", "", "", "", "", "")));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private readonly record struct PixelRectangle(int X, int Y, int Width, int Height);
}
