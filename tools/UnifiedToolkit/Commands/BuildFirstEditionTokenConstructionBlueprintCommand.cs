using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 16E-R6 read-only construction blueprint and source audit for the
/// First Edition core gameplay tokens. No image, mesh, mapping, Lua or gameplay
/// object is created or modified by this command.
/// </summary>
public static class BuildFirstEditionTokenConstructionBlueprintCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly TokenDecisionSeed[] DecisionSeeds =
    [
        new("ordnance", "Ordnance", "new-chamfered-square", "new", "Token-ordnance.png", "provisional-existing", "none", "Outer printed border is incomplete; preserve the Vassal face while better source artwork is sought."),
        new("weapons-disabled", "Weapons Disabled", "extruded-round", "new-shared", "Token_Weapons_Disabled.png", "replacement-required", "existing-token-handler", "Use a reusable token with real thickness; complete outer border artwork is still required."),
        new("jam", "Jam", "pending-round-or-hexagonal", "decision-required", "Token-Jam.png", "replacement-required", "existing-token-handler", "Physical silhouette remains unresolved. Do not construct until round versus hexagonal is confirmed."),
        new("cloak", "Cloak", "new-custom-cloak", "new", "Token-Cloak.png", "provisional-existing", "existing-token-handler", "Create a mesh and collider matching the physical token outline."),
        new("ion", "Ion", "extruded-round", "new-shared", "Token-Ion.png", "replacement-required", "existing-token-handler", "Use a reusable token with real thickness; complete outer border artwork is still required."),
        new("stress", "Stress", "new-triangle", "new", "Stress.png", "provisional-existing", "existing-token-handler", "Create a triangular mesh/collider with correct flip behaviour and physical scale."),
        new("tractor", "Tractor", "extruded-rounded-square", "new-shared", "Token-tractor.png", "provisional-existing", "existing-token-handler", "Use a reusable rounded-square token with real thickness."),
        new("reinforce", "Reinforce", "extruded-round", "new-shared", "Token_Reinforce.png", "replacement-required", "reuse-unified25-reinforce-behaviour", "Use a reusable round token with real thickness; complete outer border artwork is still required."),
        new("energy", "Energy", "new-custom-energy", "new", "Token_Energy_full.png", "authoritative-existing", "native-flip-no-lua", "Use Token_Energy_full.png. Derive a spent face by colour treatment only; do not reuse Shield-specific Lua."),
        new("shield", "Shield", "reuse-unified25-shield-object", "reuse", "Token_Shield.png", "derived-faces-required", "reuse-unified25-shield-lua", "Reuse Unified 2.5 flip construction and Shield Lua. Prepare aligned active and darkened spent faces."),
        new("focus", "Focus", "extruded-round", "new-shared", "Token_Focus.png", "replacement-required", "existing-token-handler", "Use a reusable token with real thickness; complete outer border artwork is still required."),
        new("evade", "Evade", "extruded-round", "new-shared", "Token_Evade.png", "replacement-required", "existing-token-handler", "Use a reusable token with real thickness; complete outer border artwork is still required."),
        new("target-lock", "Target Lock", "reuse-unified25-target-lock", "reuse-approved", "TL.png", "reuse-approved", "reuse-unified25-target-lock-lua", "Reuse the Unified 2.5 single owner-labelled token, mesh, image and assignment integration under First Edition rules.")
    ];

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R6 Token Construction Blueprint");
        Console.WriteLine("=========================================================");
        Console.WriteLine();
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var savePath = Path.GetFullPath(args[1]);
            var inventoryPath = Path.GetFullPath(Option(args, "--inventory") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "gameplay-object-inventory", "first-edition-gameplay-objects.json"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "token-construction-blueprint"));
            RequireDirectory(repository, "Repository");
            RequireFile(savePath, "Unified 2.5 gameplay-object save");
            RequireFile(inventoryPath, "First Edition gameplay-object inventory");
            Directory.CreateDirectory(output);

            using var inventoryDocument = JsonDocument.Parse(File.ReadAllBytes(inventoryPath));
            using var saveDocument = JsonDocument.Parse(File.ReadAllBytes(savePath));
            var requirements = ReadRequirements(inventoryDocument.RootElement);
            var shieldEvidence = InspectShield(saveDocument.RootElement);
            var decisions = BuildDecisions(repository, requirements);
            var warnings = new List<string>();
            if (!shieldEvidence.Found) warnings.Add("A scripted Unified 2.5 Shield object was not found in the supplied save.");
            if (shieldEvidence.Found && !shieldEvidence.HardCodesShield) warnings.Add("Shield Lua was found but its Shield-specific marker could not be confirmed.");
            warnings.AddRange(decisions.Where(item => item.SelectedArtworkPath is null).Select(item => $"{item.Name} preferred artwork was not resolved locally."));

            var blueprint = new TokenConstructionBlueprint
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Policy = "Architecture and source review only. This blueprint imports, edits and enables nothing.",
                RepositoryRoot = NormalisePath(repository),
                InventoryPath = NormalisePath(inventoryPath),
                Unified25SavePath = NormalisePath(savePath),
                Unified25SaveSha256 = HashFile(savePath),
                ShieldRuntimeEvidence = shieldEvidence,
                Decisions = decisions,
                SharedMeshFamilies = BuildMeshFamilies(),
                WarningCount = warnings.Count,
                Warnings = warnings
            };

            var blueprintPath = Path.Combine(output, "first-edition-token-construction-blueprint.json");
            var sourceAuditPath = Path.Combine(output, "first-edition-token-source-audit.csv");
            var meshChecklistPath = Path.Combine(output, "first-edition-token-new-mesh-checklist.csv");
            var artworkChecklistPath = Path.Combine(output, "first-edition-token-artwork-checklist.csv");
            var reportPath = Path.Combine(output, "FIRST-EDITION-TOKEN-CONSTRUCTION-BLUEPRINT.md");
            File.WriteAllText(blueprintPath, JsonSerializer.Serialize(blueprint, JsonOptions), new UTF8Encoding(false));
            WriteSourceAudit(sourceAuditPath, decisions);
            WriteMeshChecklist(meshChecklistPath, blueprint.SharedMeshFamilies);
            WriteArtworkChecklist(artworkChecklistPath, decisions);
            WriteReport(reportPath, blueprint);

            Console.WriteLine($"Repository:                    {repository}");
            Console.WriteLine($"Inventory:                     {inventoryPath}");
            Console.WriteLine($"Unified 2.5 save:              {savePath}");
            Console.WriteLine($"Token decisions:               {decisions.Count}");
            Console.WriteLine($"Approved reuse decisions:      {decisions.Count(item => item.MeshStatus.Contains("reuse", StringComparison.OrdinalIgnoreCase))}");
            Console.WriteLine($"New/shared mesh families:      {blueprint.SharedMeshFamilies.Count(item => item.Status.StartsWith("new", StringComparison.OrdinalIgnoreCase))}");
            Console.WriteLine($"Artwork follow-ups:            {decisions.Count(item => item.ArtworkStatus is "replacement-required" or "derived-faces-required")}");
            Console.WriteLine($"Scripted Shield found:         {shieldEvidence.Found}");
            Console.WriteLine($"Shield Lua hard-codes Shield:  {shieldEvidence.HardCodesShield}");
            Console.WriteLine($"Shield Lua reusable unchanged: {shieldEvidence.ReusableForShieldUnchanged}");
            Console.WriteLine($"Energy can reuse Lua unchanged:{shieldEvidence.ReusableForEnergyUnchanged}");
            Console.WriteLine($"Warnings:                      {warnings.Count}");
            Console.WriteLine();
            Console.WriteLine($"Blueprint:       {blueprintPath}");
            Console.WriteLine($"Source audit:    {sourceAuditPath}");
            Console.WriteLine($"Mesh checklist:  {meshChecklistPath}");
            Console.WriteLine($"Artwork review:  {artworkChecklistPath}");
            Console.WriteLine($"Report:           {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Blueprint completed. No assets, meshes, mappings, Lua scripts or gameplay state were modified.");
            return warnings.Count == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Token construction blueprint failed: {exception.Message}");
            return 1;
        }
    }

    private static Dictionary<string, SourceRequirement> ReadRequirements(JsonElement root)
    {
        if (!TryProperty(root, "requirements", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Gameplay-object inventory does not contain a requirements array.");
        var result = new Dictionary<string, SourceRequirement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list.EnumerateArray())
        {
            var id = String(item, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var requirement = new SourceRequirement { Id = id, Name = String(item, "name") };
            if (TryProperty(item, "candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
            foreach (var candidate in candidates.EnumerateArray())
            {
                requirement.Candidates.Add(new TokenSourceCandidate
                {
                    Source = String(candidate, "source"),
                    NameEvidence = String(candidate, "nameEvidence"),
                    RepositoryPath = String(candidate, "repositoryPath"),
                    Extension = String(candidate, "extension"),
                    SizeBytes = Long(candidate, "sizeBytes"),
                    Sha256 = String(candidate, "sha256")
                });
            }
            result[id] = requirement;
        }
        return result;
    }

    private static List<TokenConstructionDecision> BuildDecisions(string repository, Dictionary<string, SourceRequirement> requirements)
    {
        var decisions = new List<TokenConstructionDecision>();
        foreach (var seed in DecisionSeeds)
        {
            requirements.TryGetValue(seed.Id, out var requirement);
            var candidates = requirement?.Candidates.Where(candidate => IsRaster(candidate.Extension)).ToList() ?? [];
            var selected = candidates.FirstOrDefault(candidate =>
                candidate.RepositoryPath.EndsWith('/' + seed.PreferredFilename, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(RepositoryFile(repository, candidate.RepositoryPath)));
            decisions.Add(new TokenConstructionDecision
            {
                Id = seed.Id,
                Name = seed.Name,
                MeshFamily = seed.MeshFamily,
                MeshStatus = seed.MeshStatus,
                PreferredArtworkFilename = seed.PreferredFilename,
                SelectedArtworkPath = selected?.RepositoryPath,
                SelectedArtworkSource = selected?.Source,
                SelectedArtworkSha256 = selected?.Sha256,
                ArtworkStatus = seed.ArtworkStatus,
                RuntimeStrategy = seed.RuntimeStrategy,
                Notes = seed.Notes,
                SourceCandidateCount = candidates.Count,
                SourceCandidates = candidates.OrderBy(candidate => SourceRank(candidate.Source)).ThenBy(candidate => candidate.RepositoryPath, StringComparer.OrdinalIgnoreCase).ToList()
            });
        }
        return decisions;
    }

    private static ShieldRuntimeEvidence InspectShield(JsonElement root)
    {
        if (!TryProperty(root, "ObjectStates", out var states) || states.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The Unified 2.5 save does not contain ObjectStates.");
        foreach (var item in Descendants(states))
        {
            if (!String(item, "Nickname").Equals("Shield", StringComparison.OrdinalIgnoreCase)) continue;
            var lua = String(item, "LuaScript");
            if (string.IsNullOrWhiteSpace(lua)) continue;
            var hardCodesShield = lua.Contains("__XW_TokenType = 'Shield'", StringComparison.Ordinal) || lua.Contains("Marker.Shield", StringComparison.Ordinal);
            var callsAssignmentApi = lua.Contains("getShipTokenIsAssignedTo", StringComparison.Ordinal);
            var reportsShield = lua.Contains("shield token", StringComparison.OrdinalIgnoreCase);
            var stateCount = 1 + (TryProperty(item, "States", out var objectStates) && objectStates.ValueKind == JsonValueKind.Object ? objectStates.EnumerateObject().Count() : 0);
            return new ShieldRuntimeEvidence
            {
                Found = true,
                Guid = String(item, "GUID"),
                LuaLength = lua.Length,
                LuaSha256 = HashText(lua),
                StateCount = stateCount,
                UsesNativeOnFlip = lua.Contains("function onFlip", StringComparison.Ordinal),
                CallsAssignmentApi = callsAssignmentApi,
                ReportsLostRecovered = lua.Contains(" lost ", StringComparison.Ordinal) && lua.Contains(" recovered ", StringComparison.Ordinal),
                HardCodesShield = hardCodesShield || reportsShield,
                ReusableForShieldUnchanged = hardCodesShield && callsAssignmentApi && reportsShield,
                ReusableForEnergyUnchanged = false,
                EnergyRecommendation = "Reuse the same physical two-face/flip construction with no object Lua. Native TTS flipping supplies the state change; Shield-specific logging must not be copied."
            };
        }
        return new ShieldRuntimeEvidence { EnergyRecommendation = "Shield runtime evidence was not found; Energy reuse cannot yet be verified." };
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            yield return item;
            if (TryProperty(item, "ContainedObjects", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in Descendants(children)) yield return child;
        }
    }

    private static List<TokenMeshFamily> BuildMeshFamilies() =>
    [
        new() { Id = "reuse-unified25-target-lock", Name = "Unified 2.5 Target Lock", Status = "reuse-approved", Consumers = ["target-lock"], Requirements = "Preserve TL.obj, TL.png and owner-labelled assignment Lua." },
        new() { Id = "reuse-unified25-shield-object", Name = "Unified 2.5 Shield object", Status = "reuse-approved", Consumers = ["shield"], Requirements = "Preserve flip construction and Shield Lua; replace only reviewed face artwork." },
        new() { Id = "extruded-round", Name = "Extruded round token", Status = "new-shared", Consumers = ["weapons-disabled", "ion", "reinforce", "focus", "evade"], Requirements = "Real side-wall depth, matching round collider, face UV isolated from side-wall UV, consistent physical scale." },
        new() { Id = "extruded-rounded-square", Name = "Extruded rounded-square token", Status = "new-shared", Consumers = ["tractor"], Requirements = "Real side-wall depth, rounded-square collider and isolated side-wall texture band." },
        new() { Id = "new-chamfered-square", Name = "Chamfered-square Ordnance token", Status = "new", Consumers = ["ordnance"], Requirements = "Four clipped corners, matching collider, opaque side walls and isolated face UV." },
        new() { Id = "new-triangle", Name = "Triangular Stress token", Status = "new", Consumers = ["stress"], Requirements = "Physical triangular outline, matching collider, real thickness and correct two-face flipping." },
        new() { Id = "new-custom-cloak", Name = "Custom Cloak token", Status = "new", Consumers = ["cloak"], Requirements = "Trace physical token silhouette without changing the original face symbol." },
        new() { Id = "new-custom-energy", Name = "Custom Energy token", Status = "new", Consumers = ["energy"], Requirements = "Match Token_Energy_full silhouette; active/spent faces aligned; native flip with no Shield Lua." },
        new() { Id = "pending-round-or-hexagonal", Name = "Jam token", Status = "decision-required", Consumers = ["jam"], Requirements = "Confirm physical First Edition silhouette before selecting or creating a mesh." }
    ];

    private static void WriteSourceAudit(string path, IEnumerable<TokenConstructionDecision> decisions)
    {
        var lines = new List<string> { "TokenId,TokenName,Selected,PreferredFilename,Source,RepositoryPath,Extension,SizeBytes,Sha256,NameEvidence" };
        foreach (var decision in decisions)
        foreach (var candidate in decision.SourceCandidates)
            lines.Add(Csv(decision.Id, decision.Name, (candidate.RepositoryPath == decision.SelectedArtworkPath).ToString(), decision.PreferredArtworkFilename, candidate.Source, candidate.RepositoryPath, candidate.Extension, candidate.SizeBytes.ToString(CultureInfo.InvariantCulture), candidate.Sha256, candidate.NameEvidence));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteMeshChecklist(string path, IEnumerable<TokenMeshFamily> families)
    {
        var lines = new List<string> { "MeshFamilyId,Name,Status,Consumers,Requirements,Decision,Notes" };
        lines.AddRange(families.Select(item => Csv(item.Id, item.Name, item.Status, string.Join(';', item.Consumers), item.Requirements, "", "")));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteArtworkChecklist(string path, IEnumerable<TokenConstructionDecision> decisions)
    {
        var lines = new List<string> { "TokenId,TokenName,ArtworkStatus,PreferredFilename,SelectedArtworkPath,RequiredWork,Decision,Notes" };
        foreach (var item in decisions)
        {
            var work = item.ArtworkStatus switch
            {
                "replacement-required" => "Search existing sources for a complete original border; scan the physical token only if no adequate source exists.",
                "derived-faces-required" => "Prepare aligned active and darkened/desaturated spent faces after explicit artwork approval.",
                "authoritative-existing" => "Preserve the authoritative image; derive a spent colour variant only after explicit artwork approval.",
                _ => "No image changes in this phase."
            };
            lines.Add(Csv(item.Id, item.Name, item.ArtworkStatus, item.PreferredArtworkFilename, item.SelectedArtworkPath, work, "", ""));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteReport(string path, TokenConstructionBlueprint blueprint)
    {
        var shield = blueprint.ShieldRuntimeEvidence;
        var lines = new List<string>
        {
            "# Phase 16E-R6 First Edition Token Construction Blueprint", "",
            "This is an architecture and source audit only. It creates or approves no image, mesh, Lua or gameplay object.", "",
            "## Shield and Energy runtime conclusion", "",
            $"- Scripted Shield found: **{shield.Found}**",
            $"- Shield Lua length: **{shield.LuaLength}**",
            $"- Shield Lua hard-codes Shield: **{shield.HardCodesShield}**",
            $"- Reusable unchanged for Shield: **{shield.ReusableForShieldUnchanged}**",
            $"- Reusable unchanged for Energy: **{shield.ReusableForEnergyUnchanged}**",
            $"- Energy plan: {shield.EnergyRecommendation}", "",
            "## Construction decisions", "",
            "| Token | Mesh | Mesh status | Artwork | Runtime |", "|---|---|---|---|---|"
        };
        lines.AddRange(blueprint.Decisions.Select(item => $"| {item.Name} | {item.MeshFamily} | {item.MeshStatus} | {item.ArtworkStatus} | {item.RuntimeStrategy} |"));
        lines.AddRange(["", "## Warnings", ""]);
        lines.AddRange(blueprint.Warnings.Count == 0 ? ["- None."] : blueprint.Warnings.Select(value => "- " + value));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static int SourceRank(string source) => source.ToLowerInvariant() switch { "unified1e" => 0, "xwvassal" => 1, "legacy1e-sorted" => 2, "legacy1e" => 3, "unified25" => 4, _ => 5 };
    private static bool IsRaster(string extension) => extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    private static string RepositoryFile(string repository, string path) => Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string Csv(params string?[] values) => string.Join(',', values.Select(value => $"\"{(value ?? "").Replace("\"", "\"\"")}\""));
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string NormalisePath(string value) => value.Replace('\\', '/');
    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1)).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static void ShowUsage() => Console.WriteLine("Usage: build-first-edition-token-construction-blueprint <first-edition-repo-folder> <unified25-gameplay-save.json> [--inventory <file>] [--output <folder>]");
    private static bool TryProperty(JsonElement item, string name, out JsonElement value) => item.TryGetProperty(name, out value) || item.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);
    private static string String(JsonElement item, string name) => TryProperty(item, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long Long(JsonElement item, string name) => TryProperty(item, name, out var value) && value.TryGetInt64(out var result) ? result : 0;

    private sealed record TokenDecisionSeed(string Id, string Name, string MeshFamily, string MeshStatus, string PreferredFilename, string ArtworkStatus, string RuntimeStrategy, string Notes);
    private sealed class SourceRequirement { public string Id { get; init; } = ""; public string Name { get; init; } = ""; public List<TokenSourceCandidate> Candidates { get; } = []; }
}

public sealed class TokenConstructionBlueprint
{
    public string SchemaVersion { get; init; } = "";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string Policy { get; init; } = "";
    public string RepositoryRoot { get; init; } = "";
    public string InventoryPath { get; init; } = "";
    public string Unified25SavePath { get; init; } = "";
    public string Unified25SaveSha256 { get; init; } = "";
    public ShieldRuntimeEvidence ShieldRuntimeEvidence { get; init; } = new();
    public List<TokenConstructionDecision> Decisions { get; init; } = [];
    public List<TokenMeshFamily> SharedMeshFamilies { get; init; } = [];
    public int WarningCount { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public sealed class ShieldRuntimeEvidence
{
    public bool Found { get; init; }
    public string Guid { get; init; } = "";
    public int LuaLength { get; init; }
    public string LuaSha256 { get; init; } = "";
    public int StateCount { get; init; }
    public bool UsesNativeOnFlip { get; init; }
    public bool CallsAssignmentApi { get; init; }
    public bool ReportsLostRecovered { get; init; }
    public bool HardCodesShield { get; init; }
    public bool ReusableForShieldUnchanged { get; init; }
    public bool ReusableForEnergyUnchanged { get; init; }
    public string EnergyRecommendation { get; init; } = "";
}

public sealed class TokenConstructionDecision
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string MeshFamily { get; init; } = "";
    public string MeshStatus { get; init; } = "";
    public string PreferredArtworkFilename { get; init; } = "";
    public string? SelectedArtworkPath { get; init; }
    public string? SelectedArtworkSource { get; init; }
    public string? SelectedArtworkSha256 { get; init; }
    public string ArtworkStatus { get; init; } = "";
    public string RuntimeStrategy { get; init; } = "";
    public string Notes { get; init; } = "";
    public int SourceCandidateCount { get; init; }
    public List<TokenSourceCandidate> SourceCandidates { get; init; } = [];
}

public sealed class TokenSourceCandidate
{
    public string Source { get; init; } = "";
    public string NameEvidence { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public string Extension { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = "";
}

public sealed class TokenMeshFamily
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public List<string> Consumers { get; init; } = [];
    public string Requirements { get; init; } = "";
}
