using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12E-5D:
/// Builds a repository-owned copy of the Unified dial OBJ with the front-face
/// UV island rotated independently from the reverse face and side geometry.
///
/// This corrects the small angular mismatch between the dial artwork and the
/// object-attached XML controls without rotating the entire TTS object.
/// </summary>
public static class BuildFirstEditionDialModelCommand
{
    private const string DefaultSourceUrl =
        "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/dialmodel.obj";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
            ValidateDirectory(repositoryRoot, "First Edition repository");

            var source = GetOption(args, "--source") ?? DefaultSourceUrl;
            var rotationDegrees = ParseDoubleOption(
                args,
                "--front-rotation-degrees",
                1.9);
            var horizontalOffset = ParseDoubleOption(
                args,
                "--front-u-offset",
                0.005);
            var verticalOffset = ParseDoubleOption(
                args,
                "--front-v-offset",
                0.02);

            var requestedOutput = GetOption(args, "--output");

            var outputPath = Path.GetFullPath(
                requestedOutput
                ?? Path.Combine(
                    repositoryRoot,
                    "assets",
                    "generated",
                    "FirstEditionDialModel",
                    BuildCalibrationFileName(rotationDegrees, horizontalOffset, verticalOffset)));

            var reportFolder = Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase12e",
                "dial-model-generation");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            Directory.CreateDirectory(reportFolder);

            var sourceText = LoadSource(source, repositoryRoot);
            var result = TransformFrontUvIsland(sourceText, rotationDegrees, horizontalOffset, verticalOffset);

            File.WriteAllText(
                outputPath,
                result.OutputText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var relativeOutput = Path.GetRelativePath(repositoryRoot, outputPath)
                .Replace('\\', '/');

            var manifest = new DialModelGenerationManifest
            {
                Phase = "12E-5D",
                Source = source,
                OutputPath = relativeOutput,
                FrontUvRotationDegrees = rotationDegrees,
                FrontUvHorizontalOffset = horizontalOffset,
                FrontUvVerticalOffset = verticalOffset,
                FrontFacesDetected = result.FrontFaceCount,
                FrontUvCoordinatesRotated = result.RotatedUvCount,
                FrontUvMinimumU = result.MinimumU,
                FrontUvMaximumU = result.MaximumU,
                FrontUvMinimumV = result.MinimumV,
                FrontUvMaximumV = result.MaximumV,
                FrontUvCentreU = result.CentreU,
                FrontUvCentreV = result.CentreV,
                SourceSha256 = ComputeSha256(sourceText),
                OutputSha256 = ComputeSha256(result.OutputText),
                FullDiskGeometryApplied = false
            };

            var manifestPath = Path.Combine(
                reportFolder,
                "first-edition-dial-model.json");
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));

            var reportPath = Path.Combine(
                reportFolder,
                "FIRST-EDITION-DIAL-MODEL-REPORT.md");
            File.WriteAllText(
                reportPath,
                BuildReport(manifest),
                new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Phase 12E-5D First Edition Dial Front Transform Calibration");
            Console.WriteLine("========================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                 {repositoryRoot}");
            Console.WriteLine($"Source model:               {source}");
            Console.WriteLine($"Front UV rotation:          {rotationDegrees.ToString("0.###", CultureInfo.InvariantCulture)} degrees");
            Console.WriteLine($"Front UV horizontal offset: {horizontalOffset.ToString("0.###", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"Front UV vertical offset:   {verticalOffset.ToString("0.###", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"Front faces detected:       {result.FrontFaceCount}");
            Console.WriteLine($"Front UV coordinates:       {result.RotatedUvCount}");
            Console.WriteLine($"Generated model:            {outputPath}");
            Console.WriteLine($"Manifest:                   {manifestPath}");
            Console.WriteLine($"Report:                     {reportPath}");
            Console.WriteLine();
            Console.WriteLine("The reverse UV island and side geometry were not modified.");
            Console.WriteLine("The lower dial cut-out remains unchanged in this cache-busting calibration stage.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"First Edition dial-model generation failed: {ex.Message}");
            return 1;
        }
    }

    private static UvRotationResult TransformFrontUvIsland(
        string sourceText,
        double rotationDegrees,
        double horizontalOffset,
        double verticalOffset)
    {
        var newline = sourceText.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var frontUvIndices = new HashSet<int>();
        var currentGroup = string.Empty;
        var frontFaceCount = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                vertices.Add(ParseVector3(line, "v"));
            }
            else if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                uvs.Add(ParseVector2(line, "vt"));
            }
            else if (line.StartsWith("vn ", StringComparison.Ordinal))
            {
                normals.Add(ParseVector3(line, "vn"));
            }
            else if (line.StartsWith("g ", StringComparison.Ordinal))
            {
                currentGroup = line[2..].Trim();
            }
            else if (currentGroup.Equals("dial", StringComparison.OrdinalIgnoreCase)
                     && line.StartsWith("f ", StringComparison.Ordinal))
            {
                var references = ParseFace(line);
                if (references.Count < 3)
                    continue;

                var averageY = references.Average(reference =>
                    vertices[ResolveObjIndex(reference.VertexIndex, vertices.Count)].Y);
                var averageNormalY = references.Average(reference =>
                    normals[ResolveObjIndex(reference.NormalIndex, normals.Count)].Y);

                // The front face of the Unified dial is the upward-facing,
                // textured surface at approximately Y=0.213. The reverse face
                // is downward-facing and occupies a separate UV island.
                if (averageY > 0.20 && averageNormalY > 0.95)
                {
                    frontFaceCount++;
                    foreach (var reference in references)
                    {
                        frontUvIndices.Add(
                            ResolveObjIndex(reference.TextureIndex, uvs.Count));
                    }
                }
            }
        }

        if (frontFaceCount < 40)
        {
            throw new InvalidDataException(
                $"Expected at least 40 front dial faces, detected {frontFaceCount}.");
        }

        if (frontUvIndices.Count < 40)
        {
            throw new InvalidDataException(
                $"Expected at least 40 front UV coordinates, detected {frontUvIndices.Count}.");
        }

        var selected = frontUvIndices.Select(index => uvs[index]).ToArray();
        var minimumU = selected.Min(value => value.U);
        var maximumU = selected.Max(value => value.U);
        var minimumV = selected.Min(value => value.V);
        var maximumV = selected.Max(value => value.V);
        var centreU = (minimumU + maximumU) / 2.0;
        var centreV = (minimumV + maximumV) / 2.0;

        var radians = rotationDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);

        foreach (var index in frontUvIndices)
        {
            var original = uvs[index];
            var offsetU = original.U - centreU;
            var offsetV = original.V - centreV;

            uvs[index] = new Vector2(
                centreU + offsetU * cosine - offsetV * sine + horizontalOffset,
                centreV + offsetU * sine + offsetV * cosine + verticalOffset);
        }

        var textureIndex = 0;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!lines[lineIndex].TrimStart().StartsWith("vt ", StringComparison.Ordinal))
                continue;

            if (frontUvIndices.Contains(textureIndex))
            {
                var value = uvs[textureIndex];
                lines[lineIndex] = string.Create(
                    CultureInfo.InvariantCulture,
                    $"vt {value.U:0.000000} {value.V:0.000000}");
            }

            textureIndex++;
        }

        return new UvRotationResult
        {
            OutputText = string.Join(newline, lines),
            FrontFaceCount = frontFaceCount,
            RotatedUvCount = frontUvIndices.Count,
            MinimumU = minimumU,
            MaximumU = maximumU,
            MinimumV = minimumV,
            MaximumV = maximumV,
            CentreU = centreU,
            CentreV = centreV
        };
    }

    private static string BuildCalibrationFileName(
        double rotationDegrees,
        double horizontalOffset,
        double verticalOffset)
    {
        var direction = rotationDegrees < 0 ? "minus" : "plus";
        var magnitude = Math.Abs(rotationDegrees)
            .ToString("0.###", CultureInfo.InvariantCulture)
            .Replace('.', '_');
        var horizontalDirection = horizontalOffset < 0 ? "minus" : "plus";
        var horizontalMagnitude = Math.Abs(horizontalOffset)
            .ToString("0.###", CultureInfo.InvariantCulture)
            .Replace('.', '_');
        var verticalDirection = verticalOffset < 0 ? "minus" : "plus";
        var verticalMagnitude = Math.Abs(verticalOffset)
            .ToString("0.###", CultureInfo.InvariantCulture)
            .Replace('.', '_');
        return $"first-edition-dial-model-r-{direction}-{magnitude}-u-{horizontalDirection}-{horizontalMagnitude}-v-{verticalDirection}-{verticalMagnitude}.obj";
    }

    private static string LoadSource(string source, string repositoryRoot)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(2);
            return client.GetStringAsync(uri).GetAwaiter().GetResult();
        }

        var path = Path.IsPathRooted(source)
            ? source
            : Path.Combine(repositoryRoot, source);
        path = Path.GetFullPath(path);

        if (!File.Exists(path))
            throw new FileNotFoundException("Dial source OBJ was not found.", path);

        return File.ReadAllText(path);
    }

    private static IReadOnlyList<FaceReference> ParseFace(string line)
    {
        var result = new List<FaceReference>();
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var parts = token.Split('/');
            if (parts.Length < 3
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var vertex)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var texture)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var normal))
            {
                throw new InvalidDataException($"Unsupported OBJ face reference '{token}'.");
            }

            result.Add(new FaceReference(vertex, texture, normal));
        }

        return result;
    }

    private static Vector3 ParseVector3(string line, string prefix)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            throw new InvalidDataException($"Invalid OBJ {prefix} line: {line}");

        return new Vector3(
            ParseDouble(parts[1]),
            ParseDouble(parts[2]),
            ParseDouble(parts[3]));
    }

    private static Vector2 ParseVector2(string line, string prefix)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            throw new InvalidDataException($"Invalid OBJ {prefix} line: {line}");

        return new Vector2(ParseDouble(parts[1]), ParseDouble(parts[2]));
    }

    private static double ParseDouble(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int ResolveObjIndex(int index, int count)
    {
        var resolved = index > 0 ? index - 1 : count + index;
        if (resolved < 0 || resolved >= count)
            throw new InvalidDataException($"OBJ index {index} is out of range.");
        return resolved;
    }

    private static string? GetOption(string[] args, string option)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static double ParseDoubleOption(
        string[] args,
        string option,
        double defaultValue)
    {
        var value = GetOption(args, option);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new ArgumentException(
                $"Option {option} requires a numeric value, received '{value}'.");
        }

        if (parsed < -15.0 || parsed > 15.0)
            throw new ArgumentOutOfRangeException(
                option,
                "Front UV rotation must be between -15 and 15 degrees.");

        return parsed;
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildReport(DialModelGenerationManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# First Edition Dial Model Generation");
        builder.AppendLine();
        builder.AppendLine($"- Phase: `{manifest.Phase}`");
        builder.AppendLine($"- Source: `{manifest.Source}`");
        builder.AppendLine($"- Output: `{manifest.OutputPath}`");
        builder.AppendLine($"- Front UV rotation: `{manifest.FrontUvRotationDegrees.ToString("0.###", CultureInfo.InvariantCulture)} degrees`");
        builder.AppendLine($"- Front UV horizontal offset: `{manifest.FrontUvHorizontalOffset.ToString("0.###", CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Front UV vertical offset: `{manifest.FrontUvVerticalOffset.ToString("0.###", CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Front faces detected: `{manifest.FrontFacesDetected}`");
        builder.AppendLine($"- UV coordinates rotated: `{manifest.FrontUvCoordinatesRotated}`");
        builder.AppendLine($"- Full-disk geometry applied: `{manifest.FullDiskGeometryApplied}`");
        builder.AppendLine();
        builder.AppendLine("Only the upward-facing front UV island was rotated and translated horizontally and vertically. The reverse face, side geometry and collider were not modified.");
        builder.AppendLine();
        builder.AppendLine("The lower cut-out remains in this calibration model and will be addressed after the alignment angle is visually approved.");
        return builder.ToString();
    }

    private static void ValidateDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{label} not found: {path}");
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  build-first-edition-dial-model <first-edition-repository> [--front-rotation-degrees <degrees>] [--front-u-offset <value>] [--front-v-offset <value>] [--source <file-or-url>] [--output <file>]");
    }

    private readonly record struct Vector2(double U, double V);
    private readonly record struct Vector3(double X, double Y, double Z);
    private readonly record struct FaceReference(
        int VertexIndex,
        int TextureIndex,
        int NormalIndex);

    private sealed class UvRotationResult
    {
        public required string OutputText { get; init; }
        public int FrontFaceCount { get; init; }
        public int RotatedUvCount { get; init; }
        public double MinimumU { get; init; }
        public double MaximumU { get; init; }
        public double MinimumV { get; init; }
        public double MaximumV { get; init; }
        public double CentreU { get; init; }
        public double CentreV { get; init; }
    }

    private sealed class DialModelGenerationManifest
    {
        public string Phase { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public double FrontUvRotationDegrees { get; set; }
        public double FrontUvHorizontalOffset { get; set; }
        public double FrontUvVerticalOffset { get; set; }
        public int FrontFacesDetected { get; set; }
        public int FrontUvCoordinatesRotated { get; set; }
        public double FrontUvMinimumU { get; set; }
        public double FrontUvMaximumU { get; set; }
        public double FrontUvMinimumV { get; set; }
        public double FrontUvMaximumV { get; set; }
        public double FrontUvCentreU { get; set; }
        public double FrontUvCentreV { get; set; }
        public string SourceSha256 { get; set; } = string.Empty;
        public string OutputSha256 { get; set; } = string.Empty;
        public bool FullDiskGeometryApplied { get; set; }
    }
}
