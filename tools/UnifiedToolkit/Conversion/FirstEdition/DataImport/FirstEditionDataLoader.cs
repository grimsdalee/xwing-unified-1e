using System.Text.Json;

namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public static class FirstEditionDataLoader
{
    public static IReadOnlyList<FirstEditionDataShip> LoadShips(string dataRoot) => LoadShipCatalogue(dataRoot).Ships;

    public static FirstEditionDataLoadResult LoadShipCatalogue(string dataRoot)
    {
        var fullRoot = ValidateRoot(dataRoot);
        var shipsFile = FindSingleFile(fullRoot, "ships");
        var ships = ReadShipsFile(shipsFile);
        return new FirstEditionDataLoadResult
        {
            Ships = ships.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList(),
            SourceFiles = [new FirstEditionDataSourceFile { Path = shipsFile, DataType = "Ship chassis", RecordsRead = ships.Count, Notes = "Standard and single-record huge ship chassis from xwing-data." }]
        };
    }

    public static IReadOnlyList<FirstEditionDataPilot> LoadPilots(string dataRoot)
    {
        var fullRoot = ValidateRoot(dataRoot);
        var dataFolder = Directory.Exists(Path.Combine(fullRoot, "data")) ? Path.Combine(fullRoot, "data") : fullRoot;
        var files = Directory.EnumerateFiles(dataFolder, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetFileName(path).Contains("pilot", StringComparison.OrdinalIgnoreCase) || path.Split(Path.DirectorySeparatorChar).Any(p => p.Equals("pilots", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0) throw new FileNotFoundException("Could not find First Edition pilot data files.");

        var pilots = new List<FirstEditionDataPilot>();
        foreach (var file in files) ReadPilotFile(file, pilots);
        return pilots
            .GroupBy(x => $"{x.Id}|{x.ShipId}|{x.Faction}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ShipId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ReadPilotFile(string file, List<FirstEditionDataPilot> output)
    {
        var text = UnwrapJavaScriptAssignment(File.ReadAllText(file).Trim());
        JsonDocument document;
        try { document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException) { return; }
        using (document)
        {
            IEnumerable<JsonElement> records = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().ToList(),
                JsonValueKind.Object => document.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array).SelectMany(p => p.Value.EnumerateArray()).ToList(),
                _ => []
            };
            foreach (var e in records)
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var name = ReadString(e, "name");
                var ship = FirstNonEmpty(ReadString(e, "ship"), ReadString(e, "shipId"));
                var id = FirstNonEmpty(ReadString(e, "xws"), ReadString(e, "id"), NormaliseId(name));
                if (name.Length == 0 || ship.Length == 0) continue;
                output.Add(new FirstEditionDataPilot
                {
                    Id = id, Name = name, ShipId = NormaliseId(ship), Faction = NormaliseToken(ReadString(e, "faction")),
                    PilotSkill = ReadInt(e, "skill"), SquadPointCost = ReadInt(e, "points"),
                    Unique = ReadBoolean(e, "unique") || ReadInt(e, "unique") > 0,
                    UpgradeSlots = ReadStringList(e, "slots", "upgrades"), SourceFile = file
                });
            }
        }
    }

    private static string ValidateRoot(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var full = Path.GetFullPath(dataRoot);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"First Edition data folder not found: {full}");
        return full;
    }

    private static string FindSingleFile(string root, string stem)
    {
        var candidates = new[] { Path.Combine(root, "data", stem + ".js"), Path.Combine(root, "data", stem + ".json"), Path.Combine(root, stem + ".js"), Path.Combine(root, stem + ".json") };
        return candidates.FirstOrDefault(File.Exists) ?? Directory.EnumerateFiles(root, stem + ".*", SearchOption.AllDirectories).First(path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    private static List<FirstEditionDataShip> ReadShipsFile(string file)
    {
        var text = UnwrapJavaScriptAssignment(File.ReadAllText(file).Trim());
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var ships = new List<FirstEditionDataShip>();
        foreach (var e in document.RootElement.EnumerateArray())
        {
            var name = ReadString(e, "name"); var id = FirstNonEmpty(ReadString(e, "xws"), ReadString(e, "id"), NormaliseId(name));
            if (name.Length == 0 || id.Length == 0) continue;
            ships.Add(new FirstEditionDataShip { Id = id, Name = name, Size = NormaliseSize(ReadString(e, "size"), ReadBoolean(e, "large")), Attack = ReadInt(e, "attack"), Agility = ReadInt(e, "agility"), Hull = ReadInt(e, "hull"), Shields = ReadInt(e, "shields"), Actions = ReadStringList(e, "actions"), Factions = ReadStringList(e, "faction", "factions"), SourceFile = file });
        }
        return ships;
    }

    private static string UnwrapJavaScriptAssignment(string text) { var a = text.IndexOf('['); var b = text.LastIndexOf(']'); return a >= 0 && b > a ? text[a..(b + 1)] : text; }
    private static string ReadString(JsonElement e, string p) { if (!e.TryGetProperty(p, out var v)) return ""; return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : ""; }
    private static int ReadInt(JsonElement e, string p) { if (!e.TryGetProperty(p, out var v)) return 0; return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n) ? n : 0; }
    private static bool ReadBoolean(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False && v.GetBoolean();
    private static List<string> ReadStringList(JsonElement e, params string[] ps) { foreach (var p in ps) { if (!e.TryGetProperty(p, out var v)) continue; if (v.ValueKind == JsonValueKind.String) return [NormaliseToken(v.GetString() ?? "")]; if (v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => NormaliseToken(x.GetString() ?? "")).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); } return []; }
    private static string NormaliseSize(string s, bool large) => s.Length > 0 ? NormaliseToken(s) : large ? "large" : "small";
    private static string NormaliseToken(string v) => new(v.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    public static string NormaliseId(string v) => NormaliseToken(v);
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

    public static IReadOnlyList<FirstEditionDataUpgrade> LoadUpgrades(string dataRoot)
    {
        var fullRoot = ValidateRoot(dataRoot);
        var dataFolder = Directory.Exists(Path.Combine(fullRoot, "data")) ? Path.Combine(fullRoot, "data") : fullRoot;
        var files = Directory.EnumerateFiles(dataFolder, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetFileName(path).Contains("upgrade", StringComparison.OrdinalIgnoreCase) || path.Split(Path.DirectorySeparatorChar).Any(p => p.Equals("upgrades", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) throw new FileNotFoundException("Could not find First Edition upgrade data files.");

        var upgrades = new List<FirstEditionDataUpgrade>();
        foreach (var file in files) ReadUpgradeFile(file, upgrades);
        return upgrades.GroupBy(x => $"{x.Id}|{x.Slot}", StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Slot, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ReadUpgradeFile(string file, List<FirstEditionDataUpgrade> output)
    {
        var text = UnwrapJavaScriptAssignment(File.ReadAllText(file).Trim());
        JsonDocument document;
        try { document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException) { return; }
        using (document)
        {
            IEnumerable<JsonElement> records = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().ToList(),
                JsonValueKind.Object => document.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array).SelectMany(p => p.Value.EnumerateArray()).ToList(),
                _ => []
            };
            foreach (var e in records)
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var name = ReadString(e, "name");
                var slot = FirstNonEmpty(ReadString(e, "slot"), ReadString(e, "type"));
                var id = FirstNonEmpty(ReadString(e, "xws"), ReadString(e, "id"), NormaliseId(name));
                if (name.Length == 0 || slot.Length == 0) continue;
                output.Add(new FirstEditionDataUpgrade
                {
                    Id = id, Name = name, Slot = NormaliseToken(slot),
                    SquadPointCost = ReadInt(e, "points"), Unique = ReadBoolean(e, "unique") || ReadInt(e, "unique") > 0,
                    Factions = ReadStringList(e, "faction", "factions"), ShipRestrictions = ReadStringList(e, "ship", "ships"),
                    SizeRestrictions = ReadStringList(e, "size", "sizes"), Text = FirstNonEmpty(ReadString(e, "text"), ReadString(e, "description")),
                    SourceFile = file
                });
            }
        }
    }

}
