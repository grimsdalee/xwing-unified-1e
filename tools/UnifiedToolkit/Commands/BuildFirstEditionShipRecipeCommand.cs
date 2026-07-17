using System.Text.Json;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class BuildFirstEditionShipRecipeCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: UnifiedToolkit build-first-edition-ship-recipe <hybrid-ship-definitions.json> <ship-construction-recipes.json> [--ship <id-or-name>] [--output <folder>]");
            return 1;
        }

        try
        {
            var hybridPath = Path.GetFullPath(args[0]);
            var constructionPath = Path.GetFullPath(args[1]);
            var targetShip = Option(args, "--ship") ?? "t-65-x-wing";
            var outputFolder = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(Path.GetDirectoryName(hybridPath) ?? ".", "first-edition-ship-recipe"));

            Console.WriteLine("UnifiedToolkit Phase 5E Revision 1 - First Edition Ship Recipe");
            Console.WriteLine("================================================================");
            Console.WriteLine();
            Console.WriteLine($"Hybrid definitions:  {hybridPath}");
            Console.WriteLine($"Construction report: {constructionPath}");
            Console.WriteLine($"Target ship:         {targetShip}");
            Console.WriteLine($"Output folder:       {outputFolder}");
            Console.WriteLine();

            var document = FirstEditionShipRecipeBuilder.Build(hybridPath, constructionPath, targetShip);
            Directory.CreateDirectory(outputFolder);

            WriteJson(Path.Combine(outputFolder, "first-edition-ship-recipe.json"), document);
            WriteAppearancesCsv(Path.Combine(outputFolder, "first-edition-ship-appearances.csv"), document);
            WriteAssetsCsv(Path.Combine(outputFolder, "first-edition-ship-assets.csv"), document);
            WriteMarkdown(Path.Combine(outputFolder, "FIRST-EDITION-SHIP-RECIPE-REPORT.md"), document);

            Console.WriteLine($"Ship found:                 {document.Summary.ShipFound}");
            Console.WriteLine($"Ship:                       {document.TargetShipName}");
            Console.WriteLine($"Valid First Edition base:   {document.Summary.ValidFirstEditionBase}");
            Console.WriteLine($"Medium base rejected:       {document.Summary.MediumBaseRejected}");
            Console.WriteLine($"Pilots:                     {document.Summary.PilotCount}");
            Console.WriteLine($"Appearance variants:        {document.Summary.AppearanceVariantCount}");
            Console.WriteLine($"Dial assets:                {document.Summary.DialAssetCount}");
            Console.WriteLine($"Runtime recipe available:   {document.Summary.RuntimeRecipeAvailable}");
            Console.WriteLine($"Ready for review:           {document.Summary.ReadyForReview}");
            Console.WriteLine();
            Console.WriteLine("This command creates a review recipe only. It does not generate or modify TTS objects.");

            return document.Summary.ReadyForReview ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"First Edition ship recipe build failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));

    private static void WriteAppearancesCsv(string path, FirstEditionShipRecipeDocument document)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Selected,VariantId,DisplayName,SourceGuid,SourcePath,MeshUrl,DiffuseUrl,NormalUrl,ColliderUrl,HasMesh,HasDiffuse");
        foreach (var item in document.Recipe?.AppearanceVariants ?? new())
        {
            var selected = document.Recipe?.SelectedAppearance?.VariantId == item.VariantId;
            writer.WriteLine(string.Join(',', new[]
            {
                selected.ToString(), item.VariantId, item.DisplayName, item.SourceGuid, item.SourcePath,
                item.MeshUrl, item.DiffuseUrl, item.NormalUrl, item.ColliderUrl,
                item.HasMesh.ToString(), item.HasDiffuse.ToString()
            }.Select(Csv)));
        }
    }

    private static void WriteAssetsCsv(string path, FirstEditionShipRecipeDocument document)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Role,AssetId,SourceGuid,SourceName,SourcePath,FactionHint,MatchScore");
        if (document.Recipe is null) return;
        WriteRole(writer, "Dial", document.Recipe.EditionAssets.Dials);
        WriteRole(writer, "ShipReference", document.Recipe.EditionAssets.ShipReferences);
        WriteRole(writer, "PhysicalBaseToken", document.Recipe.EditionAssets.PhysicalBaseTokens);
        WriteRole(writer, "Card", document.Recipe.EditionAssets.Cards);
    }

    private static void WriteRole(StreamWriter writer, string role, IEnumerable<FirstEditionRecipeAsset> assets)
    {
        foreach (var item in assets)
            writer.WriteLine(string.Join(',', new[] { role, item.AssetId, item.SourceGuid, item.SourceName, item.SourcePath, item.FactionHint, item.MatchScore.ToString() }.Select(Csv)));
    }

    private static void WriteMarkdown(string path, FirstEditionShipRecipeDocument document)
    {
        var recipe = document.Recipe;
        var lines = new List<string>
        {
            "# First Edition Ship Construction Recipe",
            "",
            $"- Ship: {document.TargetShipName}",
            $"- Ship ID: {document.TargetShipId}",
            $"- Ready for review: {document.Summary.ReadyForReview}",
            ""
        };
        if (recipe is not null)
        {
            lines.AddRange(new[]
            {
                "## Base and runtime inputs", "",
                $"- First Edition base: {recipe.FirstEditionBaseSize}",
                $"- Source 2.5 base: {recipe.Source25BaseSize}",
                $"- Medium removed: {recipe.MediumRemoved}",
                $"- Base mesh: `{recipe.RuntimeParameters.BaseMeshPath}`",
                $"- Peg mesh: `{recipe.RuntimeParameters.PegMeshPath}`",
                $"- Ship mesh: `{recipe.RuntimeParameters.ShipMeshUrl}`",
                $"- Ship texture: `{recipe.RuntimeParameters.ShipDiffuseUrl}`",
                "", "## Selected review inputs", "",
                $"- Pilot: {recipe.SelectedPilotName} ({recipe.SelectedPilotId})",
                $"- Appearance: {recipe.SelectedAppearance?.DisplayName ?? "None"}",
                "", "## Validation", ""
            });
            if (recipe.ValidationErrors.Count == 0) lines.Add("- No validation errors.");
            else lines.AddRange(recipe.ValidationErrors.Select(x => "- ERROR: " + x));
            lines.AddRange(new[] { "", "## Review notes", "" });
            lines.AddRange(recipe.ReviewNotes.Select(x => "- " + x));
        }
        lines.AddRange(new[] { "", "This report does not generate or spawn a Tabletop Simulator object.", "" });
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
