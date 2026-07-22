using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class PilotTokenEditorPreparationResult
{
    public int PilotCount { get; init; }
    public int DonorCount { get; init; }
    public int PilotCardCount { get; init; }
    public string OutputFolder { get; init; } = string.Empty;
    public string PlanFile { get; init; } = string.Empty;
    public string HtmlFile { get; init; } = string.Empty;
}

public sealed class PilotTokenEditorPreparationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public PilotTokenEditorPreparationResult Prepare(
        string repositoryRoot,
        string completedGenerationPlan,
        string? outputFolder = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var sourcePlan = Path.GetFullPath(completedGenerationPlan);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Repository folder was not found: {root}");
        }

        if (!File.Exists(sourcePlan))
        {
            throw new FileNotFoundException(
                "Completed pilot-token generation plan was not found.",
                sourcePlan);
        }

        using var sourceDocument = JsonDocument.Parse(
            File.ReadAllText(sourcePlan, Encoding.UTF8));

        if (!sourceDocument.RootElement.TryGetProperty("pilots", out var sourcePilots)
            || sourcePilots.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The completed generation plan must contain a root 'pilots' array.");
        }

        var pilotElements = sourcePilots.EnumerateArray().ToList();
        if (pilotElements.Count == 0)
        {
            throw new InvalidDataException("The completed generation plan contains no pilots.");
        }

        var notApproved = pilotElements
            .Where(pilot =>
                !GetString(pilot, "status").Equals(
                    "Approved",
                    StringComparison.OrdinalIgnoreCase))
            .Select(pilot => GetString(pilot, "displayName"))
            .ToList();

        if (notApproved.Count > 0)
        {
            throw new InvalidDataException(
                "Every pilot must be Approved before preparing the editor. Not approved: "
                + string.Join(", ", notApproved));
        }

        var output = Path.GetFullPath(
            outputFolder
            ?? Path.Combine(root, "ukb", "reports", "pilot-token-editor"));

        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }

        var assets = Path.Combine(output, "assets");
        Directory.CreateDirectory(assets);

        var editorPilots = new List<object>();
        var donorCount = 0;
        var cardCount = 0;

        foreach (var pilot in pilotElements)
        {
            var pilotId = GetString(pilot, "pilotId");
            var displayName = GetString(pilot, "displayName");
            var targetId = GetString(pilot, "targetId");
            var faction = GetString(pilot, "faction");
            var ship = GetString(pilot, "ship");
            var skill = GetInt(pilot, "skill");
            var points = GetInt(pilot, "points");

            var donorRepositoryPath = GetString(
                pilot,
                "selectedDonorSourceRepositoryPath");

            if (string.IsNullOrWhiteSpace(donorRepositoryPath))
            {
                throw new InvalidDataException(
                    $"{displayName} does not have a selected donor repository path.");
            }

            var donorFull = ResolveRepositoryPath(root, donorRepositoryPath);
            if (!File.Exists(donorFull))
            {
                throw new FileNotFoundException(
                    $"Selected donor for {displayName} was not found.",
                    donorFull);
            }

            var donorPreview = CopyAsset(
                assets,
                donorFull,
                $"{Safe(pilotId)}-donor");
            donorCount++;

            var cardRepositoryPath = GetString(
                pilot,
                "pilotCardSourceRepositoryPath");

            string cardPreview = string.Empty;
            if (!string.IsNullOrWhiteSpace(cardRepositoryPath))
            {
                var cardFull = ResolveRepositoryPath(root, cardRepositoryPath);
                if (File.Exists(cardFull))
                {
                    cardPreview = CopyAsset(
                        assets,
                        cardFull,
                        $"{Safe(pilotId)}-card");
                    cardCount++;
                }
            }

            editorPilots.Add(new
            {
                pilotId,
                targetId,
                displayName,
                faction,
                ship,
                skill,
                points,
                donorRepositoryPath = donorRepositoryPath.Replace('\\', '/'),
                donorPreview,
                pilotCardRepositoryPath = cardRepositoryPath.Replace('\\', '/'),
                pilotCardPreview = cardPreview,
                status = "NeedsLayout",
                nameRegion = new
                {
                    x = 0.18,
                    y = 0.72,
                    width = 0.64,
                    height = 0.12
                },
                skillRegion = new
                {
                    x = 0.02,
                    y = 0.66,
                    width = 0.18,
                    height = 0.24
                },
                nameStyle = new
                {
                    text = displayName,
                    fontFamily = "Arial Narrow",
                    fontSize = 28,
                    fontWeight = "700",
                    textColor = "#111111",
                    backgroundColor = "#f2eee6",
                    align = "center",
                    rotation = 0
                },
                skillStyle = new
                {
                    text = skill.ToString(),
                    fontFamily = "Arial Black",
                    fontSize = 58,
                    fontWeight = "900",
                    textColor = "#f28c00",
                    strokeColor = "#111111",
                    strokeWidth = 3,
                    backgroundColor = "transparent",
                    align = "center",
                    rotation = 0
                },
                notes = string.Empty
            });
        }

        var editorPlan = new
        {
            schemaVersion = "1.0.0",
            generatedUtc = DateTimeOffset.UtcNow,
            sourceGenerationPlan = Path.GetRelativePath(root, sourcePlan)
                .Replace('\\', '/'),
            pilots = editorPilots
        };

        var planPath = Path.Combine(
            output,
            "pilot-token-editor-plan.template.json");

        File.WriteAllText(
            planPath,
            JsonSerializer.Serialize(editorPlan, JsonOptions),
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(output, "editor-data.js"),
            "window.editorData = "
            + JsonSerializer.Serialize(editorPlan, JsonOptions)
            + ";",
            new UTF8Encoding(false));

        CopyUiAssets(output);

        return new PilotTokenEditorPreparationResult
        {
            PilotCount = editorPilots.Count,
            DonorCount = donorCount,
            PilotCardCount = cardCount,
            OutputFolder = output,
            PlanFile = planPath,
            HtmlFile = Path.Combine(output, "index.html")
        };
    }

    private static string ResolveRepositoryPath(string root, string value)
    {
        return Path.GetFullPath(
            Path.Combine(
                root,
                value.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string CopyAsset(
        string assets,
        string source,
        string name)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var destination = Path.Combine(assets, Safe(name) + extension);
        File.Copy(source, destination, overwrite: true);
        return "assets/" + Path.GetFileName(destination);
    }

    private static void CopyUiAssets(string output)
    {
        var source = Path.Combine(
            AppContext.BaseDirectory,
            "KnowledgeBase",
            "AssetExtraction",
            "TokenEditorAssets");

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"Pilot token editor UI assets were not found: {source}");
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(output, Path.GetFileName(file)),
                overwrite: true);
        }
    }

    private static string GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var integer))
        {
            return integer;
        }

        return value.ValueKind == JsonValueKind.String
               && int.TryParse(value.GetString(), out integer)
            ? integer
            : 0;
    }

    private static string Safe(string value)
    {
        return new string(
            (value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}
