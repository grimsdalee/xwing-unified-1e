using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Commands;

public static partial class AnalyseShipRuntimeCommand
{
    private const string DefaultOutputFolderName = "ship-runtime-analysis";

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: UnifiedToolkit analyse-ship-runtime <tts-save.json> [--output <folder>]");
            return 1;
        }

        var savePath = Path.GetFullPath(args[0]);
        var outputFolder = ResolveOutputFolder(args, savePath);

        if (!File.Exists(savePath))
        {
            Console.WriteLine($"TTS save not found: {savePath}");
            return 1;
        }

        Console.WriteLine("UnifiedToolkit Phase 11B-2 Ship Runtime Contract Analysis");
        Console.WriteLine("===========================================================");
        Console.WriteLine();
        Console.WriteLine($"TTS save:      {savePath}");
        Console.WriteLine($"Output folder: {outputFolder}");
        Console.WriteLine();

        try
        {
            Directory.CreateDirectory(outputFolder);

            using var document = JsonDocument.Parse(File.ReadAllText(savePath, Encoding.UTF8));
            var objects = EnumerateObjects(document.RootElement);
            var byGuid = objects
                .Where(x => !string.IsNullOrWhiteSpace(x.Guid))
                .GroupBy(x => x.Guid, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var dialLinks = FindDialLinks(objects);
            var linkedShipGuids = dialLinks
                .Select(x => x.AssignedShipGuid)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var candidates = objects
                .Where(x => linkedShipGuids.Contains(x.Guid) || LooksLikeShip(x.Element))
                .GroupBy(x => x.Guid.Length == 0 ? x.Path : x.Guid, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderByDescending(x => linkedShipGuids.Contains(x.Guid))
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ships = new List<ShipRuntimeEntry>();
            var number = 1;
            foreach (var candidate in candidates)
            {
                var entry = AnalyseShip(candidate, number, outputFolder, dialLinks, objects, byGuid);
                if (entry.HasShipData || linkedShipGuids.Contains(candidate.Guid))
                {
                    ships.Add(entry);
                    number++;
                }
            }

            var report = BuildReport(savePath, objects.Count, dialLinks, ships);
            WriteOutputs(outputFolder, report);
            PrintSummary(report, outputFolder);

            return ships.Count == 0 ? 2 : 0;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Invalid TTS JSON: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ship runtime analysis failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveOutputFolder(string[] args, string savePath)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        return Path.Combine(Path.GetDirectoryName(savePath) ?? Directory.GetCurrentDirectory(), DefaultOutputFolderName);
    }

    private static List<ObjectLocation> EnumerateObjects(JsonElement root)
    {
        var result = new List<ObjectLocation>();
        if (!root.TryGetProperty("ObjectStates", out var states) || states.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var index = 0;
        foreach (var item in states.EnumerateArray())
        {
            WalkObject(item, $"ObjectStates[{index}]", null, result);
            index++;
        }

        return result;
    }

    private static void WalkObject(JsonElement element, string path, string? parentGuid, List<ObjectLocation> output)
    {
        var guid = GetString(element, "GUID");
        output.Add(new ObjectLocation(path, guid, parentGuid ?? string.Empty, element.Clone()));

        if (element.TryGetProperty("ContainedObjects", out var contained) && contained.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in contained.EnumerateArray())
            {
                WalkObject(child, $"{path}/ContainedObjects[{index}]", guid, output);
                index++;
            }
        }

        if (element.TryGetProperty("States", out var states) && states.ValueKind == JsonValueKind.Object)
        {
            foreach (var state in states.EnumerateObject())
            {
                if (state.Value.ValueKind == JsonValueKind.Object)
                {
                    WalkObject(state.Value, $"{path}/States[{state.Name}]", guid, output);
                }
            }
        }
    }

    private static List<DialShipLink> FindDialLinks(IEnumerable<ObjectLocation> objects)
    {
        var links = new List<DialShipLink>();
        foreach (var item in objects)
        {
            if (!IsDialObject(item.Element))
            {
                continue;
            }

            var state = ParseJsonObject(GetString(item.Element, "LuaScriptState"));
            var assigned = GetJsonString(state, "assignedShipGUID");
            links.Add(new DialShipLink
            {
                DialGuid = item.Guid,
                DialNickname = GetString(item.Element, "Nickname").Trim(),
                AssignedShipGuid = assigned,
                DialJsonPath = item.Path,
                FactionSkin = GetNestedString(item.Element, "CustomMesh", "DiffuseURL")
            });
        }

        return links.OrderBy(x => x.DialGuid, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsDialObject(JsonElement element)
    {
        var mesh = GetNestedString(element, "CustomMesh", "MeshURL");
        var collider = GetNestedString(element, "CustomMesh", "ColliderURL");
        return mesh.Contains("dialmodel", StringComparison.OrdinalIgnoreCase) ||
               collider.Contains("dialcollider", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeShip(JsonElement element)
    {
        var lua = GetString(element, "LuaScript");
        var state = GetString(element, "LuaScriptState");
        var tags = GetStrings(element, "Tags");

        return tags.Any(x => string.Equals(x, "Ship", StringComparison.OrdinalIgnoreCase)) ||
               lua.Contains("setTable(\"Data\"", StringComparison.OrdinalIgnoreCase) ||
               lua.Contains("setTable('Data'", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("\"shipData\"", StringComparison.OrdinalIgnoreCase);
    }

    private static ShipRuntimeEntry AnalyseShip(
        ObjectLocation location,
        int number,
        string outputFolder,
        IReadOnlyCollection<DialShipLink> dialLinks,
        IReadOnlyCollection<ObjectLocation> allObjects,
        IReadOnlyDictionary<string, ObjectLocation> byGuid)
    {
        var element = location.Element;
        var lua = GetString(element, "LuaScript");
        var luaState = GetString(element, "LuaScriptState");
        var xml = GetString(element, "XmlUI");
        var state = ParseJsonObject(luaState);
        var shipData = GetJsonObject(state, "shipData");
        var uiData = GetJsonObject(state, "uiData");

        var safeName = MakeSafeFileName(GetString(element, "Nickname").Trim());
        if (safeName.Length == 0)
        {
            safeName = $"ship-{number}";
        }

        var prefix = $"{number:00}-{safeName}-{(location.Guid.Length == 0 ? "noguid" : location.Guid)}";
        var luaFile = WriteText(outputFolder, prefix + ".lua", lua);
        var xmlFile = WriteText(outputFolder, prefix + ".xml", xml);
        var stateFile = WriteText(outputFolder, prefix + ".state.json", luaState);
        var shipDataFile = WriteJsonElement(outputFolder, prefix + ".ship-data.json", shipData);

        var referencedGuids = ExtractGuidReferences(string.Join('\n', lua, luaState, xml, element.GetRawText()))
            .Where(x => !string.Equals(x, location.Guid, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var relationships = BuildRelationships(location, referencedGuids, dialLinks, allObjects, byGuid);
        var dataFields = BuildDataFields(shipData);
        var tableReads = TableCallRegex().Matches(lua)
            .Select(x => $"{x.Groups[1].Value}:{x.Groups[2].Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var variableCalls = VariableCallRegex().Matches(lua)
            .Select(x => $"{x.Groups[1].Value}:{x.Groups[2].Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dataFieldUsages = DataFieldRegex().Matches(lua)
            .Select(x => x.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var customMesh = GetObject(element, "CustomMesh");
        var transform = GetObject(element, "Transform");

        return new ShipRuntimeEntry
        {
            Number = number,
            JsonPath = location.Path,
            Guid = location.Guid,
            ParentGuid = location.ParentGuid,
            Name = GetString(element, "Name"),
            Nickname = GetString(element, "Nickname").Trim(),
            Description = GetString(element, "Description"),
            GmNotes = GetString(element, "GMNotes"),
            Tags = GetStrings(element, "Tags"),
            HasShipData = shipData.ValueKind == JsonValueKind.Object,
            Faction = GetJsonString(shipData, "Faction"),
            ShipId = GetJsonString(shipData, "shipId"),
            PilotId = GetJsonString(shipData, "xws"),
            PilotName = GetJsonString(shipData, "name"),
            Size = GetJsonString(shipData, "Size"),
            Initiative = GetJsonNumber(shipData, "initiative"),
            Points = GetJsonNumber(shipData, "points"),
            MoveSet = GetJsonStringArray(shipData, "moveSet"),
            ActionSet = GetJsonStringArray(shipData, "actSet"),
            ExecuteOptions = GetJsonStringArray(shipData, "executeOptions"),
            ArcsJson = GetJsonRaw(shipData, "arcs"),
            ShipDataFields = dataFields,
            UiDataJson = uiData.ValueKind == JsonValueKind.Undefined ? string.Empty : uiData.GetRawText(),
            MeshUrl = GetString(customMesh, "MeshURL"),
            DiffuseUrl = GetString(customMesh, "DiffuseURL"),
            ColliderUrl = GetString(customMesh, "ColliderURL"),
            NormalUrl = GetString(customMesh, "NormalURL"),
            Position = ReadVector(transform, "posX", "posY", "posZ"),
            Rotation = ReadVector(transform, "rotX", "rotY", "rotZ"),
            Scale = ReadVector(transform, "scaleX", "scaleY", "scaleZ"),
            LuaCharacters = lua.Length,
            XmlCharacters = xml.Length,
            LuaStateCharacters = luaState.Length,
            LuaFile = luaFile,
            XmlFile = xmlFile,
            LuaStateFile = stateFile,
            ShipDataFile = shipDataFile,
            TableCalls = tableReads,
            VariableCalls = variableCalls,
            DataFieldUsages = dataFieldUsages,
            ReferencedGuids = referencedGuids,
            Relationships = relationships
        };
    }

    private static List<RuntimeRelationship> BuildRelationships(
        ObjectLocation ship,
        IReadOnlyCollection<string> referencedGuids,
        IReadOnlyCollection<DialShipLink> dialLinks,
        IReadOnlyCollection<ObjectLocation> allObjects,
        IReadOnlyDictionary<string, ObjectLocation> byGuid)
    {
        var result = new List<RuntimeRelationship>();

        foreach (var dial in dialLinks.Where(x => string.Equals(x.AssignedShipGuid, ship.Guid, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new RuntimeRelationship
            {
                Kind = "AssignedDial",
                Guid = dial.DialGuid,
                Name = dial.DialNickname,
                JsonPath = dial.DialJsonPath,
                Evidence = "Dial LuaScriptState.assignedShipGUID"
            });
        }

        foreach (var child in allObjects.Where(x => string.Equals(x.ParentGuid, ship.Guid, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new RuntimeRelationship
            {
                Kind = "ContainedOrStateObject",
                Guid = child.Guid,
                Name = GetString(child.Element, "Nickname").Trim(),
                JsonPath = child.Path,
                Evidence = "TTS object hierarchy"
            });
        }

        foreach (var guid in referencedGuids)
        {
            if (!byGuid.TryGetValue(guid, out var target))
            {
                continue;
            }

            result.Add(new RuntimeRelationship
            {
                Kind = ClassifyRelatedObject(target.Element),
                Guid = guid,
                Name = GetString(target.Element, "Nickname").Trim(),
                JsonPath = target.Path,
                Evidence = "GUID referenced by ship JSON/Lua/state"
            });
        }

        return result
            .GroupBy(x => $"{x.Kind}|{x.Guid}|{x.JsonPath}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Guid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ClassifyRelatedObject(JsonElement element)
    {
        var nickname = GetString(element, "Nickname");
        var name = GetString(element, "Name");
        var text = nickname + " " + name + " " + GetString(element, "Description");

        if (IsDialObject(element)) return "Dial";
        if (text.Contains("pilot", StringComparison.OrdinalIgnoreCase) && text.Contains("card", StringComparison.OrdinalIgnoreCase)) return "PilotCard";
        if (text.Contains("token", StringComparison.OrdinalIgnoreCase)) return "Token";
        if (text.Contains("peg", StringComparison.OrdinalIgnoreCase)) return "Peg";
        if (text.Contains("base", StringComparison.OrdinalIgnoreCase)) return "Base";
        return "ReferencedObject";
    }

    private static List<RuntimeDataField> BuildDataFields(JsonElement shipData)
    {
        var result = new List<RuntimeDataField>();
        if (shipData.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in shipData.EnumerateObject())
        {
            var classification = ClassifyField(property.Name);
            result.Add(new RuntimeDataField
            {
                Name = property.Name,
                JsonType = property.Value.ValueKind.ToString(),
                ValuePreview = Preview(property.Value),
                ContractGroup = classification.Group,
                FirstEditionDisposition = classification.Disposition,
                Rationale = classification.Rationale
            });
        }

        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (string Group, string Disposition, string Rationale) ClassifyField(string field)
    {
        return field.ToLowerInvariant() switch
        {
            "moveset" => ("Dial", "Required", "First Edition manoeuvre dial data supplied by the semantic repository."),
            "actset" => ("Dial", "Required", "First Edition action bar data controls visible dial action buttons."),
            "faction" => ("Dial", "Required", "Selects faction presentation and runtime behaviour."),
            "arcs" => ("Movement/Combat", "Required", "Used by the existing ship and movement runtime."),
            "executeoptions" => ("Movement", "PreserveWhenUsed", "Keep existing execution options only where the First Edition ship requires them."),
            "shipid" or "xws" or "name" => ("Identity", "Required", "Stable ship and pilot identity for runtime linking and display."),
            "size" => ("Physical", "Required", "Must use First Edition small, large or epic base size only."),
            "mesh" or "texture" or "textures" or "config" or "mountingpoints" => ("Presentation", "RequiredOrPreserve", "Required to construct and customise the physical ship object."),
            "initiative" => ("SecondEdition", "Replace", "Replace with the appropriate First Edition pilot-skill representation."),
            "points" or "half_points" => ("SecondEdition", "DoNotCopyBlindly", "2.5 squad-cost fields are not authoritative for First Edition."),
            "limited" => ("Identity", "Review", "May map to First Edition uniqueness, but semantics differ."),
            "colorid" or "proximityhider" or "movethrough" => ("Runtime", "PreserveDefault", "Runtime support field; retain a safe default unless analysis proves it unnecessary."),
            _ => ("Unclassified", "Review", "Field requires explicit review before Phase 12 generation.")
        };
    }

    private static ShipRuntimeReport BuildReport(
        string savePath,
        int objectsInspected,
        IReadOnlyCollection<DialShipLink> dialLinks,
        IReadOnlyCollection<ShipRuntimeEntry> ships)
    {
        var fieldNames = ships.SelectMany(x => x.ShipDataFields.Select(y => y.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ShipRuntimeReport
        {
            SchemaVersion = "1.0.0",
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceSave = savePath.Replace('\\', '/'),
            ObjectsInspected = objectsInspected,
            DialObjectsFound = dialLinks.Count,
            AssignedDialLinks = dialLinks.Count(x => !string.IsNullOrWhiteSpace(x.AssignedShipGuid)),
            ShipRuntimeObjectsFound = ships.Count,
            ShipsWithShipData = ships.Count(x => x.HasShipData),
            UniqueShipDataFields = fieldNames,
            DialLinks = dialLinks.ToList(),
            Ships = ships.ToList()
        };
    }

    private static void WriteOutputs(string outputFolder, ShipRuntimeReport report)
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(
            Path.Combine(outputFolder, "ship-runtime-analysis.json"),
            JsonSerializer.Serialize(report, jsonOptions),
            new UTF8Encoding(false));

        WriteShipCsv(Path.Combine(outputFolder, "ship-runtime-objects.csv"), report.Ships);
        WriteFieldCsv(Path.Combine(outputFolder, "ship-runtime-fields.csv"), report.Ships);
        WriteRelationshipCsv(Path.Combine(outputFolder, "ship-runtime-relationships.csv"), report.Ships);
        WriteMarkdown(Path.Combine(outputFolder, "SHIP-RUNTIME-ANALYSIS-REPORT.md"), report);
    }

    private static void WriteShipCsv(string path, IEnumerable<ShipRuntimeEntry> ships)
    {
        var lines = new List<string>
        {
            "Number,GUID,Nickname,Faction,ShipId,PilotId,PilotName,Size,Initiative,MoveCount,ActionCount,DialCount,HasShipData,JsonPath"
        };

        lines.AddRange(ships.Select(x => string.Join(',', new[]
        {
            Csv(x.Number.ToString()), Csv(x.Guid), Csv(x.Nickname), Csv(x.Faction), Csv(x.ShipId), Csv(x.PilotId),
            Csv(x.PilotName), Csv(x.Size), Csv(x.Initiative), Csv(x.MoveSet.Count.ToString()),
            Csv(x.ActionSet.Count.ToString()), Csv(x.Relationships.Count(y => y.Kind == "AssignedDial").ToString()),
            Csv(x.HasShipData.ToString()), Csv(x.JsonPath)
        })));

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteFieldCsv(string path, IEnumerable<ShipRuntimeEntry> ships)
    {
        var lines = new List<string> { "ShipGUID,ShipId,PilotId,Field,JsonType,ContractGroup,FirstEditionDisposition,ValuePreview,Rationale" };
        foreach (var ship in ships)
        {
            lines.AddRange(ship.ShipDataFields.Select(field => string.Join(',', new[]
            {
                Csv(ship.Guid), Csv(ship.ShipId), Csv(ship.PilotId), Csv(field.Name), Csv(field.JsonType),
                Csv(field.ContractGroup), Csv(field.FirstEditionDisposition), Csv(field.ValuePreview), Csv(field.Rationale)
            })));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteRelationshipCsv(string path, IEnumerable<ShipRuntimeEntry> ships)
    {
        var lines = new List<string> { "ShipGUID,ShipId,PilotId,Kind,RelatedGUID,RelatedName,Evidence,JsonPath" };
        foreach (var ship in ships)
        {
            lines.AddRange(ship.Relationships.Select(link => string.Join(',', new[]
            {
                Csv(ship.Guid), Csv(ship.ShipId), Csv(ship.PilotId), Csv(link.Kind), Csv(link.Guid),
                Csv(link.Name), Csv(link.Evidence), Csv(link.JsonPath)
            })));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteMarkdown(string path, ShipRuntimeReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase 11B-2 – Ship Runtime Contract Analysis");
        sb.AppendLine();
        sb.AppendLine($"- Source save: `{report.SourceSave}`");
        sb.AppendLine($"- Objects inspected: **{report.ObjectsInspected}**");
        sb.AppendLine($"- Dial objects found: **{report.DialObjectsFound}**");
        sb.AppendLine($"- Assigned dial links: **{report.AssignedDialLinks}**");
        sb.AppendLine($"- Ship runtime objects found: **{report.ShipRuntimeObjectsFound}**");
        sb.AppendLine($"- Ships with persisted `shipData`: **{report.ShipsWithShipData}**");
        sb.AppendLine();
        sb.AppendLine("## Runtime contract summary");
        sb.AppendLine();
        sb.AppendLine("The existing dial obtains its contract from the assigned ship's `Data` table. In a saved TTS game, the final table is persisted under `LuaScriptState.shipData`. This report extracts that final runtime value rather than attempting to infer it from bundled Lua source.");
        sb.AppendLine();

        foreach (var ship in report.Ships)
        {
            sb.AppendLine($"## {ship.Number:00}. {EscapeMarkdown(ship.PilotName.Length > 0 ? ship.PilotName : ship.Nickname)}");
            sb.AppendLine();
            sb.AppendLine($"- GUID: `{ship.Guid}`");
            sb.AppendLine($"- Faction / ship / pilot: `{ship.Faction}` / `{ship.ShipId}` / `{ship.PilotId}`");
            sb.AppendLine($"- Size: `{ship.Size}`");
            sb.AppendLine($"- Manoeuvres ({ship.MoveSet.Count}): `{string.Join("`, `", ship.MoveSet)}`");
            sb.AppendLine($"- Actions ({ship.ActionSet.Count}): `{string.Join("`, `", ship.ActionSet)}`");
            sb.AppendLine($"- Assigned dials: **{ship.Relationships.Count(x => x.Kind == "AssignedDial")}**");
            sb.AppendLine($"- Extracted data: `{ship.ShipDataFile}`");
            sb.AppendLine();
            sb.AppendLine("| Field | Group | 1E disposition | Preview |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var field in ship.ShipDataFields)
            {
                sb.AppendLine($"| `{field.Name}` | {field.ContractGroup} | {field.FirstEditionDisposition} | {EscapeMarkdown(field.ValuePreview)} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Generated files");
        sb.AppendLine();
        sb.AppendLine("- `ship-runtime-analysis.json` – complete machine-readable analysis");
        sb.AppendLine("- `ship-runtime-objects.csv` – one row per analysed ship");
        sb.AppendLine("- `ship-runtime-fields.csv` – field-by-field First Edition compatibility inventory");
        sb.AppendLine("- `ship-runtime-relationships.csv` – dial and object relationship evidence");
        sb.AppendLine("- Per-ship `.lua`, `.xml`, `.state.json`, and `.ship-data.json` extracts");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void PrintSummary(ShipRuntimeReport report, string outputFolder)
    {
        Console.WriteLine($"Objects inspected:           {report.ObjectsInspected}");
        Console.WriteLine($"Dial objects found:          {report.DialObjectsFound}");
        Console.WriteLine($"Assigned dial links:         {report.AssignedDialLinks}");
        Console.WriteLine($"Ship runtime objects found:  {report.ShipRuntimeObjectsFound}");
        Console.WriteLine($"Ships with runtime data:     {report.ShipsWithShipData}");
        Console.WriteLine($"Unique ship-data fields:     {report.UniqueShipDataFields.Count}");
        Console.WriteLine();
        Console.WriteLine($"Report:        {Path.Combine(outputFolder, "SHIP-RUNTIME-ANALYSIS-REPORT.md")}");
        Console.WriteLine($"Manifest:      {Path.Combine(outputFolder, "ship-runtime-analysis.json")}");
        Console.WriteLine($"Ships:         {Path.Combine(outputFolder, "ship-runtime-objects.csv")}");
        Console.WriteLine($"Fields:        {Path.Combine(outputFolder, "ship-runtime-fields.csv")}");
        Console.WriteLine($"Relationships: {Path.Combine(outputFolder, "ship-runtime-relationships.csv")}");
        Console.WriteLine();
        Console.WriteLine("Ship runtime contract analysis completed. No TTS objects were modified.");
    }

    private static string WriteText(string folder, string fileName, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        File.WriteAllText(Path.Combine(folder, fileName), text, new UTF8Encoding(false));
        return fileName;
    }

    private static string WriteJsonElement(string folder, string fileName, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return string.Empty;
        using var document = JsonDocument.Parse(value.GetRawText());
        var formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(folder, fileName), formatted, new UTF8Encoding(false));
        return fileName;
    }

    private static JsonElement ParseJsonObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static JsonElement GetJsonObject(JsonElement parent, string property)
    {
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value.Clone()
            : default;
    }

    private static JsonElement GetObject(JsonElement parent, string property)
    {
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
    }

    private static string GetString(JsonElement parent, string property)
    {
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string GetNestedString(JsonElement parent, string objectName, string property)
    {
        var nested = GetObject(parent, objectName);
        return GetString(nested, property);
    }

    private static string GetJsonString(JsonElement parent, string property)
    {
        return GetString(parent, property);
    }

    private static string GetJsonNumber(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static string GetJsonRaw(JsonElement parent, string property)
    {
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) ? value.GetRawText() : string.Empty;
    }

    private static List<string> GetJsonStringArray(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? string.Empty)
            .Where(x => x.Length > 0)
            .ToList();
    }

    private static List<string> GetStrings(JsonElement parent, string property)
    {
        return GetJsonStringArray(parent, property);
    }

    private static string ReadVector(JsonElement transform, string xName, string yName, string zName)
    {
        if (transform.ValueKind != JsonValueKind.Object) return string.Empty;
        return $"{ReadNumber(transform, xName)},{ReadNumber(transform, yName)},{ReadNumber(transform, zName)}";
    }

    private static string ReadNumber(JsonElement parent, string property)
    {
        return parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : string.Empty;
    }

    private static List<string> ExtractGuidReferences(string text)
    {
        return GuidRegex().Matches(text).Select(x => x.Value).ToList();
    }

    private static string Preview(JsonElement value)
    {
        var raw = value.GetRawText().Replace("\r", " ").Replace("\n", " ");
        return raw.Length <= 160 ? raw : raw[..157] + "...";
    }

    private static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static string EscapeMarkdown(string value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(x => invalid.Contains(x) ? '-' : x).ToArray();
        return Regex.Replace(new string(chars), @"\s+", "-").Trim('-', '.', ' ');
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{6}\b", RegexOptions.CultureInvariant)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"\b(getTable|setTable)\s*\(\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TableCallRegex();

    [GeneratedRegex(@"\b(getVar|setVar)\s*\(\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VariableCallRegex();

    [GeneratedRegex(@"\b(?:Data|data|shipData|ship_data)\s*(?:\.|\[\s*['""])([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex DataFieldRegex();

    private sealed record ObjectLocation(string Path, string Guid, string ParentGuid, JsonElement Element);

    public sealed class ShipRuntimeReport
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public DateTimeOffset GeneratedUtc { get; init; }
        public string SourceSave { get; init; } = string.Empty;
        public int ObjectsInspected { get; init; }
        public int DialObjectsFound { get; init; }
        public int AssignedDialLinks { get; init; }
        public int ShipRuntimeObjectsFound { get; init; }
        public int ShipsWithShipData { get; init; }
        public List<string> UniqueShipDataFields { get; init; } = new();
        public List<DialShipLink> DialLinks { get; init; } = new();
        public List<ShipRuntimeEntry> Ships { get; init; } = new();
    }

    public sealed class DialShipLink
    {
        public string DialGuid { get; init; } = string.Empty;
        public string DialNickname { get; init; } = string.Empty;
        public string AssignedShipGuid { get; init; } = string.Empty;
        public string DialJsonPath { get; init; } = string.Empty;
        public string FactionSkin { get; init; } = string.Empty;
    }

    public sealed class ShipRuntimeEntry
    {
        public int Number { get; init; }
        public string JsonPath { get; init; } = string.Empty;
        public string Guid { get; init; } = string.Empty;
        public string ParentGuid { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Nickname { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string GmNotes { get; init; } = string.Empty;
        public List<string> Tags { get; init; } = new();
        public bool HasShipData { get; init; }
        public string Faction { get; init; } = string.Empty;
        public string ShipId { get; init; } = string.Empty;
        public string PilotId { get; init; } = string.Empty;
        public string PilotName { get; init; } = string.Empty;
        public string Size { get; init; } = string.Empty;
        public string Initiative { get; init; } = string.Empty;
        public string Points { get; init; } = string.Empty;
        public List<string> MoveSet { get; init; } = new();
        public List<string> ActionSet { get; init; } = new();
        public List<string> ExecuteOptions { get; init; } = new();
        public string ArcsJson { get; init; } = string.Empty;
        public List<RuntimeDataField> ShipDataFields { get; init; } = new();
        public string UiDataJson { get; init; } = string.Empty;
        public string MeshUrl { get; init; } = string.Empty;
        public string DiffuseUrl { get; init; } = string.Empty;
        public string ColliderUrl { get; init; } = string.Empty;
        public string NormalUrl { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public string Rotation { get; init; } = string.Empty;
        public string Scale { get; init; } = string.Empty;
        public int LuaCharacters { get; init; }
        public int XmlCharacters { get; init; }
        public int LuaStateCharacters { get; init; }
        public string LuaFile { get; init; } = string.Empty;
        public string XmlFile { get; init; } = string.Empty;
        public string LuaStateFile { get; init; } = string.Empty;
        public string ShipDataFile { get; init; } = string.Empty;
        public List<string> TableCalls { get; init; } = new();
        public List<string> VariableCalls { get; init; } = new();
        public List<string> DataFieldUsages { get; init; } = new();
        public List<string> ReferencedGuids { get; init; } = new();
        public List<RuntimeRelationship> Relationships { get; init; } = new();
    }

    public sealed class RuntimeDataField
    {
        public string Name { get; init; } = string.Empty;
        public string JsonType { get; init; } = string.Empty;
        public string ValuePreview { get; init; } = string.Empty;
        public string ContractGroup { get; init; } = string.Empty;
        public string FirstEditionDisposition { get; init; } = string.Empty;
        public string Rationale { get; init; } = string.Empty;
    }

    public sealed class RuntimeRelationship
    {
        public string Kind { get; init; } = string.Empty;
        public string Guid { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string JsonPath { get; init; } = string.Empty;
        public string Evidence { get; init; } = string.Empty;
    }
}
