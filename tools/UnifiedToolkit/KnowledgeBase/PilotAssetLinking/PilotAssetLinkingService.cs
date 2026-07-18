using System.Security.Cryptography;
using System.Text;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;
using UnifiedToolkit.KnowledgeBase.PilotAssetLinking.Reports;

namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed class PilotAssetLinkingService
{
    private readonly PilotAssetRoleCatalogue roles = new();
    private readonly PilotAssetEligibilityFilter eligibilityFilter = new();

    public PilotAssetLinkResult Link(PilotAssetLinkingOptions options)
    {
        options.Validate();
        var knowledgeBase = ShipAssetJson.Read<UnifiedKnowledgeBase>(options.KnowledgeBasePath);
        var pilots = ShipAssetJson.Read<List<FirstEditionPilotRecord>>(options.PilotsFile);
        var contexts = LegacyAssetContextIndex.Load(options.LegacySavePath);
        var linked = pilots.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.ShipId, StringComparer.OrdinalIgnoreCase)
            .Select(p => BuildPilot(p, knowledgeBase.Domains.Assets, contexts, options.CandidatesPerRole))
            .ToList();

        ReplacePilotReferences(knowledgeBase, linked);
        PilotAssetLinkOutputWriter.Write(options.OutputRoot, knowledgeBase, linked);

        return new PilotAssetLinkResult
        {
            Pilots = linked.Count,
            CandidateLinks = linked.Sum(p => p.AssetRoles.Sum(r => r.Candidates.Count)),
            ClearRoles = linked.Sum(p => p.AssetRoles.Count(r => r.Status == "clear")),
            ReviewRoles = linked.Sum(p => p.AssetRoles.Count(r => r.Status == "review")),
            MissingRequiredRoles = linked.Sum(p => p.AssetRoles.Count(r => r.Required && r.Candidates.Count == 0)),
            OutputRoot = options.OutputRoot
        };
    }

    private KnowledgeBasePilot BuildPilot(FirstEditionPilotRecord pilot, IEnumerable<KnowledgeBaseAsset> assets, LegacyAssetContextIndex contexts, int limit)
    {
        var identity = PilotIdentityProfile.Create(pilot);
        var roleLinks = roles.GetAll().Select(role => BuildRole(role, pilot, identity, assets, contexts, limit)).ToList();
        return new KnowledgeBasePilot
        {
            PilotId = $"PILOT-{StableId($"{pilot.TargetId}|{pilot.ShipId}|{pilot.Faction}")}",
            SourceId = pilot.SourceId,
            TargetId = pilot.TargetId,
            Name = pilot.Name,
            ShipId = pilot.ShipId,
            Faction = pilot.Faction,
            PilotSkill = pilot.PilotSkill,
            SquadPointCost = pilot.SquadPointCost,
            Unique = pilot.Unique,
            AssetRoles = roleLinks
        };
    }

    private KnowledgeBasePilotAssetRole BuildRole(PilotAssetRoleDefinition role, FirstEditionPilotRecord pilot, PilotIdentityProfile identity, IEnumerable<KnowledgeBaseAsset> assets, LegacyAssetContextIndex contexts, int limit)
    {
        var candidates = assets
            .Select(asset => new { Asset = asset, Contexts = contexts.Find(asset) })
            .Where(item => eligibilityFilter.IsEligible(item.Asset, item.Contexts, role.Name, identity))
            .Select(item => Score(item.Asset, item.Contexts, role.Name, pilot, identity))
            .Where(candidate => candidate.Score >= 45)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        var status = candidates.Count == 0 ? "missing" : IsClear(candidates) ? "clear" : "review";
        return new KnowledgeBasePilotAssetRole { Role = role.Name, Required = role.Required, Status = status, Candidates = candidates };
    }

    private static KnowledgeBasePilotAssetCandidate Score(KnowledgeBaseAsset asset, IReadOnlyList<LegacyAssetContext> contexts, string role, FirstEditionPilotRecord pilot, PilotIdentityProfile identity)
    {
        var path = asset.RepositoryPath.Replace('\\', '/').ToLowerInvariant();
        var compactPath = PilotIdentityProfile.Compact(path);
        var contextText = string.Join(" ", contexts.Select(c => c.SearchText)).ToLowerInvariant();
        var compactContext = PilotIdentityProfile.Compact(contextText);
        var reasons = new List<string>();
        var score = 0;

        foreach (var alias in identity.StrongAliases)
        {
            if (compactPath.Contains(alias, StringComparison.OrdinalIgnoreCase)) { score += 70; reasons.Add($"pilot identity '{alias}' in path"); break; }
            if (compactContext.Contains(alias, StringComparison.OrdinalIgnoreCase)) { score += 75; reasons.Add($"pilot identity '{alias}' in legacy context"); break; }
        }
        if (score == 0)
        {
            var weak = identity.WeakAliases.FirstOrDefault(alias => compactPath.Contains(alias) || compactContext.Contains(alias));
            if (weak is not null) { score += 30; reasons.Add($"pilot word '{weak}' matched"); }
        }

        var shipAlias = PilotIdentityProfile.Compact(pilot.ShipId);
        if (shipAlias.Length >= 4 && (compactPath.Contains(shipAlias) || compactContext.Contains(shipAlias)))
        {
            score += 12; reasons.Add("ship identity matched");
        }

        var roleText = $"{path} {contextText}";
        switch (role)
        {
            case "PilotCard":
                if (roleText.Contains("card")) { score += 25; reasons.Add("card context"); }
                if (roleText.Contains("pilot")) { score += 8; reasons.Add("pilot context"); }
                if (roleText.Contains("token") || roleText.Contains("dial")) score -= 35;
                break;
            case "PilotBaseTokenSheet":
                if (roleText.Contains("sheet") || roleText.Contains("atlas")) { score += 30; reasons.Add("sheet or atlas context"); }
                if (roleText.Contains("token") || roleText.Contains("base")) { score += 18; reasons.Add("token/base context"); }
                if (contexts.Count > 1) { score += 10; reasons.Add("shared legacy image reference"); }
                if (roleText.Contains("card") || roleText.Contains("dial")) score -= 30;
                break;
            case "PilotBaseToken":
                if (roleText.Contains("pilot token") || roleText.Contains("ship token") || roleText.Contains("base token")) { score += 35; reasons.Add("pilot ship-token context"); }
                else if (roleText.Contains("token") && roleText.Contains("base")) { score += 20; reasons.Add("token and base context"); }
                else score -= 25;
                if (roleText.Contains("condition") || roleText.Contains("target lock") || roleText.Contains("objective") || roleText.Contains("dial")) score -= 45;
                break;
        }

        if (role.Equals("PilotCard", StringComparison.OrdinalIgnoreCase)
            && asset.Warehouse.Equals("xwing-data", StringComparison.OrdinalIgnoreCase))
        {
            score += 45;
            reasons.Add("individual xwing-data First Edition pilot card");
        }
        else if (asset.Warehouse.Equals("legacy1e", StringComparison.OrdinalIgnoreCase))
        {
            score += 12;
            reasons.Add("legacy First Edition source");
        }

        if (asset.Availability.Equals("available", StringComparison.OrdinalIgnoreCase)) score += 3;

        return new KnowledgeBasePilotAssetCandidate
        {
            AssetId = asset.AssetId,
            RepositoryPath = asset.RepositoryPath,
            Warehouse = asset.Warehouse,
            Score = score,
            Confidence = score >= 90 ? "high" : score >= 65 ? "medium" : "low",
            Reasons = reasons
        };
    }

    private static bool IsClear(IReadOnlyList<KnowledgeBasePilotAssetCandidate> candidates) =>
        candidates[0].Score >= 95 && (candidates.Count == 1 || candidates[0].Score - candidates[1].Score >= 15);


    private static void ReplacePilotReferences(UnifiedKnowledgeBase knowledgeBase, IReadOnlyCollection<KnowledgeBasePilot> pilots)
    {
        foreach (var asset in knowledgeBase.Domains.Assets)
            asset.ReferencedBy.RemoveAll(r => r.EntityType.Equals("pilot", StringComparison.OrdinalIgnoreCase));

        var byKey = knowledgeBase.Domains.Assets.ToDictionary(a => $"{a.AssetId}\u001f{a.RepositoryPath}", StringComparer.OrdinalIgnoreCase);
        foreach (var pilot in pilots)
        foreach (var role in pilot.AssetRoles)
        foreach (var candidate in role.Candidates)
            if (byKey.TryGetValue($"{candidate.AssetId}\u001f{candidate.RepositoryPath}", out var asset))
                asset.ReferencedBy.Add(new KnowledgeBaseEntityReference { EntityType = "pilot", EntityId = pilot.PilotId, Role = $"candidate:{role.Role}:{candidate.Score}" });
    }

    private static string StableId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
}
