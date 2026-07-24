using System.Globalization;
using System.Text;
using System.Text.Json;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;
using UnifiedToolkit.RepositoryAssets;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class FirstEditionDialImportResult
{
    public int ImagesScanned { get; init; }
    public int Linked { get; init; }
    public int Updated { get; init; }
    public int AlreadyLinked { get; init; }
    public int Warnings { get; init; }
    public int Errors { get; init; }
    public string ManifestFile { get; init; } = string.Empty;
    public string ReportFile { get; init; } = string.Empty;
    public string AssetManifestRoot { get; init; } = string.Empty;
    public string KnowledgeBaseRoot { get; init; } = string.Empty;
}

public sealed class FirstEditionDialImportManifest
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceRoot { get; init; } = string.Empty;
    public List<FirstEditionDialImportEntry> Dials { get; init; } = new();
}

public sealed class FirstEditionDialImportEntry
{
    public string Status { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string Ship { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class FirstEditionDialImportService
{
    private const string DialRole = "DialTexture";
    private const int AuthoritativeScore = 1000;

    public FirstEditionDialImportResult Import(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Repository folder was not found: {root}");

        var sourceRoot = Path.Combine(root, "assets", "generated", "FirstEditionDialTexture");
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Standardised First Edition dial folder was not found: {sourceRoot}");

        var reportRoot = Path.Combine(root, "_unifiedtoolkit_reports", "phase10h", "first-edition-dial-import");
        Directory.CreateDirectory(reportRoot);

        var catalogueResult = new AssetRepositoryCatalogueBuilder().Build(root);
        var knowledgeBaseResult = new KnowledgeBaseBuilder().Build(root, null, refreshCatalogue: false);

        var knowledgeBasePath = Path.Combine(knowledgeBaseResult.OutputRoot, "knowledge-base.json");
        var shipLinksPath = Path.Combine(knowledgeBaseResult.OutputRoot, "ship-links.json");
        if (!File.Exists(shipLinksPath))
            throw new FileNotFoundException("The UKB ship-links.json file was not found. Run link-ship-assets before importing dials.", shipLinksPath);

        var knowledgeBase = ShipAssetJson.Read<UnifiedKnowledgeBase>(knowledgeBasePath);
        var shipDomain = ShipAssetJson.Read<KnowledgeBaseShipDomain>(shipLinksPath);
        var assetsByPath = knowledgeBase.Domains.Assets.ToDictionary(
            asset => NormalisePath(asset.RepositoryPath),
            StringComparer.OrdinalIgnoreCase);

        var manifest = new FirstEditionDialImportManifest
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceRoot = RepositoryRelative(root, sourceRoot)
        };

        var files = Directory.EnumerateFiles(sourceRoot, "*.png", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
            manifest.Dials.Add(LinkOne(root, sourceRoot, file, shipDomain, assetsByPath));

        UpdateKnowledgeBaseReferences(knowledgeBase, shipDomain.Ships);
        knowledgeBase.Domains.Ships.Clear();
        knowledgeBase.Domains.Ships.AddRange(shipDomain.Ships);

        shipDomain = new KnowledgeBaseShipDomain
        {
            SchemaVersion = "1.2.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            Ships = shipDomain.Ships
        };

        ShipAssetJson.Write(shipLinksPath, shipDomain);
        ShipAssetJson.Write(knowledgeBasePath, knowledgeBase);
        ShipAssetJson.Write(Path.Combine(knowledgeBaseResult.OutputRoot, "assets.json"), new KnowledgeBaseAssetDomain
        {
            SchemaVersion = knowledgeBase.SchemaVersion,
            GeneratedUtc = DateTimeOffset.UtcNow,
            Assets = knowledgeBase.Domains.Assets,
            UnavailableSources = knowledgeBase.Domains.UnavailableSources,
            DuplicateGroups = knowledgeBase.Domains.DuplicateGroups
        });

        var manifestPath = Path.Combine(reportRoot, "first-edition-dial-import.json");
        var reportPath = Path.Combine(reportRoot, "first-edition-dial-import.csv");
        ShipAssetJson.Write(manifestPath, manifest);
        WriteCsv(reportPath, manifest.Dials);

        return new FirstEditionDialImportResult
        {
            ImagesScanned = files.Count,
            Linked = manifest.Dials.Count(entry => entry.Status == "linked"),
            Updated = manifest.Dials.Count(entry => entry.Status == "updated"),
            AlreadyLinked = manifest.Dials.Count(entry => entry.Status == "already-linked"),
            Warnings = manifest.Dials.Count(entry => entry.Status == "warning"),
            Errors = manifest.Dials.Count(entry => entry.Status == "error"),
            ManifestFile = manifestPath,
            ReportFile = reportPath,
            AssetManifestRoot = catalogueResult.ManifestRoot,
            KnowledgeBaseRoot = knowledgeBaseResult.OutputRoot
        };
    }

    private static FirstEditionDialImportEntry LinkOne(
        string root,
        string sourceRoot,
        string file,
        KnowledgeBaseShipDomain shipDomain,
        IReadOnlyDictionary<string, KnowledgeBaseAsset> assetsByPath)
    {
        var relative = Path.GetRelativePath(sourceRoot, file);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length != 2)
            return Failure(root, file, "Expected path <faction>/<ship>__dial-<hash>.png.");

        var faction = Normalise(segments[0]);
        var shipKey = DialFileKey(segments[1]);
        var repositoryPath = RepositoryRelative(root, file);

        if (!assetsByPath.TryGetValue(NormalisePath(repositoryPath), out var asset))
            return Failure(root, file, "The generated dial was not found in the refreshed UKB asset catalogue.", faction, shipKey);

        var matches = shipDomain.Ships.Where(ship =>
                (Normalise(ship.TargetId).Equals(shipKey, StringComparison.OrdinalIgnoreCase)
                 || Normalise(ship.SourceId).Equals(shipKey, StringComparison.OrdinalIgnoreCase))
                && ship.Factions.Any(value => Normalise(value).Equals(faction, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matches.Count != 1)
        {
            var message = matches.Count == 0
                ? $"The curated First Edition dial has no matching ship in the current semantic repository: faction '{faction}', ship '{shipKey}'. The asset remains catalogued but was not linked."
                : $"Multiple semantic ships matched faction '{faction}' and ship '{shipKey}'.";
            return Failure(root, file, message, faction, shipKey, asset.AssetId, matches.Count == 0 ? "warning" : "error");
        }

        var ship = matches[0];
        var role = ship.AssetRoles.FirstOrDefault(value => value.Role.Equals(DialRole, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            role = new KnowledgeBaseShipAssetRole
            {
                Role = DialRole,
                Required = true,
                Status = "clear"
            };
            ship.AssetRoles.Add(role);
        }

        var oldGenerated = role.Candidates
            .Where(candidate => IsGeneratedDial(candidate.RepositoryPath, faction))
            .ToList();
        var exact = oldGenerated.FirstOrDefault(candidate =>
            candidate.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase)
            && NormalisePath(candidate.RepositoryPath).Equals(NormalisePath(repositoryPath), StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            EnsureRoleStatus(ship, role, "clear");
            return Success("already-linked", ship, faction, shipKey, asset, repositoryPath,
                "The authoritative faction-specific dial link already exists.");
        }

        foreach (var candidate in oldGenerated)
            role.Candidates.Remove(candidate);

        role.Candidates.Insert(0, new KnowledgeBaseShipAssetCandidate
        {
            AssetId = asset.AssetId,
            RepositoryPath = repositoryPath,
            Warehouse = asset.Warehouse,
            Score = AuthoritativeScore,
            Confidence = "authoritative",
            Reasons = new List<string>
            {
                "explicit Phase 10H First Edition dial import",
                $"exact faction '{faction}'",
                $"exact semantic ship '{ship.TargetId}'",
                "standardised 250 x 250 PNG"
            }
        });
        EnsureRoleStatus(ship, role, "clear");

        return Success(oldGenerated.Count == 0 ? "linked" : "updated", ship, faction, shipKey, asset, repositoryPath,
            oldGenerated.Count == 0
                ? "Added an explicit authoritative dial link."
                : $"Replaced {oldGenerated.Count} previous generated faction dial link(s).");
    }


    private static void EnsureRoleStatus(
        KnowledgeBaseShip ship,
        KnowledgeBaseShipAssetRole role,
        string status)
    {
        if (role.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            return;

        var index = ship.AssetRoles.IndexOf(role);
        if (index < 0)
            throw new InvalidOperationException($"Could not find asset role '{role.Role}' on ship '{ship.TargetId}'.");

        ship.AssetRoles[index] = new KnowledgeBaseShipAssetRole
        {
            Role = role.Role,
            Required = role.Required,
            Status = status,
            Candidates = role.Candidates
        };
    }

    private static void UpdateKnowledgeBaseReferences(
        UnifiedKnowledgeBase knowledgeBase,
        IReadOnlyCollection<KnowledgeBaseShip> ships)
    {
        foreach (var asset in knowledgeBase.Domains.Assets)
        {
            asset.ReferencedBy.RemoveAll(reference =>
                reference.EntityType.Equals("ship", StringComparison.OrdinalIgnoreCase)
                && reference.Role.StartsWith("candidate:DialTexture:", StringComparison.OrdinalIgnoreCase));
        }

        var assetsById = knowledgeBase.Domains.Assets
            .GroupBy(asset => asset.AssetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var ship in ships)
        {
            var role = ship.AssetRoles.FirstOrDefault(value => value.Role.Equals(DialRole, StringComparison.OrdinalIgnoreCase));
            if (role is null) continue;

            foreach (var candidate in role.Candidates)
            {
                if (!assetsById.TryGetValue(candidate.AssetId, out var matchingAssets)) continue;
                var asset = matchingAssets.FirstOrDefault(value =>
                    NormalisePath(value.RepositoryPath).Equals(NormalisePath(candidate.RepositoryPath), StringComparison.OrdinalIgnoreCase));
                if (asset is null) continue;

                asset.ReferencedBy.Add(new KnowledgeBaseEntityReference
                {
                    EntityType = "ship",
                    EntityId = ship.ShipId,
                    Role = $"candidate:{DialRole}:{candidate.Score}"
                });
            }
        }
    }

    private static bool IsGeneratedDial(string path, string faction)
    {
        var normalised = NormalisePath(path);
        return normalised.StartsWith($"assets/generated/firsteditiondialtexture/{faction}/", StringComparison.OrdinalIgnoreCase);
    }

    private static string DialFileKey(string fileName)
    {
        var value = Path.GetFileNameWithoutExtension(fileName);
        var marker = value.IndexOf("__dial-", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) value = value[..marker];
        return Normalise(value);
    }

    private static FirstEditionDialImportEntry Success(
        string status,
        KnowledgeBaseShip ship,
        string faction,
        string shipKey,
        KnowledgeBaseAsset asset,
        string repositoryPath,
        string message) => new()
        {
            Status = status,
            Faction = faction,
            Ship = shipKey,
            ShipId = ship.ShipId,
            AssetId = asset.AssetId,
            RepositoryPath = repositoryPath,
            Message = message
        };

    private static FirstEditionDialImportEntry Failure(
        string root,
        string file,
        string message,
        string faction = "",
        string ship = "",
        string assetId = "",
        string status = "error") => new()
        {
            Status = status,
            Faction = faction,
            Ship = ship,
            AssetId = assetId,
            RepositoryPath = RepositoryRelative(root, file),
            Message = message
        };

    private static string Normalise(string value)
        => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NormalisePath(string path)
        => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

    private static string RepositoryRelative(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static void WriteCsv(string path, IEnumerable<FirstEditionDialImportEntry> entries)
    {
        var lines = new List<string>
        {
            "status,faction,ship,shipId,assetId,repositoryPath,message"
        };
        lines.AddRange(entries.Select(entry => string.Join(',',
            Csv(entry.Status), Csv(entry.Faction), Csv(entry.Ship), Csv(entry.ShipId),
            Csv(entry.AssetId), Csv(entry.RepositoryPath), Csv(entry.Message))));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string Csv(string value)
        => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
