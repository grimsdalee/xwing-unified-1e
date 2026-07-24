namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class AssetImportRequest
{
    public string RepositoryRoot { get; init; } = string.Empty;
    public string Profile { get; init; } = string.Empty;
}

public sealed class AssetImportResult
{
    public string Profile { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Scanned { get; init; }
    public int Imported { get; init; }
    public int Updated { get; init; }
    public int AlreadyLinked { get; init; }
    public int Warnings { get; init; }
    public int Errors { get; init; }
    public string ManifestFile { get; init; } = string.Empty;
    public string ReportFile { get; init; } = string.Empty;
    public string AssetManifestRoot { get; init; } = string.Empty;
    public string KnowledgeBaseRoot { get; init; } = string.Empty;
}

public interface IAssetImporter
{
    string Profile { get; }
    string Description { get; }
    AssetImportResult Import(AssetImportRequest request);
}

public sealed class AssetImportCoordinator
{
    private readonly IReadOnlyDictionary<string, IAssetImporter> importers;

    public AssetImportCoordinator(IEnumerable<IAssetImporter> importers)
    {
        this.importers = importers.ToDictionary(
            importer => importer.Profile,
            StringComparer.OrdinalIgnoreCase);
    }

    public static AssetImportCoordinator CreateDefault() => new(new IAssetImporter[]
    {
        new GeneratedPilotTokenAssetImporter(),
        new FirstEditionDialAssetImporter()
    });

    public IReadOnlyCollection<IAssetImporter> Importers => importers.Values
        .OrderBy(importer => importer.Profile, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public AssetImportResult Import(string repositoryRoot, string profile)
    {
        if (!importers.TryGetValue(profile, out var importer))
        {
            throw new ArgumentException(
                $"Unknown asset import profile '{profile}'. Available profiles: {string.Join(", ", importers.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}");
        }

        return importer.Import(new AssetImportRequest
        {
            RepositoryRoot = repositoryRoot,
            Profile = profile
        });
    }
}

public sealed class GeneratedPilotTokenAssetImporter : IAssetImporter
{
    public string Profile => "generated-pilot-tokens";
    public string Description => "Copy completed generated pilot base tokens and rebuild the asset catalogue and UKB.";

    public AssetImportResult Import(AssetImportRequest request)
    {
        var result = new GeneratedPilotTokenImportService().Import(request.RepositoryRoot);
        return new AssetImportResult
        {
            Profile = Profile,
            Description = Description,
            Scanned = result.ImagesScanned,
            Imported = result.Imported,
            Updated = result.Updated,
            Warnings = result.Warnings,
            Errors = result.Errors,
            ManifestFile = result.ManifestFile,
            ReportFile = result.ReportFile,
            AssetManifestRoot = result.AssetManifestRoot,
            KnowledgeBaseRoot = result.KnowledgeBaseRoot
        };
    }
}

public sealed class FirstEditionDialAssetImporter : IAssetImporter
{
    public string Profile => "first-edition-dials";
    public string Description => "Link standardised First Edition dial textures explicitly to semantic ships and update UKB references.";

    public AssetImportResult Import(AssetImportRequest request)
    {
        var result = new FirstEditionDialImportService().Import(request.RepositoryRoot);
        return new AssetImportResult
        {
            Profile = Profile,
            Description = Description,
            Scanned = result.ImagesScanned,
            Imported = result.Linked,
            Updated = result.Updated,
            AlreadyLinked = result.AlreadyLinked,
            Warnings = result.Warnings,
            Errors = result.Errors,
            ManifestFile = result.ManifestFile,
            ReportFile = result.ReportFile,
            AssetManifestRoot = result.AssetManifestRoot,
            KnowledgeBaseRoot = result.KnowledgeBaseRoot
        };
    }
}
