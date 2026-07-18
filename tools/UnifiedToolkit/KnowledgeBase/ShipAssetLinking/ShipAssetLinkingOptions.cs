namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class ShipAssetLinkingOptions
{
    public string RepositoryRoot { get; init; } = string.Empty;
    public string KnowledgeBasePath { get; init; } = string.Empty;
    public string ShipsFile { get; init; } = string.Empty;
    public string LegacySavePath { get; init; } = string.Empty;
    public string OutputRoot { get; init; } = string.Empty;
    public int CandidatesPerRole { get; init; }

    public static ShipAssetLinkingOptions Create(
        string repositoryRoot,
        string? shipsFile,
        string? outputFolder,
        int candidatesPerRole)
    {
        var fullRepositoryRoot = Path.GetFullPath(repositoryRoot);
        var resolvedShipsFile = shipsFile is null
            ? FindShipsFile(fullRepositoryRoot)
            : Path.GetFullPath(shipsFile);

        return new ShipAssetLinkingOptions
        {
            RepositoryRoot = fullRepositoryRoot,
            KnowledgeBasePath = Path.Combine(fullRepositoryRoot, "ukb", "knowledge-base.json"),
            ShipsFile = resolvedShipsFile,
            LegacySavePath = FindLegacySave(fullRepositoryRoot),
            OutputRoot = outputFolder is null
                ? Path.Combine(fullRepositoryRoot, "ukb")
                : Path.GetFullPath(outputFolder),
            CandidatesPerRole = Math.Clamp(candidatesPerRole, 1, 50)
        };
    }

    public void Validate()
    {
        if (!File.Exists(KnowledgeBasePath))
        {
            throw new FileNotFoundException(
                "Knowledge base not found. Run build-knowledge-base first.",
                KnowledgeBasePath);
        }

        if (!File.Exists(ShipsFile))
        {
            throw new FileNotFoundException(
                "First Edition ships.json was not found. Use --ships <file>.",
                ShipsFile);
        }
    }

    private static string FindShipsFile(string repositoryRoot)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition", "ships.json"),
            Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition", "ships.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "ConversionData", "first-edition", "ships.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string FindLegacySave(string repositoryRoot)
    {
        var candidates = new[]
        {
            Path.Combine(repositoryRoot, "source", "legacy-1e", "3302209318.json"),
            Path.Combine(repositoryRoot, "source", "legacy1e", "3302209318.json"),
            Path.Combine(repositoryRoot, "3302209318.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
