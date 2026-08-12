using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Commands;

/// <summary>Produces a read-only, review-required catalogue of First Edition upgrade mechanics.</summary>
public static partial class AuditFirstEditionUpgradeMechanicsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly MechanicRule[] Rules =
    {
        RuleAll("condition-assignment", "Assigns a condition", "assign", "condition"),
        Rule("adds-action", "Adds an action", "action bar gains"),
        Rule("action-icon-check", "Checks whether an action icon exists", "have the [boost] action icon", "have the [barrel roll] action icon", "if you have the [boost] action icon"),
        Rule("free-or-extra-action", "Grants or changes actions", "free action", "perform an action", "perform 1 action", "additional action"),
        Rule("maneuver-difficulty-change", "Changes maneuver difficulty or colour", "as green maneuvers", "as a white maneuver", "as a red maneuver", "difficulty of"),
        Rule("maneuver-colour-trigger", "Triggers from maneuver colour", "reveal a green maneuver", "execute a green maneuver", "executing a green maneuver", "executes a green maneuver", "execute a red maneuver", "executing a red maneuver", "reveal a red maneuver", "executes a white maneuver"),
        Rule("maneuver-execution", "Changes maneuver execution", "execute a maneuver", "reveal your maneuver", "maneuver template", "instead of executing"),
        Rule("upgrade-slot-change", "Adds, removes or converts upgrade slots", "upgrade bar gains", "upgrade bar loses", "upgrade icons and gains", "upgrade icon as", "upgrade icons as"),
        Rule("equip-restriction", "Changes what the ship may equip", "cannot equip", "may equip", "can equip", "equip up to", "you can equip"),
        Rule("stat-change", "Changes ship statistics", "increase your attack", "decrease your attack", "increase your agility", "decrease your agility", "increase your hull", "increase your shield", "reduce your primary attack", "reduce its agility", "reduce that ship's agility", "doubles his agility"),
        Rule("stat-value-dependent", "Effect depends on a current stat value", "up to your shield value", "equal to your agility value", "agility value lower than", "up to your primary weapon value", "primary weapon value is", "if your agility value is", "if your shield value is"),
        Rule("arc-or-weapon-interaction", "Interacts with arcs or weapon types", "firing arc", "auxiliary firing arc", "primary weapon", "secondary weapon"),
        Rule("token-assignment", "Assigns or receives tokens", "assign 1", "receive 1", "receives 1", "token to"),
        Rule("token-spend-or-remove", "Spends, removes or transfers tokens", "spend", "remove 1", "discard a focus", "transfer"),
        Rule("target-lock", "Changes target-lock mechanics", "target lock", "acquire a target lock"),
        Rule("dice-modification", "Modifies dice or results", "reroll", "change 1", "change all", "add 1 die", "roll 1 additional", "dice results"),
        Rule("attack-execution-or-restriction", "Executes or changes attack permissions", "perform this attack", "cannot attack", "may perform an attack", "attack twice", "cannot perform attacks"),
        Rule("range-dependent-effect", "Effect depends on measured range", "range 1-", "range 2-", "at range", "within range", "beyond range"),
        Rule("range-rule-change", "Extends, reduces or replaces normal range", "instead of at range", "range 3 and beyond", "range 1-5 (instead"),
        Rule("bomb-or-mine", "Drops or interacts with bombs/mines", "bomb", "mine", "detonator"),
        Rule("obstacle-or-overlap", "Interacts with obstacles or overlaps", "obstacle", "overlap", "overlapping", "touching another ship"),
        Rule("damage-interaction", "Deals, repairs or changes damage", "suffer 1 damage", "damage card", "critical damage", "repair", "discard 1 damage"),
        Rule("card-state-change", "Discards or flips cards", "discard this card", "flip this card", "turn this card"),
        Rule("setup-or-deployment", "Changes setup or deployment", "during setup", "place forces", "deploy", "docked"),
        Rule("initiative-or-pilot-skill", "Changes pilot skill", "pilot skill"),
        Rule("stress-interaction", "Assigns, removes or reacts to stress", "stress token", "stressed"),
        Rule("cloak-interaction", "Changes cloak mechanics", "cloak", "decloak"),
        Rule("energy-interaction", "Changes Epic energy mechanics", "energy"),
        Rule("regeneration", "Restores shields or hull", "recover 1 shield", "regain 1 shield", "recover up to", "restore"),
        Rule("once-per-round-or-limited-use", "Has limited-use timing", "once per round", "once per game", "first time each round")
        ,Rule("token-retention-or-cap", "Retains or limits tokens", "do not remove an unused", "cannot have more than 1", "unused focus token", "unused evade token")
        ,Rule("uncancellable-results", "Changes result cancellation", "cannot be canceled", "may cancel", "uncancelled")
        ,Rule("squad-point-cost", "Changes squad-point cost", "squad point cost", "negative squad")
        ,Rule("obstruction", "Changes obstruction mechanics", "obstruct an attack", "can obstruct", "obstruct enemy")
        ,Rule("shares-pilot-ability", "Shares or copies pilot abilities", "pilot ability of")
        ,Rule("additional-recovery", "Increases recovery effects", "recover 1 additional", "additional shield")
        ,Rule("upgrade-token-or-persistence", "Adds persistence tokens to upgrades", "illicit token", "token on each", "token on that card instead")
        ,Rule("dial-change", "Changes the selected dial maneuver", "rotate your dial", "corresponding bank maneuver", "reveal a turn maneuver")
        ,Rule("timing-order-change", "Changes timing or step order", "step after", "instead of before")
        ,Rule("direct-maneuver-action", "Executes a maneuver as an action", "action: execute")
        ,Rule("additional-maneuver", "Executes an additional or pre-reveal maneuver", "before you reveal your dial", "before you reveal your maneuver dial")
        ,Rule("reinforce-interaction", "Changes reinforce-token behaviour", "reinforce token")
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]);
            var dataPath = Path.Combine(repository, "source", "xwing-data", "data", "upgrades.js");
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "upgrade-mechanics"));
            RequireFile(dataPath, "xwing-data upgrade definitions");

            using var document = JsonDocument.Parse(File.ReadAllText(dataPath));
            var rows = document.RootElement.EnumerateArray().Select(item =>
            {
                var text = PlainText(Text(item, "text"));
                var proposedCategories = Rules.Where(rule => rule.MatchAll
                        ? rule.Terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
                        : rule.Terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    .Select(rule => new UpgradeMechanicCategory
                    {
                        Id = rule.Id,
                        Name = rule.Name,
                        Evidence = rule.Terms.Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList()
                    }).ToList();
                var structured = StructuredMetadata(item);
                var categories = proposedCategories.Concat(structured.Categories)
                    .GroupBy(category => category.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new UpgradeMechanicCategory
                    {
                        Id = group.Key,
                        Name = group.First().Name,
                        Evidence = group.SelectMany(category => category.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    }).ToList();
                var priority = RuntimePriority(categories.Select(category => category.Id));
                return new UpgradeMechanicsAuditRow
                {
                    Id = Int(item, "id"), Name = Text(item, "name"), Xws = Text(item, "xws"),
                    Slot = Text(item, "slot"), Text = text, Conditions = Strings(item, "conditions"),
                    RestrictedShips = structured.Ships,
                    RestrictedFactions = structured.Factions,
                    RestrictedSizes = structured.Sizes,
                    IsLimited = structured.IsLimited,
                    IsSquadLimited = structured.IsSquadLimited,
                    Grants = structured.Grants,
                    Categories = categories,
                    EffectTextSha256 = Sha256(text),
                    RuntimePriority = priority.Id,
                    RuntimePriorityReason = priority.Reason,
                    ReviewStatus = "review-required",
                    RuntimeStatus = "not-implemented-by-audit",
                    RequiresRuntimeReview = categories.Count > 0 || text.Length > 0
                };
            }).OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToList();

            var report = new UpgradeMechanicsAudit
            {
                SchemaVersion = 2,
                GeneratedUtc = DateTimeOffset.UtcNow,
                ClassificationMethod = "Conservative text-pattern proposals with evidence and runtime-priority triage; every classification requires human review.",
                UpgradeCount = rows.Count,
                CategorisedUpgradeCount = rows.Count(row => row.Categories.Count > 0),
                UncategorisedUpgradeCount = rows.Count(row => row.Categories.Count == 0),
                HighPriorityUpgradeCount = rows.Count(row => row.RuntimePriority == "high"),
                MediumPriorityUpgradeCount = rows.Count(row => row.RuntimePriority == "medium"),
                LowPriorityUpgradeCount = rows.Count(row => row.RuntimePriority == "low"),
                CategorySummary = rows.SelectMany(row => row.Categories)
                    .GroupBy(category => category.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new UpgradeMechanicCategorySummary
                    {
                        Id = group.Key, Name = group.First().Name,
                        UpgradeCount = rows.Count(row => row.Categories.Any(category => category.Id.Equals(group.Key, StringComparison.OrdinalIgnoreCase)))
                    }).OrderBy(summary => summary.Id, StringComparer.OrdinalIgnoreCase).ToList(),
                Upgrades = rows
            };

            Directory.CreateDirectory(output);
            var jsonPath = Path.Combine(output, "first-edition-upgrade-mechanics.json");
            var csvPath = Path.Combine(output, "first-edition-upgrade-mechanics-review.csv");
            var categoryCsvPath = Path.Combine(output, "first-edition-upgrade-mechanics-by-category.csv");
            var markdownPath = Path.Combine(output, "FIRST-EDITION-UPGRADE-MECHANICS-AUDIT.md");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
            WriteCsv(csvPath, rows);
            WriteCategoryCsv(categoryCsvPath, rows);
            WriteMarkdown(markdownPath, report);

            Console.WriteLine("UnifiedToolkit Phase 16 First Edition Upgrade Mechanics Audit");
            Console.WriteLine("================================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                    {repository}");
            Console.WriteLine($"Upgrade cards:                 {rows.Count}");
            Console.WriteLine($"Proposed mechanics categories: {report.CategorySummary.Count}");
            Console.WriteLine($"Categorised upgrades:          {report.CategorisedUpgradeCount}");
            Console.WriteLine($"Uncategorised upgrades:        {report.UncategorisedUpgradeCount}");
            Console.WriteLine($"High runtime priority:         {report.HighPriorityUpgradeCount}");
            Console.WriteLine($"Medium runtime priority:       {report.MediumPriorityUpgradeCount}");
            Console.WriteLine($"Low runtime priority:          {report.LowPriorityUpgradeCount}");
            Console.WriteLine($"Condition-source upgrades:     {rows.Count(row => row.Conditions.Count > 0)}");
            Console.WriteLine($"Manifest:                      {jsonPath}");
            Console.WriteLine($"Review CSV:                    {csvPath}");
            Console.WriteLine($"Category review CSV:           {categoryCsvPath}");
            Console.WriteLine($"Report:                        {markdownPath}");
            Console.WriteLine();
            Console.WriteLine("Audit completed. Classifications are proposals requiring review; no gameplay was modified.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition upgrade mechanics audit failed: {exception.Message}");
            return 1;
        }
    }

    private static MechanicRule Rule(string id, string name, params string[] terms) => new(id, name, false, terms);
    private static MechanicRule RuleAll(string id, string name, params string[] terms) => new(id, name, true, terms);
    private static string PlainText(string value) => Whitespace().Replace(Tag().Replace(value.Replace("<br />", " "), " "), " ").Trim();
    private static StructuredUpgradeMetadata StructuredMetadata(JsonElement item)
    {
        var ships = Strings(item, "ship");
        var factions = StringOrStrings(item, "faction");
        var sizes = Strings(item, "size");
        var isLimited = Boolean(item, "limited");
        var isSquadLimited = Boolean(item, "squadLimited");
        var grants = item.TryGetProperty("grants", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(grant => new UpgradeMechanicGrant
            {
                Type = Text(grant, "type"),
                Name = Text(grant, "name"),
                Value = grant.TryGetProperty("value", out var amount) && amount.TryGetInt32(out var number) ? number : null
            }).ToList()
            : new List<UpgradeMechanicGrant>();
        var categories = new List<UpgradeMechanicCategory>();
        if (ships.Count + factions.Count + sizes.Count > 0)
            categories.Add(StructuredCategory("equip-restriction", "Changes what the ship may equip", new[] {
                ships.Count > 0 ? $"structured ship restriction: {string.Join('|', ships)}" : "",
                factions.Count > 0 ? $"structured faction restriction: {string.Join('|', factions)}" : "",
                sizes.Count > 0 ? $"structured size restriction: {string.Join('|', sizes)}" : ""
            }));
        if (isLimited || isSquadLimited)
            categories.Add(StructuredCategory("limited-equip-count", "Limits how many copies may be equipped", new[] {
                isLimited ? "structured limited=true" : "", isSquadLimited ? "structured squadLimited=true" : ""
            }));
        foreach (var grantType in grants.GroupBy(grant => grant.Type, StringComparer.OrdinalIgnoreCase))
        {
            var id = grantType.Key.ToLowerInvariant() switch { "action" => "adds-action", "slot" => "upgrade-slot-change", "stats" => "stat-change", _ => "structured-grant" };
            var name = grantType.Key.ToLowerInvariant() switch { "action" => "Adds an action", "slot" => "Adds, removes or converts upgrade slots", "stats" => "Changes ship statistics", _ => "Provides a structured grant" };
            categories.Add(StructuredCategory(id, name, grantType.Select(grant => $"structured grant: {grant.Type}/{grant.Name}/{grant.Value?.ToString() ?? "n/a"}")));
        }
        return new(ships, factions, sizes, isLimited, isSquadLimited, grants, categories);
    }
    private static UpgradeMechanicCategory StructuredCategory(string id, string name, IEnumerable<string> evidence) => new()
    {
        Id = id, Name = name, Evidence = evidence.Where(entry => entry.Length > 0).ToList()
    };
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static RuntimePriorityResult RuntimePriority(IEnumerable<string> categoryIds)
    {
        var ids = categoryIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var high = new[] { "condition-assignment", "adds-action", "free-or-extra-action", "maneuver-difficulty-change", "maneuver-execution", "additional-maneuver",
            "upgrade-slot-change", "equip-restriction", "stat-change", "token-assignment", "token-spend-or-remove", "target-lock", "bomb-or-mine",
            "damage-interaction", "setup-or-deployment", "cloak-interaction", "energy-interaction", "regeneration",
            "upgrade-token-or-persistence", "dial-change", "direct-maneuver-action", "reinforce-interaction" };
        var medium = new[] { "maneuver-colour-trigger", "arc-or-weapon-interaction", "dice-modification", "attack-execution-or-restriction",
            "range-rule-change", "obstacle-or-overlap", "card-state-change", "initiative-or-pilot-skill", "stress-interaction", "uncancellable-results", "obstruction",
            "shares-pilot-ability", "additional-recovery" };
        var matchedHigh = high.Where(ids.Contains).ToList();
        if (matchedHigh.Count > 0) return new("high", $"State-changing mechanics: {string.Join(", ", matchedHigh)}");
        var matchedMedium = medium.Where(ids.Contains).ToList();
        if (matchedMedium.Count > 0) return new("medium", $"Resolution-time mechanics: {string.Join(", ", matchedMedium)}");
        return new("low", ids.Count == 0 ? "No proposed mechanic category." : $"Restriction, timing or passive mechanics: {string.Join(", ", ids.Order())}");
    }
    private static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int Int(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static List<string> Strings(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Select(entry => entry.GetString() ?? "").Where(entry => entry.Length > 0).ToList() : new();
    private static List<string> StringOrStrings(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        ? value.ValueKind == JsonValueKind.String ? new List<string> { value.GetString() ?? "" }.Where(entry => entry.Length > 0).ToList()
        : value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(entry => entry.GetString() ?? "").Where(entry => entry.Length > 0).ToList()
        : new List<string>() : new List<string>();
    private static bool Boolean(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
    private static void WriteCsv(string path, IEnumerable<UpgradeMechanicsAuditRow> rows)
    {
        var lines = new List<string> { "Id,Name,Xws,Slot,RestrictedShips,RestrictedFactions,RestrictedSizes,Limited,SquadLimited,StructuredGrants,RuntimePriority,RuntimePriorityReason,EffectTextSha256,ProposedCategories,MatchingEvidence,Conditions,ApprovedCategories,RejectedCategories,ReviewStatus,ReviewerNotes,RuntimeStatus,Text" };
        lines.AddRange(rows.Select(row => string.Join(',', new[] { row.Id.ToString(), Quote(row.Name), Quote(row.Xws), Quote(row.Slot),
            Quote(string.Join(';', row.RestrictedShips)), Quote(string.Join(';', row.RestrictedFactions)), Quote(string.Join(';', row.RestrictedSizes)),
            row.IsLimited.ToString(), row.IsSquadLimited.ToString(),
            Quote(string.Join(';', row.Grants.Select(grant => $"{grant.Type}/{grant.Name}/{grant.Value?.ToString() ?? "n/a"}"))),
            Quote(row.RuntimePriority), Quote(row.RuntimePriorityReason), Quote(row.EffectTextSha256),
            Quote(string.Join(';', row.Categories.Select(category => category.Id))),
            Quote(string.Join(';', row.Categories.Select(category => $"{category.Id}=[{string.Join('|', category.Evidence)}]"))),
            Quote(string.Join(';', row.Conditions)), Quote(""), Quote(""), Quote(row.ReviewStatus), Quote(""),
            Quote(row.RuntimeStatus), Quote(row.Text) })));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
    private static void WriteCategoryCsv(string path, IEnumerable<UpgradeMechanicsAuditRow> rows)
    {
        var lines = new List<string> { "CategoryId,CategoryName,UpgradeId,UpgradeName,Xws,Slot,RestrictedShips,RestrictedFactions,RestrictedSizes,RuntimePriority,Evidence,ReviewDecision,ReviewerNotes,Text" };
        lines.AddRange(rows.SelectMany(row => row.Categories.Select(category => string.Join(',', new[] {
            Quote(category.Id), Quote(category.Name), row.Id.ToString(), Quote(row.Name), Quote(row.Xws), Quote(row.Slot),
            Quote(string.Join(';', row.RestrictedShips)), Quote(string.Join(';', row.RestrictedFactions)), Quote(string.Join(';', row.RestrictedSizes)),
            Quote(row.RuntimePriority), Quote(string.Join(';', category.Evidence)), Quote(""), Quote(""), Quote(row.Text)
        }))));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
    private static void WriteMarkdown(string path, UpgradeMechanicsAudit audit)
    {
        var lines = new List<string> { "# Phase 16 First Edition Upgrade Mechanics Audit", "",
            $"- Upgrades: **{audit.UpgradeCount}**", $"- Categorised: **{audit.CategorisedUpgradeCount}**",
            $"- Uncategorised: **{audit.UncategorisedUpgradeCount}**", $"- High runtime priority: **{audit.HighPriorityUpgradeCount}**",
            $"- Medium runtime priority: **{audit.MediumPriorityUpgradeCount}**", $"- Low runtime priority: **{audit.LowPriorityUpgradeCount}**", "",
            "> These are conservative text-pattern proposals. They are not runtime implementations and require review.", "", "## Proposed categories", "" };
        lines.AddRange(audit.CategorySummary.Select(row => $"- {row.Name} (`{row.Id}`): **{row.UpgradeCount}**"));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit audit-first-edition-upgrade-mechanics <repository> [--output <folder>]");
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tag();
    [GeneratedRegex("\\s+")] private static partial Regex Whitespace();
    private sealed record MechanicRule(string Id, string Name, bool MatchAll, string[] Terms);
    private sealed record RuntimePriorityResult(string Id, string Reason);
    private sealed record StructuredUpgradeMetadata(List<string> Ships, List<string> Factions, List<string> Sizes,
        bool IsLimited, bool IsSquadLimited, List<UpgradeMechanicGrant> Grants, List<UpgradeMechanicCategory> Categories);
}

public sealed class UpgradeMechanicsAudit
{
    public int SchemaVersion { get; init; } public DateTimeOffset GeneratedUtc { get; init; }
    public string ClassificationMethod { get; init; } = ""; public int UpgradeCount { get; init; }
    public int CategorisedUpgradeCount { get; init; } public int UncategorisedUpgradeCount { get; init; }
    public int HighPriorityUpgradeCount { get; init; } public int MediumPriorityUpgradeCount { get; init; }
    public int LowPriorityUpgradeCount { get; init; }
    public List<UpgradeMechanicCategorySummary> CategorySummary { get; init; } = new();
    public List<UpgradeMechanicsAuditRow> Upgrades { get; init; } = new();
}
public sealed class UpgradeMechanicsAuditRow
{
    public int Id { get; init; } public string Name { get; init; } = ""; public string Xws { get; init; } = "";
    public string Slot { get; init; } = ""; public string Text { get; init; } = ""; public List<string> Conditions { get; init; } = new();
    public List<string> RestrictedShips { get; init; } = new(); public List<string> RestrictedFactions { get; init; } = new();
    public List<string> RestrictedSizes { get; init; } = new(); public bool IsLimited { get; init; } public bool IsSquadLimited { get; init; }
    public List<UpgradeMechanicGrant> Grants { get; init; } = new();
    public string EffectTextSha256 { get; init; } = ""; public string RuntimePriority { get; init; } = "";
    public string RuntimePriorityReason { get; init; } = "";
    public List<UpgradeMechanicCategory> Categories { get; init; } = new(); public bool RequiresRuntimeReview { get; init; }
    public string ReviewStatus { get; init; } = ""; public string RuntimeStatus { get; init; } = "";
}
public sealed class UpgradeMechanicCategory
{
    public string Id { get; init; } = ""; public string Name { get; init; } = ""; public List<string> Evidence { get; init; } = new();
}
public sealed class UpgradeMechanicCategorySummary
{
    public string Id { get; init; } = ""; public string Name { get; init; } = ""; public int UpgradeCount { get; init; }
}
public sealed class UpgradeMechanicGrant
{
    public string Type { get; init; } = ""; public string Name { get; init; } = ""; public int? Value { get; init; }
}
