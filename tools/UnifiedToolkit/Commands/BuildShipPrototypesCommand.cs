using System.Text.Json;
using System.Text.Json.Nodes;
using UnifiedToolkit.Hybrid;
using UnifiedToolkit.Models;
using UnifiedToolkit.TTS;

namespace UnifiedToolkit.Commands;

public static class BuildShipPrototypesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: build-ship-prototypes <hybrid-ship-definitions.json> <unified-2.5-save.json> [--output <folder>]");
            return 2;
        }

        try
        {
            var hybridPath = Path.GetFullPath(args[0]);
            var unifiedSavePath = Path.GetFullPath(args[1]);
            var outputFolder = ResolveOutputFolder(args, Path.Combine(Path.GetDirectoryName(hybridPath)!, "generated-prototypes"));

            if (!File.Exists(hybridPath))
                throw new FileNotFoundException("Hybrid ship definition file not found.", hybridPath);
            if (!File.Exists(unifiedSavePath))
                throw new FileNotFoundException("Unified 2.5 save file not found.", unifiedSavePath);

            Directory.CreateDirectory(outputFolder);

            var hybrid = JsonSerializer.Deserialize<HybridShipDefinitionDocument>(File.ReadAllText(hybridPath), JsonOptions)
                ?? throw new InvalidDataException("Could not read hybrid ship definitions.");
            var unifiedSaveText = File.ReadAllText(unifiedSavePath);
            var unifiedSaveRoot = JsonNode.Parse(unifiedSaveText)?.AsObject()
                ?? throw new InvalidDataException("Could not parse the Unified 2.5 save root.");
            var unified = TtsSaveLoader.Load(unifiedSavePath);
            var compositeBase = FindRecursive(unified.Objects, "8c3322")
                ?? throw new InvalidDataException("Unified CompositeBase GUID 8c3322 was not found.");

            var requests = new[]
            {
                new PrototypeRequest("xwing", "Cavern Angels Zealot", -5f),
                new PrototypeRequest("arc170", "Norra Wexley", 5f)
            };

            var prototypes = new List<ShipPrototypeObject>();
            var results = new List<ShipPrototypeBuildResult>();

            foreach (var request in requests)
            {
                var ship = hybrid.Ships.FirstOrDefault(x => x.SemanticData.Id.Equals(request.ShipId, StringComparison.OrdinalIgnoreCase));
                if (ship is null)
                {
                    results.Add(Failed(request.ShipId, "The requested ship is absent from the hybrid repository."));
                    continue;
                }

                var appearance = SelectAppearance(ship, request.PreferredAppearance);
                if (appearance is null)
                {
                    results.Add(Failed(request.ShipId, "No appearance is available."));
                    continue;
                }

                var fileName = $"{Safe(ship.SemanticData.Id)}-{Safe(appearance.DisplayName)}.json";
                var outputPath = Path.Combine(outputFolder, fileName);
                var prototype = PrototypeShipObjectBuilder.Build(ship, appearance, compositeBase, outputPath, request.PositionX);
                File.WriteAllText(outputPath, prototype.ObjectJson.ToJsonString(JsonOptions));
                prototypes.Add(prototype);
                results.Add(prototype.Result);
            }

            var savePath = Path.Combine(outputFolder, "XWing-1E-Phase5B-R3-Static-Prototype-Save.json");
            var save = PrototypeShipObjectBuilder.BuildPrototypeSave(prototypes, unifiedSaveRoot);
            File.WriteAllText(savePath, save.ToJsonString(JsonOptions));

            var document = new ShipPrototypeBuildDocument
            {
                HybridDefinitionPath = hybridPath,
                UnifiedSavePath = unifiedSavePath,
                Prototypes = results,
                Summary = new ShipPrototypeBuildSummary
                {
                    RequestedShipCount = requests.Length,
                    GeneratedPrototypeCount = results.Count(x => x.Generated),
                    FailedPrototypeCount = results.Count(x => !x.Generated),
                    T65XWingGenerated = results.Any(x => x.ShipId.Equals("xwing", StringComparison.OrdinalIgnoreCase) && x.Generated),
                    Arc170Generated = results.Any(x => x.ShipId.Equals("arc170", StringComparison.OrdinalIgnoreCase) && x.Generated)
                }
            };
            File.WriteAllText(Path.Combine(outputFolder, "prototype-build-report.json"), JsonSerializer.Serialize(document, JsonOptions));
            WriteValidationChecklist(Path.Combine(outputFolder, "TTS-VALIDATION-CHECKLIST.md"), results, savePath);

            Console.WriteLine("UnifiedToolkit Phase 5B Revision 3 - Static Scriptless TTS Prototypes");
            Console.WriteLine("===================================================================");
            Console.WriteLine();
            Console.WriteLine($"Hybrid definitions:       {hybridPath}");
            Console.WriteLine($"Unified 2.5 save:         {unifiedSavePath}");
            Console.WriteLine($"Composite base resolved:  True");
            Console.WriteLine($"Requested prototypes:     {document.Summary.RequestedShipCount}");
            Console.WriteLine($"Generated prototypes:     {document.Summary.GeneratedPrototypeCount}");
            Console.WriteLine($"Failed prototypes:        {document.Summary.FailedPrototypeCount}");
            Console.WriteLine($"T-65 X-Wing generated:    {document.Summary.T65XWingGenerated}");
            Console.WriteLine($"ARC-170 generated:        {document.Summary.Arc170Generated}");
            Console.WriteLine();
            Console.WriteLine($"Prototype save: {savePath}");
            Console.WriteLine($"Output folder:  {outputFolder}");
            Console.WriteLine();
            Console.WriteLine("Load the generated prototype save in Tabletop Simulator and complete TTS-VALIDATION-CHECKLIST.md.");
            Console.WriteLine("These are static scriptless visual prototypes. Base, peg and model are independent objects so JSON loading can be validated before attachments are restored.");
            return document.Summary.FailedPrototypeCount == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Prototype build failed: {ex.Message}");
            return 1;
        }
    }

    private static ShipAppearanceVariant? SelectAppearance(HybridShipDefinition ship, string preferred) =>
        ship.AppearanceVariants.FirstOrDefault(x => x.DisplayName.Equals(preferred, StringComparison.OrdinalIgnoreCase))
        ?? ship.AppearanceVariants.FirstOrDefault();

    private static TtsObject? FindRecursive(IEnumerable<TtsObject> objects, string guid)
    {
        foreach (var item in objects)
        {
            if (item.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase))
                return item;
            var child = FindRecursive(item.Children, guid);
            if (child is not null)
                return child;
        }
        return null;
    }

    private static ShipPrototypeBuildResult Failed(string shipId, string error) => new()
    {
        ShipId = shipId,
        Generated = false,
        Errors = new[] { error }
    };

    private static string Safe(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-').ToLowerInvariant();
    }

    private static string ResolveOutputFolder(string[] args, string defaultPath)
    {
        for (var i = 2; i < args.Length - 1; i++)
            if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[i + 1]);
        return Path.GetFullPath(defaultPath);
    }

    private static void WriteValidationChecklist(string path, IReadOnlyList<ShipPrototypeBuildResult> results, string savePath)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("# Phase 5B TTS Prototype Validation");
        writer.WriteLine();
        writer.WriteLine($"Load `{Path.GetFileName(savePath)}` in Tabletop Simulator.");
        writer.WriteLine();
        foreach (var result in results.Where(x => x.Generated))
        {
            writer.WriteLine($"## {result.ShipName} — {result.AppearanceName}");
            writer.WriteLine();
            writer.WriteLine($"- Expected First Edition base: **{result.FirstEditionBaseSize}**");
            if (result.MediumRemoved)
                writer.WriteLine($"- Source 2.5 base `{result.Source25BaseSize}` must not appear.");
            writer.WriteLine("- [ ] Base is the correct First Edition physical size.");
            writer.WriteLine("- [ ] Ship faces forward relative to the firing arc.");
            writer.WriteLine("- [ ] Ship is centred over the peg.");
            writer.WriteLine("- [ ] Peg is centred over the base.");
            writer.WriteLine("- [ ] Model height looks correct.");
            writer.WriteLine("- [ ] Model scale looks correct beside a physical/reference miniature.");
            writer.WriteLine("- [ ] Model and peg remain attached when moved and rotated.");
            writer.WriteLine("- [ ] Texture is the intended official First Edition colour scheme.");
            writer.WriteLine();
            writer.WriteLine("Record any required correction:");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine("Scale:");
            writer.WriteLine("Height:");
            writer.WriteLine("Rotation:");
            writer.WriteLine("Position:");
            writer.WriteLine("Other:");
            writer.WriteLine("```");
            writer.WriteLine();
        }
    }

    private sealed record PrototypeRequest(string ShipId, string PreferredAppearance, float PositionX);
}
