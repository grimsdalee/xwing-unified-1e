using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12D-2:
/// Generates reusable First Edition dial reverse textures for every supported
/// First Edition faction. The output uses a transparent exterior, a solid
/// faction-colour face, and a darker circular rim.
/// </summary>
public static class GenerateFirstEditionDialBacksCommand
{
    private const int ImageSize = 512;
    private const float OuterMargin = 10f;
    private const float RimInset = 8f;
    private const float RimWidth = 18f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyList<FactionDialBackDefinition> Definitions =
        new[]
        {
            new FactionDialBackDefinition(
                FactionId: "firstorder",
                DisplayName: "First Order",
                FillRed: 43,
                FillGreen: 43,
                FillBlue: 48,
                RimRed: 25,
                RimGreen: 25,
                RimBlue: 30),

            new FactionDialBackDefinition(
                FactionId: "galacticempire",
                DisplayName: "Galactic Empire",
                FillRed: 50,
                FillGreen: 58,
                FillBlue: 66,
                RimRed: 32,
                RimGreen: 40,
                RimBlue: 48),

            new FactionDialBackDefinition(
                FactionId: "rebelalliance",
                DisplayName: "Rebel Alliance",
                FillRed: 92,
                FillGreen: 43,
                FillBlue: 48,
                RimRed: 74,
                RimGreen: 25,
                RimBlue: 30),

            new FactionDialBackDefinition(
                FactionId: "resistance",
                DisplayName: "Resistance",
                FillRed: 110,
                FillGreen: 52,
                FillBlue: 58,
                RimRed: 88,
                RimGreen: 36,
                RimBlue: 42),

            new FactionDialBackDefinition(
                FactionId: "scumandvillainy",
                DisplayName: "Scum and Villainy",
                FillRed: 96,
                FillGreen: 76,
                FillBlue: 34,
                RimRed: 78,
                RimGreen: 58,
                RimBlue: 16)
        };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "Usage: generate-first-edition-dial-backs " +
                "<first-edition-repository> [--output <folder>]");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);

            if (!Directory.Exists(repositoryRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Repository folder does not exist: {repositoryRoot}");
            }

            var generatedRoot = Path.Combine(
                repositoryRoot,
                "assets",
                "generated",
                "FirstEditionDialBack");

            var reportRoot = ReadOption(args, "--output")
                ?? Path.Combine(
                    repositoryRoot,
                    "_unifiedtoolkit_reports",
                    "phase12d",
                    "first-edition-dial-backs");

            reportRoot = Path.GetFullPath(reportRoot);

            Directory.CreateDirectory(generatedRoot);
            Directory.CreateDirectory(reportRoot);

            var results = new List<GeneratedFactionDialBack>();

            foreach (var definition in Definitions)
            {
                var factionFolder = Path.Combine(
                    generatedRoot,
                    definition.FactionId);

                Directory.CreateDirectory(factionFolder);

                var destination = Path.Combine(
                    factionFolder,
                    "dial-back.png");

                GenerateImage(destination, definition);

                var info = new FileInfo(destination);

                results.Add(new GeneratedFactionDialBack
                {
                    FactionId = definition.FactionId,
                    DisplayName = definition.DisplayName,
                    FillRgb = definition.FillRgb,
                    RimRgb = definition.RimRgb,
                    Width = ImageSize,
                    Height = ImageSize,
                    RepositoryPath = NormalisePath(
                        Path.GetRelativePath(repositoryRoot, destination)),
                    FullPath = destination,
                    Bytes = info.Length
                });
            }

            var manifest = new FirstEditionDialBackManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                ImageWidth = ImageSize,
                ImageHeight = ImageSize,
                TransparentExterior = true,
                FactionsGenerated = results.Count,
                DialBacks = results
            };

            var manifestPath = Path.Combine(
                reportRoot,
                "first-edition-dial-backs.json");
            var csvPath = Path.Combine(
                reportRoot,
                "first-edition-dial-backs.csv");
            var reportPath = Path.Combine(
                reportRoot,
                "FIRST-EDITION-DIAL-BACKS.md");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));

            WriteCsv(csvPath, results);
            WriteReport(reportPath, manifest);

            Console.WriteLine(
                "UnifiedToolkit Phase 12D-2 First Edition Dial Back Generation");
            Console.WriteLine(
                "===============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:              {repositoryRoot}");
            Console.WriteLine($"Generated asset folder:  {generatedRoot}");
            Console.WriteLine($"Image format:            PNG {ImageSize}x{ImageSize}");
            Console.WriteLine($"Transparent exterior:    Yes");
            Console.WriteLine($"Faction dial backs:      {results.Count}");
            Console.WriteLine();

            foreach (var result in results)
            {
                Console.WriteLine(
                    $"  {result.DisplayName,-20} " +
                    $"Fill {result.FillRgb,-14} Rim {result.RimRgb,-14} " +
                    $"{result.RepositoryPath}");
            }

            Console.WriteLine();
            Console.WriteLine($"Manifest:                {manifestPath}");
            Console.WriteLine($"CSV:                     {csvPath}");
            Console.WriteLine($"Report:                  {reportPath}");
            Console.WriteLine();
            Console.WriteLine(
                "First Edition dial backs generated. Existing outputs were " +
                "refreshed; source assets were not modified.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"First Edition dial-back generation failed: {ex.Message}");
            return 1;
        }
    }

    private static void GenerateImage(
        string destination,
        FactionDialBackDefinition definition)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(
                ImageSize,
                ImageSize,
                SKColorType.Rgba8888,
                SKAlphaType.Premul));

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(
                definition.FillRed,
                definition.FillGreen,
                definition.FillBlue,
                255)
        };

        using var rimPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = RimWidth,
            Color = new SKColor(
                definition.RimRed,
                definition.RimGreen,
                definition.RimBlue,
                255)
        };

        var centre = ImageSize / 2f;
        var outerRadius = ImageSize / 2f - OuterMargin;

        canvas.DrawCircle(
            centre,
            centre,
            outerRadius,
            fillPaint);

        canvas.DrawCircle(
            centre,
            centre,
            outerRadius - RimInset,
            rimPaint);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(
            SKEncodedImageFormat.Png,
            100);

        using var stream = File.Create(destination);
        encoded.SaveTo(stream);
    }

    private static void WriteCsv(
        string path,
        IEnumerable<GeneratedFactionDialBack> results)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "FactionId,DisplayName,FillRGB,RimRGB,Width,Height," +
            "RepositoryPath,Bytes");

        foreach (var result in results)
        {
            writer.WriteLine(string.Join(',',
                Csv(result.FactionId),
                Csv(result.DisplayName),
                Csv(result.FillRgb),
                Csv(result.RimRgb),
                result.Width.ToString(CultureInfo.InvariantCulture),
                result.Height.ToString(CultureInfo.InvariantCulture),
                Csv(result.RepositoryPath),
                result.Bytes.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static void WriteReport(
        string path,
        FirstEditionDialBackManifest manifest)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "# Phase 12D-2 – First Edition Dial Backs");
        writer.WriteLine();
        writer.WriteLine(
            "Reusable First Edition dial reverse textures generated by " +
            "UnifiedToolkit.");
        writer.WriteLine();
        writer.WriteLine($"- Size: **{manifest.ImageWidth}×{manifest.ImageHeight} PNG**");
        writer.WriteLine("- Exterior: **transparent**");
        writer.WriteLine($"- Factions generated: **{manifest.FactionsGenerated}**");
        writer.WriteLine();
        writer.WriteLine("| Faction | Fill RGB | Rim RGB | Asset |");
        writer.WriteLine("|---|---|---|---|");

        foreach (var item in manifest.DialBacks)
        {
            writer.WriteLine(
                $"| {item.DisplayName} | `{item.FillRgb}` | " +
                $"`{item.RimRgb}` | `{item.RepositoryPath}` |");
        }
    }

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

    private static string NormalisePath(string value) =>
        value.Replace('\\', '/');

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}

public sealed record FactionDialBackDefinition(
    string FactionId,
    string DisplayName,
    byte FillRed,
    byte FillGreen,
    byte FillBlue,
    byte RimRed,
    byte RimGreen,
    byte RimBlue)
{
    public string FillRgb => $"{FillRed},{FillGreen},{FillBlue}";
    public string RimRgb => $"{RimRed},{RimGreen},{RimBlue}";
}

public sealed class FirstEditionDialBackManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public bool TransparentExterior { get; init; }
    public int FactionsGenerated { get; init; }
    public List<GeneratedFactionDialBack> DialBacks { get; init; } = new();
}

public sealed class GeneratedFactionDialBack
{
    public string FactionId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string FillRgb { get; init; } = string.Empty;
    public string RimRgb { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public string RepositoryPath { get; init; } = string.Empty;

    [JsonIgnore]
    public string FullPath { get; init; } = string.Empty;

    public long Bytes { get; init; }
}
