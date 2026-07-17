using System.Text.Json;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class BuildFirstEditionShipObjectModelCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: UnifiedToolkit build-first-edition-ship-object-model <hybrid-ship-definitions.json> <ship-construction-recipes.json> [--ship <id-or-name>] [--output <folder>]");
            return 1;
        }

        try
        {
            var hybridPath = Path.GetFullPath(args[0]);
            var constructionPath = Path.GetFullPath(args[1]);
            var targetShip = Option(args, "--ship") ?? "xwing";
            var outputFolder = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(Path.GetDirectoryName(hybridPath) ?? ".", "first-edition-ship-object-model"));

            Console.WriteLine("UnifiedToolkit Phase 5F Revision 2 - First Edition Ship Object Model");
            Console.WriteLine("====================================================================");
            Console.WriteLine();
            Console.WriteLine($"Hybrid definitions:  {hybridPath}");
            Console.WriteLine($"Construction report: {constructionPath}");
            Console.WriteLine($"Target ship:         {targetShip}");
            Console.WriteLine($"Output folder:       {outputFolder}");
            Console.WriteLine();

            var document = FirstEditionShipObjectModelBuilder.Build(hybridPath, constructionPath, targetShip);
            Directory.CreateDirectory(outputFolder);
            WriteJson(Path.Combine(outputFolder, "first-edition-ship-object-model.json"), document);
            WriteAuditCsv(Path.Combine(outputFolder, "first-edition-ship-value-audit.csv"), document);
            WriteComponentsCsv(Path.Combine(outputFolder, "first-edition-ship-components.csv"), document);
            WriteMarkdown(Path.Combine(outputFolder, "FIRST-EDITION-SHIP-OBJECT-MODEL-REPORT.md"), document);

            Console.WriteLine($"Recipe available:               {document.Summary.RecipeAvailable}");
            Console.WriteLine($"Base component valid:           {document.Summary.BaseComponentValid}");
            Console.WriteLine($"Peg component valid:            {document.Summary.PegComponentValid}");
            Console.WriteLine($"Ship model component valid:     {document.Summary.ShipModelComponentValid}");
            Console.WriteLine($"Identifier component valid:     {document.Summary.IdentifierComponentValid}");
            Console.WriteLine($"Pilot/dial component valid:     {document.Summary.PilotDialComponentValid}");
            Console.WriteLine($"Medium rejected:                {document.Summary.MediumRejected}");
            Console.WriteLine($"Audit entries:                  {document.Summary.AuditEntryCount}");
            Console.WriteLine($"Validation errors:              {document.Summary.ErrorCount}");
            Console.WriteLine($"Ready for serialization review: {document.Summary.ReadyForSerializationReview}");
            Console.WriteLine();
            Console.WriteLine("This command builds and validates an in-memory component model only. It does not generate or modify TTS objects.");

            return document.Summary.ReadyForSerializationReview ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"First Edition ship object-model build failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    }));

    private static void WriteAuditCsv(string path, FirstEditionShipObjectModelDocument document)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Sequence,Stage,Property,Value,Expected,Valid,Source,Note");
        foreach (var item in document.AuditTrail)
            writer.WriteLine(string.Join(',', new[]
            {
                item.Sequence.ToString(), item.Stage, item.Property, item.Value, item.Expected,
                item.Valid.ToString(), item.Source, item.Note
            }.Select(Csv)));
    }

    private static void WriteComponentsCsv(string path, FirstEditionShipObjectModelDocument document)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Component,Valid,Key,Value");
        var model = document.ObjectModel;
        if (model is null) return;
        Row(writer, "Base", model.Base.IsValid, "Size", model.Base.Size.ToString());
        Row(writer, "Base", model.Base.IsValid, "MeshPath", model.Base.MeshPath);
        Row(writer, "Peg", model.Peg.IsValid, "PegType", model.Peg.PegType);
        Row(writer, "Peg", model.Peg.IsValid, "MeshPath", model.Peg.MeshPath);
        Row(writer, "ShipModel", model.ShipModel.IsValid, "Appearance", model.ShipModel.AppearanceName);
        Row(writer, "ShipModel", model.ShipModel.IsValid, "MeshUrl", model.ShipModel.MeshUrl);
        Row(writer, "Identifier", model.Identifier.IsValid, "BaseSize", model.Identifier.BaseSize);
        Row(writer, "PilotDial", model.PilotDial.IsValid, "Pilot", model.PilotDial.PilotName);
        Row(writer, "PilotDial", model.PilotDial.IsValid, "DialAssets", model.PilotDial.DialAssets.Count.ToString());
    }

    private static void Row(StreamWriter writer, string component, bool valid, string key, string value) =>
        writer.WriteLine(string.Join(',', new[] { component, valid.ToString(), key, value }.Select(Csv)));

    private static void WriteMarkdown(string path, FirstEditionShipObjectModelDocument document)
    {
        var model = document.ObjectModel;
        var lines = new List<string>
        {
            "# First Edition Ship Object Model",
            "",
            $"- Target: {document.TargetShip}",
            $"- Ready for serialization review: {document.Summary.ReadyForSerializationReview}",
            $"- Validation errors: {document.Summary.ErrorCount}",
            ""
        };
        if (model is not null)
        {
            lines.AddRange(new[]
            {
                "## Components", "",
                $"- Base: {model.Base.Size} — valid: {model.Base.IsValid}",
                $"- Peg: {model.Peg.PegType} — valid: {model.Peg.IsValid}",
                $"- Ship model: {model.ShipModel.AppearanceName} — valid: {model.ShipModel.IsValid}",
                $"- Identifier: {model.Identifier.BaseSize} — valid: {model.Identifier.IsValid}",
                $"- Pilot/dial: {model.PilotDial.PilotName} — valid: {model.PilotDial.IsValid}",
                "", "## Base-size audit", ""
            });
            lines.AddRange(document.AuditTrail
                .Where(x => x.Property.Contains("Size", StringComparison.OrdinalIgnoreCase) || x.Property.Contains("Mesh", StringComparison.OrdinalIgnoreCase) || x.Property.Contains("Peg", StringComparison.OrdinalIgnoreCase))
                .Select(x => $"- [{(x.Valid ? "PASS" : "FAIL")}] {x.Stage} / {x.Property}: `{x.Value}` (expected `{x.Expected}`)"));
        }
        lines.AddRange(new[] { "", "## Validation errors", "" });
        if (document.ValidationErrors.Count == 0) lines.Add("- None.");
        else lines.AddRange(document.ValidationErrors.Select(x => "- " + x));
        lines.AddRange(new[] { "", "No Tabletop Simulator object or save is generated by this command.", "" });
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
