namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed class PilotAssetLinkingOptions
{
    public string RepositoryRoot { get; init; } = string.Empty;
    public string KnowledgeBasePath { get; init; } = string.Empty;
    public string PilotsFile { get; init; } = string.Empty;
    public string LegacySavePath { get; init; } = string.Empty;
    public string OutputRoot { get; init; } = string.Empty;
    public int CandidatesPerRole { get; init; }

    public static PilotAssetLinkingOptions Create(string repositoryRoot, string? pilotsFile, string? outputFolder, int candidatesPerRole)
    {
        var fullRoot = Path.GetFullPath(repositoryRoot);
        return new PilotAssetLinkingOptions
        {
            RepositoryRoot = fullRoot,
            KnowledgeBasePath = Path.Combine(fullRoot, "ukb", "knowledge-base.json"),
            PilotsFile = pilotsFile is null ? FindPilotsFile(fullRoot) : Path.GetFullPath(pilotsFile),
            LegacySavePath = FindLegacySave(fullRoot),
            OutputRoot = outputFolder is null ? Path.Combine(fullRoot, "ukb") : Path.GetFullPath(outputFolder),
            CandidatesPerRole = Math.Clamp(candidatesPerRole, 1, 50)
        };
    }

    public void Validate()
    {
        if (!File.Exists(KnowledgeBasePath))
            throw new FileNotFoundException("Knowledge base not found. Run build-knowledge-base first.", KnowledgeBasePath);
        if (!File.Exists(PilotsFile))
            throw new FileNotFoundException("First Edition pilots.json was not found. Use --pilots <file>.", PilotsFile);
    }

    private static string FindPilotsFile(string root)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition", "pilots.json"),
            Path.Combine(root, "tools", "UnifiedToolkit", "ConversionData", "first-edition", "pilots.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "ConversionData", "first-edition", "pilots.json")
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
