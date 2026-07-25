namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed class PilotAssetLinkingOptions
{
    public string RepositoryRoot { get; init; } = string.Empty;
    public string KnowledgeBasePath { get; init; } = string.Empty;
    public string PilotsFile { get; init; } = string.Empty;
    public string OfficialPilotsFile { get; init; } = string.Empty;
    public string LegacySavePath { get; init; } = string.Empty;
    public string XWingDataPilotsPath { get; init; } = string.Empty;
    public string OutputRoot { get; init; } = string.Empty;
    public string TokenSheetDecisionsPath { get; init; } = string.Empty;
    public int CandidatesPerRole { get; init; }

    public static PilotAssetLinkingOptions Create(
        string repositoryRoot,
        string? pilotsFile,
        string? outputFolder,
        int candidatesPerRole)
    {
        var fullRoot = Path.GetFullPath(repositoryRoot);
        var resolvedPilotsFile = pilotsFile is null
            ? FindPilotsFile(fullRoot)
            : Path.GetFullPath(pilotsFile);

        return new PilotAssetLinkingOptions
        {
            RepositoryRoot = fullRoot,
            KnowledgeBasePath = Path.Combine(fullRoot, "ukb", "knowledge-base.json"),
            PilotsFile = resolvedPilotsFile,
            OfficialPilotsFile = FindOfficialPilotsFile(fullRoot, resolvedPilotsFile),
            LegacySavePath = FindLegacySave(fullRoot),
            XWingDataPilotsPath = FindXWingDataPilots(fullRoot),
            OutputRoot = outputFolder is null
                ? Path.Combine(fullRoot, "ukb")
                : Path.GetFullPath(outputFolder),
            TokenSheetDecisionsPath = Path.Combine(
                fullRoot,
                "ukb",
                "pilot-token-sheet-decisions.json"),
            CandidatesPerRole = Math.Clamp(candidatesPerRole, 1, 50)
        };
    }

    public void Validate()
    {
        if (!File.Exists(KnowledgeBasePath))
            throw new FileNotFoundException(
                "Knowledge base not found. Run build-knowledge-base first.",
                KnowledgeBasePath);

        if (!File.Exists(PilotsFile))
            throw new FileNotFoundException(
                "First Edition pilots.json was not found. Use --pilots <file>.",
                PilotsFile);

        if (!File.Exists(XWingDataPilotsPath))
            throw new FileNotFoundException(
                "Imported xwing-data pilots.js was not found. Run import-xwing-data first.",
                XWingDataPilotsPath);
    }

    private static string FindPilotsFile(string root)
    {
        // Prefer the live repository mapping folder. The compiled copy under
        // AppContext.BaseDirectory is only a fallback because it can be stale.
        var candidates = new[]
        {
            Path.Combine(
                root,
                "tools",
                "UnifiedToolkit",
                "ConversionData",
                "first-edition",
                "pilots.json"),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "ConversionData",
                "first-edition",
                "pilots.json"),
            Path.Combine(
                AppContext.BaseDirectory,
                "ConversionData",
                "first-edition",
                "pilots.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string FindOfficialPilotsFile(
        string root,
        string mappedPilotsFile)
    {
        var mappedFolder = Path.GetDirectoryName(mappedPilotsFile);
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(mappedFolder))
            candidates.Add(Path.Combine(mappedFolder, "official-pilots.json"));

        candidates.Add(Path.Combine(
            root,
            "tools",
            "UnifiedToolkit",
            "ConversionData",
            "first-edition",
            "official-pilots.json"));

        candidates.Add(Path.Combine(
            Directory.GetCurrentDirectory(),
            "ConversionData",
            "first-edition",
            "official-pilots.json"));

        candidates.Add(Path.Combine(
            AppContext.BaseDirectory,
            "ConversionData",
            "first-edition",
            "official-pilots.json"));

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists)
            ?? candidates[0];
    }

    private static string FindXWingDataPilots(string root)
    {
        var candidates = new[]
        {
            Path.Combine(
                root,
                "assets",
                "source",
                "xwing-data",
                "data",
                "pilots.js"),
            Path.Combine(
                root,
                "source",
                "xwing-data",
                "data",
                "pilots.js")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string FindLegacySave(string root)
    {
        var candidates = new[]
        {
            Path.Combine(root, "source", "legacy-1e", "3302209318.json"),
            Path.Combine(root, "source", "legacy1e", "3302209318.json"),
            Path.Combine(root, "3302209318.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
