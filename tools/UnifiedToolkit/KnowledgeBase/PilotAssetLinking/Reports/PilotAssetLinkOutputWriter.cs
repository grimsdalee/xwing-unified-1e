using System.Text;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking.Reports;

public static class PilotAssetLinkOutputWriter
{
    public static void Write(string outputRoot, UnifiedKnowledgeBase knowledgeBase, IReadOnlyCollection<KnowledgeBasePilot> pilots)
    {
        var reports = Path.Combine(outputRoot, "reports");
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(reports);
        ShipAssetJson.Write(Path.Combine(outputRoot, "knowledge-base.json"), knowledgeBase);
        ShipAssetJson.Write(Path.Combine(outputRoot, "pilot-links.json"), new KnowledgeBasePilotDomain { SchemaVersion = "1.0.0", GeneratedUtc = DateTimeOffset.UtcNow, Pilots = pilots.ToList() });
        WriteReview(Path.Combine(reports, "pilot-link-review.csv"), pilots);
        WriteUnresolved(Path.Combine(reports, "unresolved-required-pilot-assets.csv"), pilots);
        WriteSummary(Path.Combine(reports, "PILOT-LINK-SUMMARY.md"), pilots);
    }

    private static void WriteReview(string path, IEnumerable<KnowledgeBasePilot> pilots)
    {
        var lines = new List<string> { "pilotId,sourceId,targetId,pilotName,shipId,faction,role,required,status,rank,assetId,warehouse,score,confidence,repositoryPath,reasons" };
        foreach (var pilot in pilots)
        foreach (var role in pilot.AssetRoles)
        {
            if (role.Candidates.Count == 0)
                lines.Add(Csv(pilot.PilotId, pilot.SourceId, pilot.TargetId, pilot.Name, pilot.ShipId, pilot.Faction, role.Role, role.Required, role.Status, 0, "", "", 0, "", "", ""));
            else
                for (var i = 0; i < role.Candidates.Count; i++)
                {
                    var c = role.Candidates[i];
                    lines.Add(Csv(pilot.PilotId, pilot.SourceId, pilot.TargetId, pilot.Name, pilot.ShipId, pilot.Faction, role.Role, role.Required, role.Status, i + 1, c.AssetId, c.Warehouse, c.Score, c.Confidence, c.RepositoryPath, string.Join("; ", c.Reasons)));
                }
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteUnresolved(string path, IEnumerable<KnowledgeBasePilot> pilots)
    {
        var lines = new List<string> { "pilotId,targetId,pilotName,shipId,faction,role,recommendedResolution" };
        foreach (var pilot in pilots)
        foreach (var role in pilot.AssetRoles.Where(r => r.Required && r.Candidates.Count == 0))
        {
            var resolution = role.Role == "PilotBaseToken" ? "Locate token sheet and extract the individual First Edition pilot base token." : "Locate authoritative First Edition pilot card artwork.";
            lines.Add(Csv(pilot.PilotId, pilot.TargetId, pilot.Name, pilot.ShipId, pilot.Faction, role.Role, resolution));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteSummary(string path, IReadOnlyCollection<KnowledgeBasePilot> pilots)
    {
        var roles = pilots.SelectMany(p => p.AssetRoles).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Pilot Asset Linking Summary").AppendLine();
        sb.AppendLine($"Generated UTC: {DateTimeOffset.UtcNow:O}").AppendLine();
        sb.AppendLine($"- Pilots processed: {pilots.Count}");
        sb.AppendLine($"- Candidate links: {roles.Sum(r => r.Candidates.Count)}");
        sb.AppendLine($"- Clear role matches: {roles.Count(r => r.Status == "clear")}");
        sb.AppendLine($"- Roles requiring review: {roles.Count(r => r.Status == "review")}");
        sb.AppendLine($"- Missing required roles: {roles.Count(r => r.Required && r.Candidates.Count == 0)}").AppendLine();
        sb.AppendLine("## Role Breakdown").AppendLine();
        sb.AppendLine("| Role | Clear | Review | Missing | Candidates |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var group in roles.GroupBy(r => r.Role).OrderBy(g => g.Key))
            sb.AppendLine($"| {group.Key} | {group.Count(r => r.Status == "clear")} | {group.Count(r => r.Status == "review")} | {group.Count(r => r.Status == "missing")} | {group.Sum(r => r.Candidates.Count)} |");
        sb.AppendLine().AppendLine("No pilot assets were automatically approved.");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Csv(params object?[] values) => string.Join(",", values.Select(value => $"\"{(value?.ToString() ?? string.Empty).Replace("\"", "\"\"")}\""));
}
