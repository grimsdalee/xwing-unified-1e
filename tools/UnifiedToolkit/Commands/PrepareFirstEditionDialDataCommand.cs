using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

public static class PrepareFirstEditionDialDataCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  prepare-first-edition-dial-data <first-edition-repo-folder> <xwing-data-folder> [mapping-folder] [--output <folder>]");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var xwingDataRoot = Path.GetFullPath(args[1]);
            var mappingFolder = ResolveMappingFolder(args, repositoryRoot);
            var outputFolder = ResolveOutputFolder(args, repositoryRoot);

            ValidateDirectory(repositoryRoot, "First Edition repository");
            ValidateDirectory(xwingDataRoot, "xwing-data repository");
            ValidateDirectory(mappingFolder, "First Edition mapping folder");

            var mappingFile = Path.Combine(mappingFolder, "ships.json");
            if (!File.Exists(mappingFile))
                throw new FileNotFoundException("First Edition ship mapping file was not found.", mappingFile);

            var sourceFile = FindShipsDataFile(xwingDataRoot);
            var mappings = ReadMappings(mappingFile);
            var sourceShips = ReadSourceShips(sourceFile);
            var sourceIndex = BuildSourceIndex(sourceShips);

            var results = mappings
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.TargetId, StringComparer.OrdinalIgnoreCase)
                .Select(mapping => Match(mapping, sourceIndex))
                .ToList();

            Directory.CreateDirectory(outputFolder);
            var manifestPath = Path.Combine(outputFolder, "first-edition-dial-data.json");
            var csvPath = Path.Combine(outputFolder, "first-edition-dial-data.csv");
            var reportPath = Path.Combine(outputFolder, "FIRST-EDITION-DIAL-DATA-REPORT.md");

            var manifest = new DialDataManifest
            {
                GeneratedUtc = DateTime.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                XWingDataRoot = NormalisePath(xwingDataRoot),
                MappingFile = NormalisePath(mappingFile),
                SourceFile = NormalisePath(sourceFile),
                SemanticShips = mappings.Count,
                SourceShips = sourceShips.Count,
                MatchedShips = results.Count(x => x.Status == "Matched"),
                MissingShips = results.Count(x => x.Status == "Missing"),
                ShipsWithoutDial = results.Count(x => x.Status == "NoDial"),
                Records = results
            };

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            WriteCsv(csvPath, results);
            WriteMarkdown(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit Phase 11B-3 First Edition Dial Data Preparation");
            Console.WriteLine("==============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:       {repositoryRoot}");
            Console.WriteLine($"xwing-data:       {xwingDataRoot}");
            Console.WriteLine($"Mapping folder:   {mappingFolder}");
            Console.WriteLine($"Source file:      {sourceFile}");
            Console.WriteLine();
            Console.WriteLine($"Semantic ships:          {mappings.Count}");
            Console.WriteLine($"Source ship records:     {sourceShips.Count}");
            Console.WriteLine($"Matched with dial data:  {manifest.MatchedShips}");
            Console.WriteLine($"Matched without a dial:  {manifest.ShipsWithoutDial}");
            Console.WriteLine($"Missing source matches:  {manifest.MissingShips}");
            Console.WriteLine();
            Console.WriteLine($"Manifest: {manifestPath}");
            Console.WriteLine($"CSV:      {csvPath}");
            Console.WriteLine($"Report:   {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Dial source data prepared. No semantic mappings or TTS objects were modified.");

            return manifest.MissingShips == 0 && manifest.ShipsWithoutDial == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveMappingFolder(string[] args, string repositoryRoot)
    {
        var positional = args.Skip(2).TakeWhile(x => !x.StartsWith("--", StringComparison.Ordinal)).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(positional))
            return Path.GetFullPath(positional);

        var sourceFolder = Path.Combine(repositoryRoot, "tools", "UnifiedToolkit", "ConversionData", "first-edition");
        if (Directory.Exists(sourceFolder)) return sourceFolder;

        return Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
    }

    private static string ResolveOutputFolder(string[] args, string repositoryRoot)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[i + 1]);
        }

        return Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "phase11b", "first-edition-dial-data");
    }

    private static string FindShipsDataFile(string root)
    {
        var preferred = new[]
        {
            Path.Combine(root, "data", "ships.js"),
            Path.Combine(root, "data", "ships.json"),
            Path.Combine(root, "ships.js"),
            Path.Combine(root, "ships.json")
        };

        var match = preferred.FirstOrDefault(File.Exists);
        if (match is not null) return match;

        match = Directory.EnumerateFiles(root, "ships.*", SearchOption.AllDirectories)
            .Where(x => x.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                        x.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return match ?? throw new FileNotFoundException("Could not locate ships.js or ships.json under the xwing-data folder.");
    }

    private static List<ShipMappingRecord> ReadMappings(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path), JsonDocumentOptions());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("ships.json must contain a JSON array.");

        return document.RootElement.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new ShipMappingRecord
            {
                MappingId = ReadString(x, "mappingId"),
                SourceId = ReadString(x, "sourceId"),
                TargetId = ReadString(x, "targetId"),
                Name = ReadString(x, "name"),
                Size = ReadString(x, "size"),
                Factions = ReadStringArray(x, "factions")
            })
            .Where(x => x.TargetId.Length > 0)
            .ToList();
    }

    private static List<SourceShipRecord> ReadSourceShips(string path)
    {
        var raw = File.ReadAllText(path);
        var json = UnwrapJavaScriptArray(raw);
        using var document = JsonDocument.Parse(json, JsonDocumentOptions());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The xwing-data ship source must contain an array.");

        var output = new List<SourceShipRecord>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = ReadString(item, "name");
            var xws = FirstNonEmpty(ReadString(item, "xws"), ReadString(item, "id"), Normalise(name));
            if (name.Length == 0 || xws.Length == 0) continue;

            output.Add(new SourceShipRecord
            {
                Xws = Normalise(xws),
                Name = name,
                Size = Normalise(ReadString(item, "size")),
                Factions = ReadStringArray(item, "faction", "factions").Select(Normalise).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Actions = ReadStringArray(item, "actions"),
                Dial = ReadStringArray(item, "dial"),
                HasLegacyMatrix = item.TryGetProperty("maneuvers", out var matrix) && matrix.ValueKind == JsonValueKind.Array
            });
        }

        return output;
    }

    private static Dictionary<string, SourceShipRecord> BuildSourceIndex(IEnumerable<SourceShipRecord> ships)
    {
        var index = new Dictionary<string, SourceShipRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in ships)
        {
            AddIndex(index, ship.Xws, ship);
            AddIndex(index, Normalise(ship.Name), ship);
        }
        return index;
    }

    private static void AddIndex(Dictionary<string, SourceShipRecord> index, string key, SourceShipRecord value)
    {
        if (key.Length > 0 && !index.ContainsKey(key)) index[key] = value;
    }

    private static DialDataRecord Match(ShipMappingRecord mapping, IReadOnlyDictionary<string, SourceShipRecord> sourceIndex)
    {
        var keys = new[]
        {
            Normalise(mapping.TargetId),
            Normalise(mapping.SourceId),
            Normalise(mapping.Name)
        }.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var source = keys.Select(key => sourceIndex.TryGetValue(key, out var value) ? value : null).FirstOrDefault(x => x is not null);
        if (source is null)
        {
            return new DialDataRecord
            {
                MappingId = mapping.MappingId,
                ShipId = mapping.TargetId,
                ShipName = mapping.Name,
                Size = mapping.Size,
                Factions = mapping.Factions,
                MatchKeysTried = keys,
                Status = "Missing",
                Notes = "No matching xwing-data ship record was found."
            };
        }

        return new DialDataRecord
        {
            MappingId = mapping.MappingId,
            ShipId = mapping.TargetId,
            ShipName = mapping.Name,
            Size = mapping.Size,
            Factions = mapping.Factions,
            SourceXws = source.Xws,
            SourceName = source.Name,
            SourceSize = source.Size,
            SourceFactions = source.Factions,
            Actions = source.Actions,
            DialCodes = source.Dial,
            HasLegacyManeuverMatrix = source.HasLegacyMatrix,
            MatchKeysTried = keys,
            Status = source.Dial.Count > 0 ? "Matched" : "NoDial",
            Notes = source.Dial.Count > 0 ? "Exact source dial codes captured; runtime conversion is intentionally deferred to the next step." : "Source record matched but contains no dial array."
        };
    }

    private static void WriteCsv(string path, IEnumerable<DialDataRecord> records)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("Status,ShipId,ShipName,Factions,SourceXws,SourceName,DialCount,DialCodes,Actions,HasLegacyManeuverMatrix,Notes");
        foreach (var item in records)
        {
            writer.WriteLine(string.Join(",",
                Csv(item.Status), Csv(item.ShipId), Csv(item.ShipName), Csv(string.Join("|", item.Factions)),
                Csv(item.SourceXws), Csv(item.SourceName), item.DialCodes.Count,
                Csv(string.Join("|", item.DialCodes)), Csv(string.Join("|", item.Actions)),
                item.HasLegacyManeuverMatrix ? "true" : "false", Csv(item.Notes)));
        }
    }

    private static void WriteMarkdown(string path, DialDataManifest manifest)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Phase 11B-3 – First Edition Dial Data Preparation");
        writer.WriteLine();
        writer.WriteLine($"- Semantic ships: **{manifest.SemanticShips}**");
        writer.WriteLine($"- Source ship records: **{manifest.SourceShips}**");
        writer.WriteLine($"- Matched with dial data: **{manifest.MatchedShips}**");
        writer.WriteLine($"- Matched without a dial: **{manifest.ShipsWithoutDial}**");
        writer.WriteLine($"- Missing source matches: **{manifest.MissingShips}**");
        writer.WriteLine();
        writer.WriteLine("This step preserves the original First Edition `dial` codes exactly as supplied by xwing-data. It does not yet translate them into Unified runtime `moveSet` values.");
        writer.WriteLine();
        writer.WriteLine("| Status | Ship | Source | Dial entries | Notes |");
        writer.WriteLine("|---|---|---|---:|---|");
        foreach (var item in manifest.Records)
            writer.WriteLine($"| {Md(item.Status)} | {Md(item.ShipName)} (`{Md(item.ShipId)}`) | {Md(item.SourceName)} (`{Md(item.SourceXws)}`) | {item.DialCodes.Count} | {Md(item.Notes)} |");
    }

    private static JsonDocumentOptions JsonDocumentOptions() => new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static string UnwrapJavaScriptArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start)
            throw new InvalidDataException("Could not locate a JSON array in the xwing-data ships source.");
        return text[start..(end + 1)];
    }

    private static string ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }

    private static List<string> ReadStringArray(JsonElement element, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!element.TryGetProperty(property, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String)
                return [value.GetString() ?? ""];
            if (value.ValueKind == JsonValueKind.Array)
                return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
        }
        return [];
    }

    private static string Normalise(string value) => new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static string Md(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string NormalisePath(string value) => value.Replace('\\', '/');

    private static void ValidateDirectory(string path, string label)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} was not found: {path}");
    }

    private sealed class ShipMappingRecord
    {
        public string MappingId { get; init; } = "";
        public string SourceId { get; init; } = "";
        public string TargetId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Size { get; init; } = "";
        public List<string> Factions { get; init; } = [];
    }

    private sealed class SourceShipRecord
    {
        public string Xws { get; init; } = "";
        public string Name { get; init; } = "";
        public string Size { get; init; } = "";
        public List<string> Factions { get; init; } = [];
        public List<string> Actions { get; init; } = [];
        public List<string> Dial { get; init; } = [];
        public bool HasLegacyMatrix { get; init; }
    }

    private sealed class DialDataManifest
    {
        public DateTime GeneratedUtc { get; init; }
        public string RepositoryRoot { get; init; } = "";
        public string XWingDataRoot { get; init; } = "";
        public string MappingFile { get; init; } = "";
        public string SourceFile { get; init; } = "";
        public int SemanticShips { get; init; }
        public int SourceShips { get; init; }
        public int MatchedShips { get; init; }
        public int MissingShips { get; init; }
        public int ShipsWithoutDial { get; init; }
        public List<DialDataRecord> Records { get; init; } = [];
    }

    private sealed class DialDataRecord
    {
        public string MappingId { get; init; } = "";
        public string ShipId { get; init; } = "";
        public string ShipName { get; init; } = "";
        public string Size { get; init; } = "";
        public List<string> Factions { get; init; } = [];
        public string SourceXws { get; init; } = "";
        public string SourceName { get; init; } = "";
        public string SourceSize { get; init; } = "";
        public List<string> SourceFactions { get; init; } = [];
        public List<string> Actions { get; init; } = [];
        public List<string> DialCodes { get; init; } = [];
        public bool HasLegacyManeuverMatrix { get; init; }
        public List<string> MatchKeysTried { get; init; } = [];
        public string Status { get; init; } = "";
        public string Notes { get; init; } = "";
    }
}
