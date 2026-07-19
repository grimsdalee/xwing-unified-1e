using System.Text;
using UnifiedToolkit.KnowledgeBase;
using UnifiedToolkit.KnowledgeBase.PilotAssetLinking;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.Commands;

public static class ApplyPilotTokenSheetDecisionsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { ShowUsage(); return 1; }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var csvPath = Path.GetFullPath(args[1]);
            var candidates = 8;

            for (var i = 2; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--candidates" when i + 1 < args.Length && int.TryParse(args[++i], out var value):
                        candidates = Math.Clamp(value, 1, 50);
                        break;
                    default:
                        Console.WriteLine($"Unknown or incomplete option: {args[i]}");
                        ShowUsage();
                        return 1;
                }
            }

            Console.WriteLine("UnifiedToolkit Apply Pilot Token Sheet Decisions");
            Console.WriteLine("================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository: {repositoryRoot}");
            Console.WriteLine($"Decisions:  {csvPath}");
            Console.WriteLine();

            if (!File.Exists(csvPath))
                throw new FileNotFoundException("Decision CSV was not found.", csvPath);

            var pilotLinksPath = Path.Combine(repositoryRoot, "ukb", "pilot-links.json");
            var knowledgeBasePath = Path.Combine(repositoryRoot, "ukb", "knowledge-base.json");
            if (!File.Exists(pilotLinksPath))
                throw new FileNotFoundException("pilot-links.json was not found. Run link-pilot-assets first.", pilotLinksPath);
            if (!File.Exists(knowledgeBasePath))
                throw new FileNotFoundException("knowledge-base.json was not found. Run build-knowledge-base first.", knowledgeBasePath);

            var pilots = ShipAssetJson.Read<KnowledgeBasePilotDomain>(pilotLinksPath).Pilots;
            var knowledgeBase = ShipAssetJson.Read<UnifiedKnowledgeBase>(knowledgeBasePath);
            var rows = ReadCsv(csvPath);
            var decisions = ValidateAndResolve(rows, pilots, knowledgeBase.Domains.Assets, repositoryRoot);

            var storePath = Path.Combine(repositoryRoot, "ukb", "pilot-token-sheet-decisions.json");
            PilotTokenSheetDecisionStore.Write(storePath, new PilotTokenSheetDecisionDocument
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                SourceCsv = csvPath,
                Decisions = decisions.OrderBy(x => x.PilotName, StringComparer.OrdinalIgnoreCase).ToList()
            });

            var result = new PilotAssetLinkingService().Link(PilotAssetLinkingOptions.Create(repositoryRoot, null, null, candidates));
            var approved = decisions.Count(x => x.Status == "approve");
            var missing = decisions.Count(x => x.Status == "missing");
            var pathResolved = decisions.Count(x => x.Status == "approve" && rows.Any(r => Same(r, x) && string.IsNullOrWhiteSpace(Get(r, "selectedAssetId"))));

            Console.WriteLine($"Approved sheets:       {approved}");
            Console.WriteLine($"Retained as missing:   {missing}");
            Console.WriteLine($"Asset IDs from paths:  {pathResolved}");
            Console.WriteLine();
            Console.WriteLine($"Clear role matches:     {result.ClearRoles}");
            Console.WriteLine($"Roles requiring review: {result.ReviewRoles}");
            Console.WriteLine($"Missing required roles: {result.MissingRequiredRoles}");
            Console.WriteLine();
            Console.WriteLine($"Decision store: {storePath}");
            Console.WriteLine($"Output:         {result.OutputRoot}");
            Console.WriteLine();
            Console.WriteLine("Pilot token-sheet decisions applied and pilot links regenerated successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static List<PilotTokenSheetDecision> ValidateAndResolve(
        IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyCollection<KnowledgeBasePilot> pilots,
        IReadOnlyCollection<KnowledgeBaseAsset> assets,
        string repositoryRoot)
    {
        var pilotsById = pilots.ToDictionary(x => x.PilotId, StringComparer.OrdinalIgnoreCase);
        var assetsById = assets
            .GroupBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var assetsByPath = assets.GroupBy(x => PilotTokenSheetDecisionStore.NormalizeRepositoryPath(x.RepositoryPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var decisions = new List<PilotTokenSheetDecision>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = index + 2;
            var row = rows[index];
            var pilotId = Get(row, "pilotId");
            var status = Get(row, "decision");
            if (string.IsNullOrWhiteSpace(status)) status = Get(row, "currentStatus");
            status = status.Trim().ToLowerInvariant();
            if (status == "reject-all") status = "missing";

            if (string.IsNullOrWhiteSpace(pilotId)) { errors.Add($"Row {rowNumber}: pilotId is blank."); continue; }
            if (!seen.Add(pilotId)) { errors.Add($"Row {rowNumber}: duplicate pilotId '{pilotId}'."); continue; }
            if (!pilotsById.TryGetValue(pilotId, out var pilot)) { errors.Add($"Row {rowNumber}: pilotId '{pilotId}' is not present in pilot-links.json."); continue; }
            if (status is not ("approve" or "missing")) { errors.Add($"Row {rowNumber}: status must be approve or missing."); continue; }

            var assetId = Get(row, "selectedAssetId").Trim();
            var repositoryPath = PilotTokenSheetDecisionStore.NormalizeRepositoryPath(Get(row, "selectedRepositoryPath"));

            if (status == "approve")
            {
                if (string.IsNullOrWhiteSpace(assetId) && string.IsNullOrWhiteSpace(repositoryPath))
                {
                    errors.Add($"Row {rowNumber}: approved pilot '{pilot.Name}' has neither selectedAssetId nor selectedRepositoryPath.");
                    continue;
                }

                KnowledgeBaseAsset? asset = null;
                if (!string.IsNullOrWhiteSpace(repositoryPath))
                    assetsByPath.TryGetValue(repositoryPath, out asset);

                if (asset is null && !string.IsNullOrWhiteSpace(assetId) && assetsById.TryGetValue(assetId, out var matchingAssets))
                {
                    asset = matchingAssets.FirstOrDefault();
                }

                if (asset is not null)
                {
                    if (!string.IsNullOrWhiteSpace(assetId) && !asset.AssetId.Equals(assetId, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"Row {rowNumber}: asset ID and repository path refer to different assets.");
                    assetId = asset.AssetId;
                    repositoryPath = asset.RepositoryPath;
                }
                else
                {
                    var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, repositoryPath.Replace('/', Path.DirectorySeparatorChar)));
                    if (!fullPath.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                    {
                        errors.Add($"Row {rowNumber}: selected repository file was not found: {repositoryPath}");
                        continue;
                    }
                    var computedId = PilotTokenSheetDecisionStore.ComputeAssetId(fullPath);
                    if (!string.IsNullOrWhiteSpace(assetId) && !assetId.Equals(computedId, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Row {rowNumber}: supplied asset ID does not match the selected file contents.");
                        continue;
                    }
                    assetId = computedId;
                }
            }
            else
            {
                assetId = string.Empty;
                repositoryPath = string.Empty;
            }

            decisions.Add(new PilotTokenSheetDecision
            {
                PilotId = pilot.PilotId,
                TargetId = pilot.TargetId,
                PilotName = pilot.Name,
                Status = status,
                AssetId = assetId,
                RepositoryPath = repositoryPath,
                Notes = Get(row, "notes")
            });
        }

        if (errors.Count > 0)
            throw new InvalidDataException("Decision validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(x => "- " + x)));
        if (decisions.Count != pilots.Count(p => p.AssetRoles.Any(r => r.Role == "PilotBaseTokenSheet" && r.Status != "clear")))
            Console.WriteLine($"Note: CSV contains {decisions.Count} decisions. Existing clear sheet matches remain unchanged.");

        return decisions;
    }

    private static bool Same(Dictionary<string, string> row, PilotTokenSheetDecision decision) =>
        Get(row, "pilotId").Equals(decision.PilotId, StringComparison.OrdinalIgnoreCase);

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value : string.Empty;

    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var records = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
        if (records.Count == 0) throw new InvalidDataException("Decision CSV is empty.");
        var headers = records[0];
        var required = new[] { "pilotId", "currentStatus", "selectedAssetId", "selectedRepositoryPath" };
        foreach (var name in required)
            if (!headers.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Decision CSV is missing required column '{name}'.");

        return records.Skip(1).Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v))).Select(values =>
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++) row[headers[i]] = i < values.Count ? values[i] : string.Empty;
            return row;
        }).ToList();
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new List<string>(); }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        if (rows.Count > 0 && rows[0].Count > 0) rows[0][0] = rows[0][0].TrimStart('\uFEFF');
        return rows;
    }

    private static void ShowUsage() => Console.WriteLine("  apply-pilot-token-sheet-decisions <first-edition-repo-folder> <decisions.csv> [--candidates <1-50>]");
}
