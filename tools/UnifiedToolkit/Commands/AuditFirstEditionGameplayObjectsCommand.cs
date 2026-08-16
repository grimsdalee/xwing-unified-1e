using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

/// <summary>Phase 16E read-only inventory of First Edition tokens, devices, obstacles and mission objects.</summary>
public static class AuditFirstEditionGameplayObjectsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]);
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "gameplay-object-inventory"));
            var contexts = Path.GetFullPath(Option(args, "--legacy-contexts") ?? Path.Combine(repository,
                "ukb", "reports", "legacy-asset-contexts.csv"));
            var imports = Path.GetFullPath(Option(args, "--legacy-import") ?? Path.Combine(repository,
                "assets", "manifests", "legacy1e-import.json"));
            RequireDirectory(repository, "Repository");
            RequireFile(contexts, "Legacy asset contexts");
            RequireFile(imports, "Legacy import manifest");

            var candidates = DiscoverCandidates(repository, contexts, imports);
            var requirements = Requirements();
            var canonicalTokens = LoadCanonicalTokens(repository);
            foreach (var requirement in requirements)
            {
                requirement.Candidates = candidates.Where(candidate => Matches(requirement, candidate)).
                    OrderBy(candidate => SourceRank(candidate.Source)).ThenBy(candidate => candidate.RepositoryPath).ToList();
                var canonical = ResolveCanonical(requirement, canonicalTokens);
                requirement.CanonicalTokenIds = canonical.TokenIds;
                requirement.CanonicalDesignCount = canonical.DesignCount;
                requirement.Status = ResolveStatus(requirement);
                requirement.Recommendation = Recommendation(requirement);
            }

            var mechanics = LoadMechanicDemand(repository);
            var document = new FirstEditionGameplayObjectInventory
            {
                SchemaVersion = 1,
                GeneratedUtc = DateTimeOffset.UtcNow,
                Policy = "Audit only. Candidate discovery does not approve, copy, convert or enable an asset.",
                RequiredObjectCount = requirements.Count(requirement => requirement.Policy == "required"),
                OptionalObjectCount = requirements.Count(requirement => requirement.Policy == "optional"),
                ExcludedObjectCount = requirements.Count(requirement => requirement.Policy == "excluded-second-edition"),
                CanonicalCount = requirements.Count(requirement => requirement.Status == "canonical"),
                PartialCanonicalCount = requirements.Count(requirement => requirement.Status == "partial-canonical"),
                CandidateReviewCount = requirements.Count(requirement => requirement.Status is "candidate-review-required" or "partial-canonical"),
                MissingCount = requirements.Count(requirement => requirement.Status == "missing"),
                CandidateAssetCount = candidates.Count,
                Requirements = requirements,
                Candidates = candidates,
                MechanicsDemand = mechanics
            };

            Directory.CreateDirectory(output);
            var jsonPath = Path.Combine(output, "first-edition-gameplay-objects.json");
            var requirementsPath = Path.Combine(output, "first-edition-gameplay-object-requirements.csv");
            var candidatesPath = Path.Combine(output, "gameplay-object-asset-candidates.csv");
            var reportPath = Path.Combine(output, "FIRST-EDITION-GAMEPLAY-OBJECT-INVENTORY.md");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
            WriteRequirements(requirementsPath, requirements);
            WriteCandidates(candidatesPath, candidates);
            WriteReport(reportPath, document);

            Console.WriteLine("UnifiedToolkit Phase 16E First Edition Gameplay Object Inventory");
            Console.WriteLine("================================================================");
            Console.WriteLine();
            Console.WriteLine($"Required object definitions:       {document.RequiredObjectCount}");
            Console.WriteLine($"Optional/review definitions:       {document.OptionalObjectCount}");
            Console.WriteLine($"Second Edition exclusions:         {document.ExcludedObjectCount}");
            Console.WriteLine($"Already canonical:                 {document.CanonicalCount}");
            Console.WriteLine($"Partially canonical:               {document.PartialCanonicalCount}");
            Console.WriteLine($"Candidate review required:         {document.CandidateReviewCount}");
            Console.WriteLine($"Missing candidate evidence:        {document.MissingCount}");
            Console.WriteLine($"Discovered candidate asset files:  {document.CandidateAssetCount}");
            Console.WriteLine($"Upgrade mechanics demand records:  {document.MechanicsDemand.Sum(row => row.UpgradeCount)}");
            Console.WriteLine();
            Console.WriteLine($"Inventory:    {jsonPath}");
            Console.WriteLine($"Requirements: {requirementsPath}");
            Console.WriteLine($"Candidates:   {candidatesPath}");
            Console.WriteLine($"Report:       {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Audit completed. No assets, mappings, Lua scripts or gameplay state were modified.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition gameplay-object audit failed: {exception.Message}");
            return 1;
        }
    }

    private static List<GameplayObjectRequirement> Requirements() => new()
    {
        Required("focus", "token", "Focus token", "focus"),
        Required("evade", "token", "Evade token", "evade"),
        Required("stress", "token", "Stress token", "stress"),
        Required("ion", "token", "Ion token", "ion"),
        Required("target-lock", "token", "Target Lock token (approved single owner-labelled compatibility model)", "target lock", "targetlock", "tlbag", "tl"),
        Required("shield", "token", "Shield token", "shield"),
        Required("cloak", "token", "Cloak token", "cloak"),
        Required("tractor", "token", "Tractor beam token", "tractor"),
        RequiredSet("reinforce", "token", "Reinforce tokens (Epic and small-ship)", 2, "reinforce"),
        Required("energy", "token", "Epic energy token", "energy"),
        Required("weapons-disabled", "token", "Weapons disabled token", "weapons disabled", "weaponsdisabled", "disarm"),
        Required("jam", "token", "Jam token", "jam"),
        Required("ordnance", "upgrade-token", "Ordnance token", "ordnance"),
        RequiredSet("condition-tokens", "condition-token", "First Edition condition tokens", 9, "condition token", "conditiontokens"),
        Required("seismic-charge", "bomb", "Seismic Charge token", "seismic charge", "seismic"),
        Required("proton-bomb", "bomb", "Proton Bomb token", "proton bomb", "protonbomb"),
        Required("ion-bomb", "bomb", "Ion Bomb token", "ion bomb", "ionbomb"),
        Required("thermal-detonator", "bomb", "Thermal Detonator token", "thermal detonator", "thermalbomb"),
        Required("bomblet", "bomb", "Bomblet token", "bomblet"),
        Required("proximity-mine", "mine", "Proximity Mine token", "proximity mine", "proximity"),
        Required("cluster-mine", "mine", "Cluster Mine tokens", "cluster mine", "clustermine"),
        Required("conner-net", "mine", "Conner Net token", "conner net", "connor net", "conner", "connor"),
        Required("rigged-cargo", "device", "Rigged Cargo Chute debris token", "rigged cargo", "riggedcargo"),
        RequiredSet("core-asteroids", "obstacle-set", "Core Set asteroid tokens", 6, "core asteroid", "core1", "core2", "core3", "core4", "core5", "core6"),
        RequiredSet("tfa-asteroids", "obstacle-set", "The Force Awakens asteroid tokens", 6, "tfa asteroid", "tfa1", "tfa2", "tfa3", "tfa4", "tfa5", "tfa6"),
        RequiredSet("debris-clouds", "obstacle-set", "Debris cloud tokens", 6, "debris cloud", "debris1", "debris2", "debris3", "debris4", "debris5", "debris6"),
        Optional("critical-hit-marker", "marker", "Critical hit marker", "critical hit", "critical"),
        Optional("bomb-drop-template", "template", "Bomb drop template", "bombdropper", "bomb drop"),
        Optional("mission-cargo", "mission", "Mission cargo token", "mission cargo", "cargo token", "cargo"),
        Optional("mission-satellite", "mission", "Mission satellite token", "satellite"),
        Optional("mission-minefield", "mission", "Mission minefield tokens", "minefield"),
        Excluded("calculate", "token", "Second Edition calculate token", "calculate"),
        Excluded("force", "token", "Second Edition force token", "force"),
        Excluded("charge", "token", "Second Edition charge token", "charge"),
        Excluded("strain", "token", "Second Edition strain token", "strain"),
        Excluded("deplete", "token", "Second Edition deplete token", "deplete"),
        Excluded("gas-clouds", "obstacle-set", "Second Edition gas clouds", "gas cloud", "gascloud"),
        Excluded("remote-objects", "remote", "Second Edition remote objects", "remote", "probe droid", "buzz droid", "commandos"),
        Excluded("fuse", "token", "Second Edition fuse marker", "fuse")
    };

    private static GameplayObjectRequirement Required(string id, string category, string name, params string[] aliases) =>
        new() { Id = id, Category = category, Name = name, Policy = "required", ExpectedDesignCount = 1, Aliases = aliases.ToList() };
    private static GameplayObjectRequirement RequiredSet(string id, string category, string name, int count, params string[] aliases) =>
        new() { Id = id, Category = category, Name = name, Policy = "required", ExpectedDesignCount = count, Aliases = aliases.ToList() };
    private static GameplayObjectRequirement Optional(string id, string category, string name, params string[] aliases) =>
        new() { Id = id, Category = category, Name = name, Policy = "optional", ExpectedDesignCount = 1, Aliases = aliases.ToList() };
    private static GameplayObjectRequirement Excluded(string id, string category, string name, params string[] aliases) =>
        new() { Id = id, Category = category, Name = name, Policy = "excluded-second-edition", ExpectedDesignCount = 0, Aliases = aliases.ToList() };

    private static List<GameplayObjectAssetCandidate> DiscoverCandidates(string repository, string contextsPath, string importPath)
    {
        var results = new List<GameplayObjectAssetCandidate>();
        var roots = new[]
        {
            (Path.Combine(repository, "assets", "source", "unified1e", "condition-tokens"), "unified1e"),
            (Path.Combine(repository, "assets", "source", "unified25", "assets", "Items", "tokens"), "unified25"),
            (Path.Combine(repository, "assets", "source", "unified25", "assets", "textures", "bombs"), "unified25"),
            (Path.Combine(repository, "assets", "source", "unified25", "assets", "textures", "obstacles"), "unified25"),
            (Path.Combine(repository, "assets", "source", "unified25", "assets", "models", "obstacles"), "unified25"),
            (Path.Combine(repository, "assets", "source", "legacy1e-non-pilot", "steamusercontent-a.akamaihd.net"), "legacy1e-sorted"),
            (Path.Combine(repository, "assets", "source", "xwvassal", "images"), "xwvassal")
        };
        foreach (var (root, source) in roots.Where(item => Directory.Exists(item.Item1)))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is not (".png" or ".jpg" or ".jpeg" or ".obj")) continue;
                var relative = Relative(repository, path);
                var category = source == "legacy1e-sorted" ? InferSortedLegacyCategory(relative) : InferCategory(relative);
                if ((source == "xwvassal" || source == "legacy1e-sorted") && category == "other") continue;
                results.Add(new GameplayObjectAssetCandidate
                {
                    Source = source,
                    Category = category,
                    NameEvidence = Path.GetFileNameWithoutExtension(path),
                    RepositoryPath = relative,
                    Extension = extension,
                    SizeBytes = new FileInfo(path).Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                });
            }
        }
        results.AddRange(LegacyCandidates(repository, contextsPath, importPath));
        return results.GroupBy(candidate => $"{candidate.Source}|{candidate.RepositoryPath}|{candidate.NameEvidence}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).OrderBy(candidate => candidate.Source).ThenBy(candidate => candidate.Category)
            .ThenBy(candidate => candidate.RepositoryPath).ToList();
    }

    private static List<GameplayObjectAssetCandidate> LegacyCandidates(string repository, string contextsPath, string importPath)
    {
        using var importDocument = JsonDocument.Parse(File.ReadAllText(importPath));
        var imports = importDocument.RootElement.GetProperty("entries").EnumerateArray().Select(item => new
        {
            Url = Url(Text(item, "sourceUrl")), Destination = Text(item, "destinationRepositoryPath"), Status = Text(item, "status")
        }).Where(item => (item.Status is "downloaded" or "unchanged") && !string.IsNullOrWhiteSpace(item.Destination))
          .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var relocatedRoot = Path.Combine(repository, "assets", "source", "legacy1e-non-pilot", "steamusercontent-a.akamaihd.net");
        var relocatedFiles = Directory.Exists(relocatedRoot)
            ? Directory.EnumerateFiles(relocatedRoot, "*", SearchOption.AllDirectories)
                .GroupBy(path => Path.GetFileName(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var results = new List<GameplayObjectAssetCandidate>();
        foreach (var row in Csv.Read(contextsPath))
        {
            var url = Url(row.GetValueOrDefault("SourceUrl") ?? "");
            if (!imports.TryGetValue(url, out var import)) continue;
            var path = Path.Combine(repository, import.Destination.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                var fileName = Path.GetFileName(import.Destination);
                if (!relocatedFiles.TryGetValue(fileName, out var relocated) || relocated.Count == 0) continue;
                path = relocated[0];
            }
            if (!File.Exists(path)) continue;
            var name = row.GetValueOrDefault("ObjectNickname") ?? "";
            if (string.IsNullOrWhiteSpace(name)) name = row.GetValueOrDefault("ObjectName") ?? "";
            var container = row.GetValueOrDefault("ContainerText") ?? "";
            var evidence = $"{name} {container}".Trim();
            var category = InferCategory($"{Relative(repository, path)} {evidence}");
            if (category == "other") continue;
            results.Add(new GameplayObjectAssetCandidate
            {
                Source = "legacy1e", Category = category, NameEvidence = evidence,
                RepositoryPath = Relative(repository, path), Extension = Path.GetExtension(path).ToLowerInvariant(),
                SizeBytes = new FileInfo(path).Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), SourceUrl = url
            });
        }
        return results;
    }

    private static bool Matches(GameplayObjectRequirement requirement, GameplayObjectAssetCandidate candidate)
    {
        var evidence = Normalise(candidate.NameEvidence);
        if (requirement.Id == "condition-tokens" && candidate.Source == "unified1e") return true;
        if (requirement.Category is "token" or "upgrade-token" or "marker" && candidate.Category != "token") return false;
        if (requirement.Category == "bomb" && candidate.Category != "bomb") return false;
        if (requirement.Category == "mine" && candidate.Category != "mine") return false;
        if (requirement.Policy == "excluded-second-edition" &&
            (requirement.Id is "calculate" or "force" or "charge" or "strain" or "deplete" or "fuse"))
            return evidence.StartsWith(Normalise(requirement.Id), StringComparison.Ordinal);
        return requirement.Aliases.Any(alias =>
        {
            var key = Normalise(alias);
            return key.Length <= 2 ? evidence == key : evidence.Contains(key, StringComparison.Ordinal);
        });
    }

    private static CanonicalResolution ResolveCanonical(GameplayObjectRequirement requirement,
        IReadOnlyDictionary<string, CanonicalGameplayToken> tokens)
    {
        var acceptedIds = requirement.Id switch
        {
            "tractor" => new[] { "tractor-beam" },
            "reinforce" => new[] { "reinforce", "reinforce-epic" },
            "critical-hit-marker" => new[] { "critical-hit" },
            _ => new[] { requirement.Id }
        };
        var matched = acceptedIds.Where(tokens.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var canonicalCandidateCount = requirement.Candidates
            .Where(candidate => candidate.Source == "unified1e")
            .Select(candidate => candidate.RepositoryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new CanonicalResolution(matched.Count + canonicalCandidateCount, matched);
    }

    private static string ResolveStatus(GameplayObjectRequirement requirement)
    {
        if (requirement.Policy == "excluded-second-edition") return requirement.Candidates.Count > 0 ? "edition-incompatible-evidence" : "excluded-no-evidence";
        if (requirement.CanonicalDesignCount >= requirement.ExpectedDesignCount) return "canonical";
        if (requirement.CanonicalDesignCount > 0) return "partial-canonical";
        if (requirement.Candidates.Count > 0) return "candidate-review-required";
        return "missing";
    }

    private static string Recommendation(GameplayObjectRequirement requirement) => requirement.Status switch
    {
        "canonical" => "Retain canonical First Edition asset; verify runtime object construction later.",
        "partial-canonical" => $"Retain {requirement.CanonicalDesignCount} canonical design(s); locate or approve the remaining {requirement.ExpectedDesignCount - requirement.CanonicalDesignCount}.",
        "candidate-review-required" => "Visually compare candidates with original First Edition components before import or reuse.",
        "missing" => requirement.Policy == "optional" ? "Defer unless required by a selected mission." : "Locate an original First Edition source or scan the physical component.",
        "edition-incompatible-evidence" => "Exclude from First Edition runtime unless a separately approved First Edition equivalent is identified.",
        _ => "No action required."
    };

    private static Dictionary<string, CanonicalGameplayToken> LoadCanonicalTokens(string repository)
    {
        var root = Path.Combine(repository, "assets", "source", "unified1e", "reference", "gameplay-objects");
        var results = new Dictionary<string, CanonicalGameplayToken>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return results;
        foreach (var manifest in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (!document.RootElement.TryGetProperty("Tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array) continue;
            foreach (var token in tokens.EnumerateArray())
            {
                var id = Text(token, "Id");
                var meshPath = Text(token, "MeshPath");
                var facePath = Text(token, "FacePath");
                var objectType = Text(token, "ObjectType");
                var customToken = objectType.Equals("Custom_Token", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(facePath) ||
                    (!customToken && string.IsNullOrWhiteSpace(meshPath))) continue;
                var face = Path.Combine(repository, facePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(face)) continue;
                if (!customToken)
                {
                    var mesh = Path.Combine(repository, meshPath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(mesh)) continue;
                }
                results[id] = new CanonicalGameplayToken(id, Relative(repository, manifest), meshPath.Replace('\\', '/'), facePath.Replace('\\', '/'));
            }
        }
        return results;
    }

    private static List<GameplayMechanicsDemand> LoadMechanicDemand(string repository)
    {
        var path = Path.Combine(repository, "assets", "source", "unified1e", "reference", "cards", "upgrade-mechanics.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var demanded = new[] { "token-assignment", "token-spend-or-remove", "token-retention-or-cap", "upgrade-token-or-persistence", "bomb-or-mine", "obstacle-or-overlap", "condition-assignment", "energy-interaction", "reinforce-interaction", "cloak-interaction", "target-lock" };
        return demanded.Select(id => new GameplayMechanicsDemand
        {
            MechanicId = id,
            UpgradeCount = document.RootElement.GetProperty("upgrades").EnumerateArray().Count(upgrade =>
                upgrade.GetProperty("mechanics").EnumerateArray().Any(mechanic => Text(mechanic, "id") == id))
        }).Where(row => row.UpgradeCount > 0).OrderByDescending(row => row.UpgradeCount).ThenBy(row => row.MechanicId).ToList();
    }

    private static string InferCategory(string value)
    {
        var key = value.ToLowerInvariant();
        if (key.Contains("general-tokens") || key.Contains("general_tokens")) return "token";
        if (key.Contains("bomb-tokens") || key.Contains("bomb_tokens")) return "bomb";
        if (key.Contains("asteroid-tokens") || key.Contains("asteroid_tokens")) return "asteroid";
        if (key.Contains("condition-tokens") || key.Contains("condition_tokens")) return "condition-token";
        if (key.Contains("condition")) return "condition-token";
        if (key.Contains("asteroid") || key.Contains("astroid") || key.Contains("core1") || key.Contains("tfa1")) return "asteroid";
        if (key.Contains("debris") || key.Contains("riggedcargo") || key.Contains("loosecargo")) return "debris";
        if (key.Contains("mine") || key.Contains("conner") || key.Contains("connor") || key.Contains("proximity")) return "mine";
        if (key.Contains("bomb") || key.Contains("seismic") || key.Contains("thermal") || key.Contains("bomblet")) return "bomb";
        if (key.Contains("remote") || key.Contains("probe") || key.Contains("buzz") || key.Contains("commandos")) return "remote";
        if (key.Contains("token") || key.Contains("focus") || key.Contains("evade") || key.Contains("stress") || key.Contains("cloak") || key.Contains("tractor") || key.Contains("reinforce") || key.Contains("shield")) return "token";
        if (key.Contains("cargo") || key.Contains("satellite") || key.Contains("mission")) return "mission";
        return "other";
    }

    private static string InferSortedLegacyCategory(string value)
    {
        var key = value.Replace('\\', '/').ToLowerInvariant();
        if (key.Contains("/general-tokens/")) return "token";
        if (key.Contains("/condition-tokens/")) return "condition-token";
        if (key.Contains("/bomb-tokens/")) return "bomb";
        if (key.Contains("/asteroid-tokens/")) return "asteroid";
        return "other";
    }

    private static int SourceRank(string source) => source switch { "unified1e" => 0, "xwvassal" => 1, "legacy1e-sorted" => 2, "legacy1e" => 3, "unified25" => 4, _ => 5 };
    private static void WriteRequirements(string path, IEnumerable<GameplayObjectRequirement> rows)
    {
        var lines = new List<string> { "Id,Category,Name,Policy,ExpectedDesignCount,CanonicalDesignCount,CanonicalTokenIds,Status,CandidateCount,Recommendation" };
        lines.AddRange(rows.Select(row => string.Join(',', new[]
        {
            Quote(row.Id), Quote(row.Category), Quote(row.Name), Quote(row.Policy),
            row.ExpectedDesignCount.ToString(), row.CanonicalDesignCount.ToString(), Quote(string.Join(';', row.CanonicalTokenIds)),
            Quote(row.Status), row.Candidates.Count.ToString(), Quote(row.Recommendation)
        })));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
    private static void WriteCandidates(string path, IEnumerable<GameplayObjectAssetCandidate> rows)
    {
        var lines = new List<string> { "Source,Category,NameEvidence,RepositoryPath,Extension,SizeBytes,Sha256,SourceUrl" };
        lines.AddRange(rows.Select(row => string.Join(',', new[]
        {
            Quote(row.Source), Quote(row.Category), Quote(row.NameEvidence), Quote(row.RepositoryPath),
            Quote(row.Extension), row.SizeBytes.ToString(), Quote(row.Sha256), Quote(row.SourceUrl)
        })));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
    private static void WriteReport(string path, FirstEditionGameplayObjectInventory audit)
    {
        var lines = new List<string>
        {
            "# Phase 16E First Edition Gameplay Object Inventory", "",
            $"- Required definitions: **{audit.RequiredObjectCount}**", $"- Optional/review definitions: **{audit.OptionalObjectCount}**",
            $"- Second Edition exclusions: **{audit.ExcludedObjectCount}**", $"- Already canonical: **{audit.CanonicalCount}**",
            $"- Partially canonical: **{audit.PartialCanonicalCount}**",
            $"- Candidate review required: **{audit.CandidateReviewCount}**", $"- Missing evidence: **{audit.MissingCount}**",
            $"- Candidate asset files: **{audit.CandidateAssetCount}**", "",
            "Catalogue presence is evidence only. No Unified 2.5 or legacy asset is approved for First Edition reuse by this audit.", "",
            "## Requirements", ""
        };
        foreach (var group in audit.Requirements.GroupBy(row => row.Category).OrderBy(group => group.Key))
        {
            lines.Add($"### {group.Key}"); lines.Add("");
            lines.AddRange(group.Select(row => $"- **{row.Name}** — `{row.Status}`; canonical={row.CanonicalDesignCount}/{row.ExpectedDesignCount}; canonical IDs={FormatIds(row.CanonicalTokenIds)}; candidates={row.Candidates.Count}; {row.Recommendation}"));
            lines.Add("");
        }
        lines.Add("## Upgrade mechanics demand"); lines.Add("");
        lines.AddRange(audit.MechanicsDemand.Select(row => $"- `{row.MechanicId}`: {row.UpgradeCount} upgrades"));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string Normalise(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string Url(string value) => value.Trim().Replace("http://", "https://", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Quote(object? value) => $"\"{(value?.ToString() ?? "").Replace("\"", "\"\"")}\"";
    private static string FormatIds(IReadOnlyCollection<string> ids) => ids.Count == 0 ? "none" : string.Join(", ", ids.Select(id => $"`{id}`"));
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit audit-first-edition-gameplay-objects <repository> [--output <folder>] [--legacy-contexts <file>] [--legacy-import <file>]");
}

public sealed class FirstEditionGameplayObjectInventory
{
    public int SchemaVersion { get; init; } public DateTimeOffset GeneratedUtc { get; init; } public string Policy { get; init; } = "";
    public int RequiredObjectCount { get; init; } public int OptionalObjectCount { get; init; } public int ExcludedObjectCount { get; init; }
    public int CanonicalCount { get; init; } public int PartialCanonicalCount { get; init; } public int CandidateReviewCount { get; init; } public int MissingCount { get; init; } public int CandidateAssetCount { get; init; }
    public List<GameplayObjectRequirement> Requirements { get; init; } = new(); public List<GameplayObjectAssetCandidate> Candidates { get; init; } = new(); public List<GameplayMechanicsDemand> MechanicsDemand { get; init; } = new();
}
public sealed class GameplayObjectRequirement
{
    public string Id { get; init; } = ""; public string Category { get; init; } = ""; public string Name { get; init; } = ""; public string Policy { get; init; } = "";
    public int ExpectedDesignCount { get; init; } public int CanonicalDesignCount { get; set; } public List<string> CanonicalTokenIds { get; set; } = new(); public List<string> Aliases { get; init; } = new(); public string Status { get; set; } = ""; public string Recommendation { get; set; } = "";
    public List<GameplayObjectAssetCandidate> Candidates { get; set; } = new();
}
public sealed class GameplayObjectAssetCandidate
{
    public string Source { get; init; } = ""; public string Category { get; init; } = ""; public string NameEvidence { get; init; } = ""; public string RepositoryPath { get; init; } = "";
    public string Extension { get; init; } = ""; public long SizeBytes { get; init; } public string Sha256 { get; init; } = ""; public string SourceUrl { get; init; } = "";
}
public sealed class GameplayMechanicsDemand { public string MechanicId { get; init; } = ""; public int UpgradeCount { get; init; } }
public sealed record CanonicalGameplayToken(string Id, string ManifestPath, string MeshPath, string FacePath);
public sealed record CanonicalResolution(int DesignCount, List<string> TokenIds);
