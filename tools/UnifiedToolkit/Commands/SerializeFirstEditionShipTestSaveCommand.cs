using System.Text.Json;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class SerializeFirstEditionShipTestSaveCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: UnifiedToolkit serialize-first-edition-ship-test-save <first-edition-ship-object-model.json> <unified-2.5-save.json> <unified-repo-folder> [--output <folder>]");
            return 1;
        }

        try
        {
            var objectModelPath = Path.GetFullPath(args[0]);
            var unifiedSavePath = Path.GetFullPath(args[1]);
            var unifiedRepositoryPath = Path.GetFullPath(args[2]);
            var outputFolder = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(Path.GetDirectoryName(objectModelPath) ?? ".", "first-edition-ship-test-save-r3"));
            var savePath = Path.Combine(outputFolder, "XWing-1E-Phase5G-R3-Runtime-Derived-Assembly-Test.json");

            Console.WriteLine("UnifiedToolkit Phase 5G Revision 3 - Runtime-Derived T-65 Assembly Serializer");
            Console.WriteLine("============================================================================");
            Console.WriteLine();
            Console.WriteLine($"Object model:       {objectModelPath}");
            Console.WriteLine($"Unified envelope:   {unifiedSavePath}");
            Console.WriteLine($"Unified repository: {unifiedRepositoryPath}");
            Console.WriteLine($"Output folder:      {outputFolder}");
            Console.WriteLine();

            Directory.CreateDirectory(outputFolder);
            var result = FirstEditionShipTestSaveSerializer.Serialize(objectModelPath, unifiedSavePath, unifiedRepositoryPath, savePath);
            WriteJson(Path.Combine(outputFolder, "first-edition-ship-test-save-report.json"), result);
            WriteObjectCsv(Path.Combine(outputFolder, "first-edition-ship-test-save-objects.csv"), result);
            WriteAssetCsv(Path.Combine(outputFolder, "first-edition-ship-asset-resolutions.csv"), result);
            WriteConfigurationCsv(Path.Combine(outputFolder, "first-edition-ship-configurations.csv"), result);
            WriteMarkdown(Path.Combine(outputFolder, "FIRST-EDITION-SHIP-TEST-SAVE-REPORT.md"), result);

            Console.WriteLine($"Ship:                              {result.ShipName}");
            Console.WriteLine($"Pilot/appearance:                  {result.PilotName} / {result.AppearanceName}");
            Console.WriteLine($"First Edition base:                {result.BaseSize}");
            Console.WriteLine($"Asset decisions:                   {result.AssetResolutions.Count}");
            Console.WriteLine($"Accepted asset decisions:          {result.AssetResolutions.Count(x => x.Accepted)}");
            Console.WriteLine($"Rejected/unresolved assets:        {result.AssetResolutions.Count(x => !x.Accepted)}");
            Console.WriteLine($"Objects serialized:                {result.ObjectCount}");
            Console.WriteLine($"Base serialized:                   {result.BaseSerialized}");
            Console.WriteLine($"Peg serialized:                    {result.PegSerialized}");
            Console.WriteLine($"Primary fuselage serialized:       {result.PrimaryShipModelSerialized}");
            Console.WriteLine($"Open S-foils serialized:           {result.VisibleConfigurationSerialized}");
            Console.WriteLine($"Closed S-foils metadata recorded:  {result.AlternateConfigurationRecorded}");
            Console.WriteLine($"Ready for TTS load test:           {result.ReadyForTtsLoadTest}");
            Console.WriteLine();
            Console.WriteLine($"Test save: {savePath}");
            Console.WriteLine();
            Console.WriteLine("This is a scriptless four-part geometry test derived from the live Unified T-65 runtime object.");
            return result.ReadyForTtsLoadTest ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"First Edition ship test-save serialization failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));

    private static void WriteObjectCsv(string path, FirstEditionShipTestSaveResult result)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Component,Guid,Nickname,MeshUrl,DiffuseUrl,ColliderUrl,PositionX,PositionY,PositionZ,RotationY,Scale");
        foreach (var item in result.Objects)
            writer.WriteLine(string.Join(',', new[]
            {
                item.Component, item.Guid, item.Nickname, item.MeshUrl, item.DiffuseUrl, item.ColliderUrl,
                item.PositionX.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.PositionY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.PositionZ.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.RotationY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }.Select(Csv)));
    }

    private static void WriteAssetCsv(string path, FirstEditionShipTestSaveResult result)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Component,Role,Accepted,Source,OriginalValue,ResolvedValue,LocalPath,Reason");
        foreach (var item in result.AssetResolutions)
            writer.WriteLine(string.Join(',', new[]
            {
                item.Component, item.Role, item.Accepted.ToString(), item.Source, item.OriginalValue,
                item.ResolvedValue, item.LocalPath, item.Reason
            }.Select(Csv)));
    }

    private static void WriteConfigurationCsv(string path, FirstEditionShipTestSaveResult result)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Role,Name,MeshUrl,SerializedVisible");
        writer.WriteLine(string.Join(',', new[] { "Primary", "Primary fuselage", result.Configuration.PrimaryMesh, "True" }.Select(Csv)));
        writer.WriteLine(string.Join(',', new[] { "VisibleConfiguration", result.Configuration.VisibleConfigurationName, result.Configuration.VisibleConfigurationMesh, "True" }.Select(Csv)));
        writer.WriteLine(string.Join(',', new[] { "AlternateConfiguration", result.Configuration.AlternateConfigurationName, result.Configuration.AlternateConfigurationMesh, "False" }.Select(Csv)));
    }

    private static void WriteMarkdown(string path, FirstEditionShipTestSaveResult result)
    {
        var lines = new List<string>
        {
            "# First Edition T-65 Runtime-Derived Physical Assembly Test", "",
            $"- Ship: {result.ShipName}",
            $"- Pilot/appearance requested: {result.PilotName} / {result.AppearanceName}",
            $"- First Edition base: {result.BaseSize}",
            $"- Objects: {result.ObjectCount}",
            $"- Ready for TTS load test: {result.ReadyForTtsLoadTest}", "",
            "## Runtime-derived model group", "",
            $"- Primary fuselage: `{result.Configuration.PrimaryMesh}`",
            $"- Visible configuration ({result.Configuration.VisibleConfigurationName}): `{result.Configuration.VisibleConfigurationMesh}`",
            $"- Alternate configuration ({result.Configuration.AlternateConfigurationName}, metadata only): `{result.Configuration.AlternateConfigurationMesh}`",
            $"- Shared texture: `{result.Configuration.SharedTexture}`", "",
            "## Asset resolutions", ""
        };
        lines.AddRange(result.AssetResolutions.Select(x =>
            $"- **{x.Component}/{x.Role}** — {(x.Accepted ? "accepted" : "unresolved")} from {x.Source}: `{x.ResolvedValue}` — {x.Reason}"));
        lines.AddRange(new[] { "", "## Serialized objects", "" });
        lines.AddRange(result.Objects.Select(x => $"- {x.Component}: Y={x.PositionY}, scale={x.Scale}, mesh=`{x.MeshUrl}`"));
        lines.AddRange(new[] { "", "## Test scope", "" });
        lines.AddRange(result.ReviewNotes.Select(x => "- " + x));
        lines.AddRange(new[] { "", "## Validation errors", "" });
        lines.AddRange(result.ValidationErrors.Count == 0 ? new[] { "- None." } : result.ValidationErrors.Select(x => "- " + x));
        File.WriteAllLines(path, lines);
    }

    private static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
