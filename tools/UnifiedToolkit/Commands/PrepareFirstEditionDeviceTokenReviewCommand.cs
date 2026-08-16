using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Builds a review-only TTS save from exact Unified 2.5 device-object candidates.
/// No source asset, mapping, Lua script, manifest, or gameplay state is changed.
/// </summary>
public static class PrepareFirstEditionDeviceTokenReviewCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string ClusterMineSideTextureUrl = "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/textures/bombs/clustermine-side.png";
    private const double MillimetresToInGameUnits = 0.03637;

    private const string ClusterMineSideLua = """
checkingRange = nil
scale = 1/self.getScale().x

local removeButtonUp = {
    click_function = 'removeCheckRange',
    label = 'Remove',
    function_owner = self,
    position = {0, 0.1*scale, 0},
    rotation = {0, 0, 0},
    width = 400*scale,
    height = 300*scale,
    font_size = 100*scale,
    color = {0.7, 0.7, 0.7}
}

local removeButtonDown = {
    click_function = 'removeCheckRange',
    label = 'Remove',
    function_owner = self,
    position = {0, -0.1*scale, 0},
    rotation = {180, 0, 0},
    width = 400*scale,
    height = 300*scale,
    font_size = 100*scale,
    color = {0.7, 0.7, 0.7}
}

function removeCheckRange()
  checkRange(nil)
end

function onLoad(save_state)
    self.addContextMenuItem("Check Range 1", function() checkRange(1) end, false)
end

function checkRange(range)
    checkingRange = Global.call("API_CheckObjectRange", {
      owner = self,
      range = range,
      currentRange = checkingRange,
      removeButtonUp = removeButtonUp,
      removeButtonDown = removeButtonDown,
      options = {
        thickness = 0.05*scale
      }
    })
end
""";

    private static readonly DeviceDefinition[] Devices =
    [
        new("seismic-charge", "Seismic Charge", "Seismic Charge"),
        new("proton-bomb", "Proton Bomb", "Proton Bomb"),
        new("ion-bomb", "Ion Bomb", "Ion Bomb"),
        new("thermal-detonator", "Thermal Detonator", "Thermal Detonator"),
        new("bomblet", "Bomblet", "Bomblet"),
        new("proximity-mine", "Proximity Mine", "Proximity Mine"),
        new("cluster-mine", "Cluster Mine", "Cluster Mine (middle)"),
        new("conner-net", "Conner Net", "Connor Net"),
        new("rigged-cargo", "Rigged Cargo", "Loose Cargo")
    ];

    private static readonly HashSet<string> ExcludedStateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Blaze Bomb", "Concussion Bomb", "Electro-Proton Bomb", "Electro-Chaff Cloud", "Spare Parts"
    };

    public static int Run(string[] args)
    {
        Console.WriteLine("UnifiedToolkit Phase 16E-R20 First Edition Device Token Review");
        Console.WriteLine("================================================================");
        Console.WriteLine();

        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var referenceSave = Path.GetFullPath(args[1]);
            var inventoryPath = Path.GetFullPath(Option(args, "--inventory") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "gameplay-object-inventory", "first-edition-gameplay-objects.json"));
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(
                repository, "_unifiedtoolkit_reports", "phase16", "device-token-review"));

            RequireDirectory(repository, "Repository");
            RequireFile(referenceSave, "Unified 2.5 TTS reference save");
            RequireFile(inventoryPath, "First Edition gameplay-object inventory");
            Directory.CreateDirectory(output);

            var requirementIds = ReadRequirementIds(inventoryPath);
            var missingRequirements = Devices
                .Where(device => !requirementIds.Contains(device.RequirementId))
                .Select(device => device.RequirementId)
                .ToList();
            if (missingRequirements.Count > 0)
                throw new InvalidDataException($"Inventory is missing required device definitions: {string.Join(", ", missingRequirements)}.");

            var sourceRoot = JsonNode.Parse(File.ReadAllText(referenceSave))?.AsObject()
                ?? throw new InvalidDataException($"Could not parse TTS reference save: {referenceSave}");
            var sourceObjects = sourceRoot["ObjectStates"]?.AsArray()
                ?? throw new InvalidDataException("TTS reference save has no ObjectStates array.");

            var rows = new List<DeviceReviewRow>();
            foreach (var definition in Devices)
            {
                var match = FindTopLevelObject(sourceObjects, definition.SourceNickname)
                    ?? throw new InvalidDataException($"Reference save does not contain top-level object '{definition.SourceNickname}'.");
                rows.Add(BuildRow(definition, match.Index, match.Object));
            }

            var warnings = BuildWarnings(rows);
            var savePath = Path.Combine(output, "first-edition-device-token-review.json");
            var selectionsPath = Path.Combine(output, "first-edition-device-token-selections.csv");
            var manifestPath = Path.Combine(output, "first-edition-device-token-review-manifest.json");
            var reportPath = Path.Combine(output, "FIRST-EDITION-DEVICE-TOKEN-REVIEW.md");

            var manifest = new DeviceReviewManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                Phase = "16E-R20",
                Policy = "Review only. No device is approved, imported, trimmed, converted, or enabled.",
                RepositoryRoot = NormalisePath(repository),
                InventoryPath = NormalisePath(inventoryPath),
                ReferenceSavePath = NormalisePath(referenceSave),
                ReferenceSaveSha256 = Sha256(referenceSave),
                RequirementCount = Devices.Length,
                ReviewObjectCount = rows.Sum(row => row.PhysicalPieceCount),
                WarningCount = warnings.Count,
                Warnings = warnings,
                Devices = rows
            };

            File.WriteAllText(savePath, BuildSave(sourceRoot, rows).ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            WriteSelections(selectionsPath, rows);
            WriteReport(reportPath, manifest, savePath, selectionsPath, manifestPath);

            Console.WriteLine($"Repository:                    {repository}");
            Console.WriteLine($"Inventory:                     {inventoryPath}");
            Console.WriteLine($"Unified 2.5 reference save:    {referenceSave}");
            Console.WriteLine($"Device requirements:           {manifest.RequirementCount}");
            Console.WriteLine($"Review objects:                {manifest.ReviewObjectCount}");
            Console.WriteLine($"Objects retaining states:      {rows.Count(row => row.SourceStateCount > 0)}");
            Console.WriteLine($"Objects retaining Lua:         {rows.Count(row => row.LuaPresent)}");
            Console.WriteLine($"Warnings:                      {manifest.WarningCount}");
            Console.WriteLine();
            Console.WriteLine($"TTS review save: {savePath}");
            Console.WriteLine($"Selections:      {selectionsPath}");
            Console.WriteLine($"Manifest:        {manifestPath}");
            Console.WriteLine($"Report:          {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Review package prepared. No assets, mappings, Lua scripts or gameplay state were modified.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Device token review preparation failed: {exception.Message}");
            return 1;
        }
    }

    private static HashSet<string> ReadRequirementIds(string inventoryPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(inventoryPath));
        if (!TryProperty(document.RootElement, "requirements", out var requirements) || requirements.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Gameplay-object inventory does not contain a requirements array.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in requirements.EnumerateArray())
        {
            if (TryProperty(requirement, "id", out var id) && id.ValueKind == JsonValueKind.String)
                ids.Add(id.GetString() ?? string.Empty);
        }
        return ids;
    }

    private static ObjectMatch? FindTopLevelObject(JsonArray objects, string nickname)
    {
        for (var index = 0; index < objects.Count; index++)
        {
            if (objects[index] is not JsonObject candidate) continue;
            if (string.Equals(candidate["Nickname"]?.GetValue<string>(), nickname, StringComparison.OrdinalIgnoreCase))
                return new ObjectMatch(index, candidate);
        }
        return null;
    }

    private static DeviceReviewRow BuildRow(DeviceDefinition definition, int sourceIndex, JsonObject source)
    {
        var states = source["States"] as JsonObject;
        var stateNames = states is null
            ? new List<string>()
            : states.Select(pair => pair.Value?["Nickname"]?.GetValue<string>() ?? $"State {pair.Key}").ToList();
        var excludedStates = stateNames.Where(ExcludedStateNames.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var assets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectAssetUrls(source, assets);
        var physicalPieceCount = definition.RequirementId.Equals("cluster-mine", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
        if (physicalPieceCount == 3) assets.Add(ClusterMineSideTextureUrl);
        var serialized = source.ToJsonString();

        return new DeviceReviewRow
        {
            RequirementId = definition.RequirementId,
            CanonicalName = definition.CanonicalName,
            SourcePath = $"ObjectStates[{sourceIndex}]",
            SourceGuid = source["GUID"]?.GetValue<string>() ?? string.Empty,
            SourceNickname = source["Nickname"]?.GetValue<string>() ?? string.Empty,
            SourceObjectType = source["Name"]?.GetValue<string>() ?? string.Empty,
            SourceStateCount = states?.Count ?? 0,
            SourceStateNames = stateNames,
            ExcludedStateNames = excludedStates,
            LuaPresent = !string.IsNullOrWhiteSpace(source["LuaScript"]?.GetValue<string>()),
            PhysicalPieceCount = physicalPieceCount,
            AssetUrls = assets.ToList(),
            ExactObjectSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant()
        };
    }

    private static List<string> BuildWarnings(List<DeviceReviewRow> rows)
    {
        var warnings = new List<string>
        {
            "The five bomb candidates retain Unified 2.5 multi-state menus, including non-First-Edition states. Review the visible selected form only; canonical import must trim excluded states.",
            "The Cluster Mine is a three-piece set. Unified 2.5 stores the middle token and constructs both side tokens at runtime; this review materializes all three pieces directly.",
            "Unified 2.5 calls the source object 'Connor Net'; this review maps it to the official First Edition name 'Conner Net'.",
            "Unified 2.5 calls the source object 'Loose Cargo'; this is only a candidate for the First Edition Rigged Cargo Chute debris token and requires visual approval."
        };
        if (rows.Any(row => row.LuaPresent))
            warnings.Add("Source object Lua is retained for code and context-menu inspection, but the full Unified 2.5 global runtime is intentionally absent; this review does not validate executable device behaviour.");
        return warnings;
    }

    private static JsonObject BuildSave(JsonObject sourceRoot, List<DeviceReviewRow> rows)
    {
        var save = sourceRoot.DeepClone().AsObject();
        var sourceObjects = sourceRoot["ObjectStates"]!.AsArray();
        var reviewObjects = new JsonArray();
        var positions = new (float X, float Z)[]
        {
            (-8, 7), (0, 7), (8, 7),
            (-8, 0), (0, 0), (8, 0),
            (-8, -7), (0, -7), (8, -7)
        };

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var sourceIndex = int.Parse(row.SourcePath["ObjectStates[".Length..^1], CultureInfo.InvariantCulture);
            var clone = sourceObjects[sourceIndex]!.DeepClone().AsObject();
            var position = positions[index];
            clone["GUID"] = $"d2{index + 1:x4}";
            clone["Nickname"] = $"{row.CanonicalName} — Unified 2.5 candidate";
            clone["Description"] = "PHASE 16E-R20 REVIEW ONLY — not approved or canonical.";
            clone["GMNotes"] = JsonSerializer.Serialize(new
            {
                phase = "16E-R20",
                reviewOnly = true,
                row.RequirementId,
                row.CanonicalName,
                row.SourcePath,
                row.SourceGuid,
                row.SourceNickname,
                row.SourceStateCount,
                row.ExcludedStateNames
            });
            clone["Locked"] = false;
            clone["DragSelectable"] = true;
            var transform = clone["Transform"]?.AsObject() ?? new JsonObject();
            transform["posX"] = position.X;
            transform["posY"] = 1.25;
            transform["posZ"] = position.Z;
            transform["rotX"] = 0.0;
            transform["rotZ"] = 0.0;
            clone["Transform"] = transform;
            reviewObjects.Add(clone);

            if (row.RequirementId.Equals("cluster-mine", StringComparison.OrdinalIgnoreCase))
            {
                reviewObjects.Add(BuildClusterMineSide(clone, "d2c001", position.X, position.Z, -1));
                reviewObjects.Add(BuildClusterMineSide(clone, "d2c002", position.X, position.Z, 1));
            }
        }

        save["SaveName"] = "X-Wing Unified 1E - Phase 16E-R20 Device Token Review";
        save["GameMode"] = string.Empty;
        save["Date"] = DateTime.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
        save["Note"] = "Review only. Nine Unified 2.5 device candidates are displayed as eleven physical objects. The complete three-piece Cluster Mine set is materialized from its runtime construction recipe. States and Lua are retained for inspection, not approval.";
        save["Rules"] = string.Empty;
        save["XmlUI"] = string.Empty;
        save["LuaScript"] = string.Empty;
        save["LuaScriptState"] = string.Empty;
        save["ObjectStates"] = reviewObjects;
        return save;
    }

    private static JsonObject BuildClusterMineSide(JsonObject centre, string guid, float centreX, float centreZ, int direction)
    {
        var side = centre.DeepClone().AsObject();
        side["GUID"] = guid;
        side["Name"] = "Custom_Token";
        side["Nickname"] = "Cluster Mine (side) — Unified 2.5 runtime candidate";
        side["Description"] = "PHASE 16E-R20 REVIEW ONLY — dynamically constructed side token.";
        side["GMNotes"] = JsonSerializer.Serialize(new
        {
            phase = "16E-R20",
            reviewOnly = true,
            requirementId = "cluster-mine",
            canonicalName = "Cluster Mine side token",
            source = "Unified 2.5 BombModule.ExpandCluster",
            physicalPiece = direction < 0 ? "left" : "right",
            texture = ClusterMineSideTextureUrl
        });
        side.Remove("States");
        side.Remove("CustomMesh");
        side["CustomImage"] = new JsonObject
        {
            ["ImageURL"] = ClusterMineSideTextureUrl,
            ["ImageSecondaryURL"] = string.Empty,
            ["ImageScalar"] = 1.0,
            ["WidthScale"] = 0.0,
            ["CustomToken"] = new JsonObject
            {
                ["Thickness"] = 0.1,
                ["MergeDistancePixels"] = 5.0,
                ["StandUp"] = false,
                ["Stackable"] = false
            }
        };
        side["LuaScript"] = ClusterMineSideLua;
        side["LuaScriptState"] = string.Empty;
        side["Tags"] = new JsonArray(JsonValue.Create("Mine"));
        side["Locked"] = false;
        side["DragSelectable"] = true;

        var transform = side["Transform"]!.AsObject();
        var rotation = transform["rotY"]?.GetValue<double>() ?? 0.0;
        var radians = rotation * Math.PI / 180.0;
        var localX = direction * 43.5 * MillimetresToInGameUnits;
        var localZ = -1.5 * MillimetresToInGameUnits;
        var rotatedX = localX * Math.Cos(radians) + localZ * Math.Sin(radians);
        var rotatedZ = -localX * Math.Sin(radians) + localZ * Math.Cos(radians);
        transform["posX"] = centreX + rotatedX;
        transform["posY"] = 1.25;
        transform["posZ"] = centreZ + rotatedZ;
        transform["scaleX"] = 0.4554;
        transform["scaleY"] = 0.4554;
        transform["scaleZ"] = 0.4554;
        return side;
    }

    private static void CollectAssetUrls(JsonNode? node, ISet<string> urls)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Value is JsonValue value && pair.Key.EndsWith("URL", StringComparison.OrdinalIgnoreCase) &&
                    value.TryGetValue<string>(out var url) && !string.IsNullOrWhiteSpace(url))
                    urls.Add(url);
                else
                    CollectAssetUrls(pair.Value, urls);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) CollectAssetUrls(item, urls);
        }
    }

    private static void WriteSelections(string path, IEnumerable<DeviceReviewRow> rows)
    {
        var output = new StringBuilder();
        output.AppendLine("RequirementId,CanonicalName,PhysicalPieceCount,SourceGuid,SourcePath,SourceNickname,SourceStateCount,LuaPresent,Decision,Notes");
        foreach (var row in rows)
        {
            output.AppendLine(string.Join(",",
                Csv(row.RequirementId), Csv(row.CanonicalName), row.PhysicalPieceCount.ToString(CultureInfo.InvariantCulture),
                Csv(row.SourceGuid), Csv(row.SourcePath),
                Csv(row.SourceNickname), row.SourceStateCount.ToString(CultureInfo.InvariantCulture),
                row.LuaPresent ? "true" : "false", "", ""));
        }
        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private static void WriteReport(string path, DeviceReviewManifest manifest, string savePath, string selectionsPath, string manifestPath)
    {
        var report = new StringBuilder();
        report.AppendLine("# First Edition Device Token Review");
        report.AppendLine();
        report.AppendLine("> Review only. Nothing in this package is approved, imported, trimmed, converted, or enabled.");
        report.AppendLine();
        report.AppendLine("## Outputs");
        report.AppendLine();
        report.AppendLine($"- TTS review save: `{NormalisePath(savePath)}`");
        report.AppendLine($"- Selections: `{NormalisePath(selectionsPath)}`");
        report.AppendLine($"- Manifest: `{NormalisePath(manifestPath)}`");
        report.AppendLine();
        report.AppendLine("## Review candidates");
        report.AppendLine();
        report.AppendLine("| First Edition requirement | Pieces | Unified 2.5 source | States | Lua | Review note |");
        report.AppendLine("|---|---:|---|---:|:---:|---|");
        foreach (var row in manifest.Devices)
        {
            var note = row.RequirementId switch
            {
                "cluster-mine" => "Complete runtime-built set: centre plus two sides",
                "conner-net" => "Source spelling differs",
                "rigged-cargo" => "Loose Cargo visual candidate",
                _ when row.ExcludedStateNames.Count > 0 => $"Retains excluded states: {string.Join(", ", row.ExcludedStateNames)}",
                _ => "Exact source object retained"
            };
            report.AppendLine($"| {row.CanonicalName} | {row.PhysicalPieceCount} | {row.SourceNickname} (`{row.SourceGuid}`) | {row.SourceStateCount} | {(row.LuaPresent ? "Yes" : "No")} | {note} |");
        }
        report.AppendLine();
        report.AppendLine("## Warnings");
        report.AppendLine();
        foreach (var warning in manifest.Warnings) report.AppendLine($"- {warning}");
        report.AppendLine();
        report.AppendLine("## Review procedure");
        report.AppendLine();
        report.AppendLine("1. Load the generated save in Tabletop Simulator.");
        report.AppendLine("2. Inspect each object's image quality, physical size, silhouette, mesh/collider, flip behaviour, state menu, and retained script metadata. The complete Unified 2.5 global runtime is intentionally not loaded.");
        report.AppendLine("3. Record `approve`, `reject`, or `revise` in the selections CSV. Approval must state whether source states and Lua should be retained, trimmed, or replaced.");
        report.AppendLine("4. For Cluster Mine, inspect the centre and both runtime-defined side tokens as one three-piece set.");
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
    }

    private static string? Option(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string NormalisePath(string path) => path.Replace('\\', '/');
    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void RequireDirectory(string path, string label)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} was not found: {path}");
    }

    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{label} was not found: {path}", path);
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  UnifiedToolkit prepare-first-edition-device-token-review <first-edition-repo-folder> <tts-reference-save.json> [--inventory <file>] [--output <folder>]");
    }

    private sealed record DeviceDefinition(string RequirementId, string CanonicalName, string SourceNickname);
    private sealed record ObjectMatch(int Index, JsonObject Object);

    public sealed class DeviceReviewManifest
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public DateTimeOffset GeneratedUtc { get; set; }
        public string Phase { get; set; } = string.Empty;
        public string Policy { get; set; } = string.Empty;
        public string RepositoryRoot { get; set; } = string.Empty;
        public string InventoryPath { get; set; } = string.Empty;
        public string ReferenceSavePath { get; set; } = string.Empty;
        public string ReferenceSaveSha256 { get; set; } = string.Empty;
        public int RequirementCount { get; set; }
        public int ReviewObjectCount { get; set; }
        public int WarningCount { get; set; }
        public List<string> Warnings { get; set; } = [];
        public List<DeviceReviewRow> Devices { get; set; } = [];
    }

    public sealed class DeviceReviewRow
    {
        public string RequirementId { get; set; } = string.Empty;
        public string CanonicalName { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string SourceGuid { get; set; } = string.Empty;
        public string SourceNickname { get; set; } = string.Empty;
        public string SourceObjectType { get; set; } = string.Empty;
        public int SourceStateCount { get; set; }
        public List<string> SourceStateNames { get; set; } = [];
        public List<string> ExcludedStateNames { get; set; } = [];
        public bool LuaPresent { get; set; }
        public int PhysicalPieceCount { get; set; } = 1;
        public List<string> AssetUrls { get; set; } = [];
        public string ExactObjectSha256 { get; set; } = string.Empty;
    }
}
