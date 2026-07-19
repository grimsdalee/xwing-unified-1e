using System.Security.Cryptography;
using System.Text;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed class PilotTokenSheetDecisionDocument
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string SourceCsv { get; init; } = string.Empty;
    public List<PilotTokenSheetDecision> Decisions { get; init; } = new();
}

public sealed class PilotTokenSheetDecision
{
    public string PilotId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string PilotName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public static class PilotTokenSheetDecisionStore
{
    public static IReadOnlyDictionary<string, PilotTokenSheetDecision> Load(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, PilotTokenSheetDecision>(StringComparer.OrdinalIgnoreCase);

        var document = ShipAssetJson.Read<PilotTokenSheetDecisionDocument>(path);
        return document.Decisions.ToDictionary(x => x.PilotId, StringComparer.OrdinalIgnoreCase);
    }

    public static void Write(string path, PilotTokenSheetDecisionDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ShipAssetJson.Write(path, document);
    }

    public static KnowledgeBasePilotAssetRole Apply(
        KnowledgeBasePilotAssetRole role,
        PilotTokenSheetDecision? decision)
    {
        if (decision is null || !role.Role.Equals("PilotBaseTokenSheet", StringComparison.OrdinalIgnoreCase))
            return role;

        if (decision.Status.Equals("missing", StringComparison.OrdinalIgnoreCase))
            return new KnowledgeBasePilotAssetRole
            {
                Role = role.Role,
                Required = role.Required,
                Status = "missing",
                Candidates = new List<KnowledgeBasePilotAssetCandidate>()
            };

        return new KnowledgeBasePilotAssetRole
        {
            Role = role.Role,
            Required = role.Required,
            Status = "clear",
            Candidates = new List<KnowledgeBasePilotAssetCandidate>
            {
                new()
                {
                    AssetId = decision.AssetId,
                    RepositoryPath = decision.RepositoryPath,
                    Warehouse = InferWarehouse(decision.RepositoryPath),
                    Score = 1000,
                    Confidence = "manual",
                    Reasons = new List<string> { "manually approved pilot token sheet" }
                }
            }
        };
    }

    public static string ComputeAssetId(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return $"AST-{Convert.ToHexString(hash)[..16]}";
    }

    public static string NormalizeRepositoryPath(string value) => value.Replace('\\', '/').Trim().TrimStart('/');

    private static string InferWarehouse(string path)
    {
        var normalized = path.Replace('\\', '/');
        var marker = "assets/source/";
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return "manual";
        var remainder = normalized[(index + marker.Length)..];
        var slash = remainder.IndexOf('/');
        return slash < 0 ? remainder : remainder[..slash];
    }
}
