using System.Net;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed class PilotTokenReviewPackageResult
{
    public int ReviewPilots { get; init; }
    public int MissingSheetPilots { get; init; }
    public int CandidateImages { get; init; }
    public string OutputFolder { get; init; } = string.Empty;
    public string HtmlFile { get; init; } = string.Empty;
    public string DecisionTemplateFile { get; init; } = string.Empty;
}

public sealed class PilotTokenReviewPackageBuilder
{
    public PilotTokenReviewPackageResult Build(string repositoryRoot, string? pilotLinksFile = null, string? outputFolder = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Repository folder was not found: {root}");

        var linksPath = Path.GetFullPath(pilotLinksFile ?? Path.Combine(root, "ukb", "pilot-links.json"));
        if (!File.Exists(linksPath))
            throw new FileNotFoundException("Pilot links file was not found. Run link-pilot-assets first.", linksPath);

        var output = Path.GetFullPath(outputFolder ?? Path.Combine(root, "ukb", "reports", "pilot-token-review"));
        Directory.CreateDirectory(output);

        var json = File.ReadAllText(linksPath, Encoding.UTF8);
        var domain = JsonSerializer.Deserialize<KnowledgeBasePilotDomain>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Could not read pilot links from: {linksPath}");

        var rows = domain.Pilots
            .Select(pilot => new ReviewRow(
                pilot,
                pilot.AssetRoles.FirstOrDefault(role => role.Role == "PilotBaseTokenSheet"),
                pilot.AssetRoles.FirstOrDefault(role => role.Role == "PilotBaseToken")))
            .Where(row => row.TokenRole is not null && row.TokenRole.Candidates.Count == 0)
            .Where(row => row.SheetRole is null || row.SheetRole.Status != "clear")
            .OrderBy(row => row.SheetRole?.Status == "missing" ? 0 : 1)
            .ThenBy(row => row.Pilot.Faction, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Pilot.ShipId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Pilot.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var htmlPath = Path.Combine(output, "index.html");
        var decisionsPath = Path.Combine(output, "pilot-token-sheet-decisions.template.csv");
        WriteHtml(htmlPath, output, root, rows);
        WriteDecisionTemplate(decisionsPath, rows);

        return new PilotTokenReviewPackageResult
        {
            ReviewPilots = rows.Count(row => row.SheetRole?.Status == "review"),
            MissingSheetPilots = rows.Count(row => row.SheetRole is null || row.SheetRole.Candidates.Count == 0),
            CandidateImages = rows.Sum(row => row.SheetRole?.Candidates.Count ?? 0),
            OutputFolder = output,
            HtmlFile = htmlPath,
            DecisionTemplateFile = decisionsPath
        };
    }

    private static void WriteHtml(string path, string outputFolder, string repositoryRoot, IReadOnlyCollection<ReviewRow> rows)
    {
        var reviewCount = rows.Count(row => row.SheetRole?.Status == "review");
        var missingCount = rows.Count(row => row.SheetRole is null || row.SheetRole.Candidates.Count == 0);
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine("<title>Pilot Token Sheet Review</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f4f6f8;color:#17202a}header{position:sticky;top:0;background:#17202a;color:white;padding:18px 24px;z-index:10;box-shadow:0 2px 8px #0004}main{padding:20px;max-width:1500px;margin:auto}.summary{display:flex;gap:14px;flex-wrap:wrap}.pill{background:#fff2;border:1px solid #fff4;border-radius:999px;padding:6px 11px}.card{background:white;border-radius:10px;margin:0 0 20px;padding:18px;box-shadow:0 2px 8px #0002;border-left:6px solid #d68910}.card.missing{border-left-color:#c0392b}.meta{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:8px;margin:10px 0 16px}.label{font-size:12px;color:#64748b;text-transform:uppercase}.value{font-weight:600}.candidates{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:14px}.candidate{border:1px solid #d8dee6;border-radius:8px;padding:10px;background:#fafbfc}.candidate img{display:block;max-width:100%;max-height:440px;margin:0 auto 10px;background:#ddd;object-fit:contain}.rank{font-weight:700}.path{font-family:Consolas,monospace;font-size:12px;overflow-wrap:anywhere}.reasons{font-size:13px;color:#475569}.missing-box{padding:18px;border:2px dashed #c0392b;border-radius:8px;background:#fff5f4}.note{font-size:13px;color:#475569}a{color:#0b5cab}</style></head><body>");
        sb.AppendLine("<header><h1 style=\"margin:0 0 10px\">Pilot Token Sheet Review</h1>");
        sb.AppendLine($"<div class=\"summary\"><span class=\"pill\">{reviewCount} ambiguous pilots</span><span class=\"pill\">{missingCount} missing-sheet pilots</span><span class=\"pill\">Generated {WebUtility.HtmlEncode(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz"))}</span></div></header>");
        sb.AppendLine("<main><p>This report is review-only. It does not approve assets or crop tokens. Record decisions in <code>pilot-token-sheet-decisions.template.csv</code>.</p>");

        foreach (var row in rows)
        {
            var isMissing = row.SheetRole is null || row.SheetRole.Candidates.Count == 0;
            sb.AppendLine($"<section class=\"card{(isMissing ? " missing" : string.Empty)}\">");
            sb.AppendLine($"<h2>{H(row.Pilot.Name)}</h2>");
            sb.AppendLine("<div class=\"meta\">");
            Meta(sb, "Faction", row.Pilot.Faction);
            Meta(sb, "Ship", row.Pilot.ShipId);
            Meta(sb, "Pilot ID", row.Pilot.PilotId);
            Meta(sb, "Target ID", row.Pilot.TargetId);
            Meta(sb, "Skill / Cost", $"{row.Pilot.PilotSkill} / {row.Pilot.SquadPointCost}");
            Meta(sb, "Sheet status", row.SheetRole?.Status ?? "missing");
            sb.AppendLine("</div>");

            if (isMissing)
            {
                sb.AppendLine("<div class=\"missing-box\"><strong>No candidate token sheet was found.</strong><br>Locate the correct First Edition sheet manually, then add its repository path to the decision template.</div>");
            }
            else
            {
                sb.AppendLine("<div class=\"candidates\">");
                for (var index = 0; index < row.SheetRole!.Candidates.Count; index++)
                {
                    var candidate = row.SheetRole.Candidates[index];
                    var absoluteAssetPath = Path.GetFullPath(Path.Combine(repositoryRoot, candidate.RepositoryPath.Replace('/', Path.DirectorySeparatorChar)));
                    var imageSource = Path.GetRelativePath(outputFolder, absoluteAssetPath).Replace(Path.DirectorySeparatorChar, '/');
                    var exists = File.Exists(absoluteAssetPath);
                    sb.AppendLine("<article class=\"candidate\">");
                    sb.AppendLine($"<div class=\"rank\">Candidate {index + 1} — score {candidate.Score} ({H(candidate.Confidence)})</div>");
                    if (exists)
                        sb.AppendLine($"<a href=\"{A(imageSource)}\" target=\"_blank\"><img loading=\"lazy\" src=\"{A(imageSource)}\" alt=\"{H(row.Pilot.Name)} candidate {index + 1}\"></a>");
                    else
                        sb.AppendLine("<div class=\"missing-box\">Image file is not present at this repository path.</div>");
                    sb.AppendLine($"<div class=\"path\">{H(candidate.RepositoryPath)}</div>");
                    sb.AppendLine($"<div class=\"note\">Warehouse: {H(candidate.Warehouse)} · Asset: {H(candidate.AssetId)}</div>");
                    sb.AppendLine($"<div class=\"reasons\">{H(string.Join("; ", candidate.Reasons))}</div>");
                    sb.AppendLine("</article>");
                }
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</section>");
        }

        sb.AppendLine("</main></body></html>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteDecisionTemplate(string path, IEnumerable<ReviewRow> rows)
    {
        var lines = new List<string>
        {
            "pilotId,targetId,pilotName,shipId,faction,currentStatus,selectedAssetId,selectedRepositoryPath,decision,notes"
        };

        foreach (var row in rows)
        {
            var first = row.SheetRole?.Candidates.FirstOrDefault();
            lines.Add(Csv(
                row.Pilot.PilotId,
                row.Pilot.TargetId,
                row.Pilot.Name,
                row.Pilot.ShipId,
                row.Pilot.Faction,
                row.SheetRole?.Status ?? "missing",
                first?.AssetId ?? string.Empty,
                first?.RepositoryPath ?? string.Empty,
                string.Empty,
                string.Empty));
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void Meta(StringBuilder sb, string label, string value) =>
        sb.AppendLine($"<div><div class=\"label\">{H(label)}</div><div class=\"value\">{H(value)}</div></div>");

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string A(string value)
    {
        var normalized = value.Replace('\\', '/');
        var escapedSegments = normalized
            .Split('/')
            .Select(segment => segment is "." or ".."
                ? segment
                : Uri.EscapeDataString(Uri.UnescapeDataString(segment)));

        return WebUtility.HtmlEncode(string.Join("/", escapedSegments));
    }
    private static string Csv(params object?[] values) => string.Join(",", values.Select(value => $"\"{(value?.ToString() ?? string.Empty).Replace("\"", "\"\"")}\""));

    private sealed record ReviewRow(KnowledgeBasePilot Pilot, KnowledgeBasePilotAssetRole? SheetRole, KnowledgeBasePilotAssetRole? TokenRole);
}
