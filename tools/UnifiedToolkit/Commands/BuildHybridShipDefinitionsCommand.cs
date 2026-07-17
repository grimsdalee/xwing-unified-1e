using System.Text.Json;
using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Hybrid;

namespace UnifiedToolkit.Commands;

public static class BuildHybridShipDefinitionsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: UnifiedToolkit build-hybrid-ships <repo-folder> <unified-2.5-save.json> <legacy-1e-save.json> [mapping-folder] [--allow-source-errors] [--output <folder>]");
            return 1;
        }
        try
        {
            var repositoryFolder = Path.GetFullPath(args[0]);
            var unifiedSave = Path.GetFullPath(args[1]);
            var legacySave = Path.GetFullPath(args[2]);
            var allowSourceErrors = args.Any(x => x.Equals("--allow-source-errors", StringComparison.OrdinalIgnoreCase));
            var mappingFolder = ResolveMappingFolder(args.Skip(3).ToArray());
            var outputFolder = ResolveOutputFolder(args, Path.Combine(repositoryFolder, "_unifiedtoolkit_reports", "hybrid"));

            Console.WriteLine("UnifiedToolkit Phase 4B Revision 3 - Concrete Ship Construction Recipe");
            Console.WriteLine("======================================================================");
            Console.WriteLine();
            Console.WriteLine($"Semantic repository: {repositoryFolder}");
            Console.WriteLine($"Unified 2.5 save:    {unifiedSave}");
            Console.WriteLine($"Legacy 1E save:      {legacySave}");
            Console.WriteLine($"Mapping folder:      {mappingFolder}");
            Console.WriteLine();

            var semanticBuild = FirstEditionRepositoryBuilder.Build(repositoryFolder, mappingFolder, allowSourceErrors);
            var baseConversions = FirstEditionBaseDefinitionCatalogue.BuildConversions(repositoryFolder, semanticBuild.Repository);
            FirstEditionBaseDefinitionCatalogue.ValidateNoMedium(FirstEditionBaseDefinitionCatalogue.Definitions, baseConversions);
            var frameworks = SpawnerFrameworkAnalyser.Analyse(unifiedSave);
            var spawnerLua = SpawnerLuaAnalyser.Analyse(unifiedSave);
            var shipRecipe = ShipSpawnerRecipeExtractor.Analyse(unifiedSave);
            var legacy = LegacyShipAssetCatalogueBuilder.Build(legacySave, semanticBuild.Repository);
            var document = HybridShipDefinitionBuilder.Build(semanticBuild.Repository, semanticBuild.MappingVersion, frameworks, legacy, baseConversions, unifiedSave, legacySave);

            Directory.CreateDirectory(outputFolder);
            WriteJson(Path.Combine(outputFolder, "first-edition-base-definitions.json"), FirstEditionBaseDefinitionCatalogue.Definitions);
            WriteJson(Path.Combine(outputFolder, "first-edition-base-size-conversions.json"), baseConversions);
            WriteBaseConversionCsv(Path.Combine(outputFolder, "first-edition-base-size-conversions.csv"), baseConversions);
            WriteJson(Path.Combine(outputFolder, "spawner-framework-catalogue.json"), frameworks);
            WriteJson(Path.Combine(outputFolder, "spawner-lua-normalization.json"), spawnerLua.Normalization);
            WriteJson(Path.Combine(outputFolder, "spawner-bundle-modules.json"), spawnerLua.BundleModules);
            WriteJson(Path.Combine(outputFolder, "spawner-entry-points.json"), spawnerLua.EntryPoints);
            WriteJson(Path.Combine(outputFolder, "spawner-function-catalogue.json"), spawnerLua.Functions);
            WriteJson(Path.Combine(outputFolder, "spawner-object-references.json"), spawnerLua.ObjectReferences);
            WriteJson(Path.Combine(outputFolder, "spawn-json-call-sites.json"), spawnerLua.CallSites);
            WriteJson(Path.Combine(outputFolder, "ship-construction-pipeline.json"), spawnerLua.ConstructionPipeline);
            WriteJson(Path.Combine(outputFolder, "first-edition-spawner-definitions.json"), spawnerLua.FirstEditionDefinitions);
            WriteJson(Path.Combine(outputFolder, "spawner-lua-analysis.json"), spawnerLua);
            WriteJson(Path.Combine(outputFolder, "ship-spawner-module.json"), shipRecipe.Functions);
            WriteJson(Path.Combine(outputFolder, "ship-spawner-call-graph.json"), shipRecipe.CallGraph);
            WriteJson(Path.Combine(outputFolder, "ship-base-prototype-selection.json"), shipRecipe.BasePrototype);
            WriteJson(Path.Combine(outputFolder, "ship-model-attachment-recipe.json"), shipRecipe.ModelAttachment);
            WriteJson(Path.Combine(outputFolder, "ship-config-attachment-recipe.json"), shipRecipe.ConfigAttachment);
            WriteJson(Path.Combine(outputFolder, "spawner-indirect-guid-references.json"), shipRecipe.IndirectObjectReferences);
            WriteJson(Path.Combine(outputFolder, "first-edition-ship-construction-recipes.json"), shipRecipe.FirstEditionRecipes);
            WriteJson(Path.Combine(outputFolder, "ship-spawner-recipe-analysis.json"), shipRecipe);
            WriteJson(Path.Combine(outputFolder, "legacy-ship-family-catalogue.json"), legacy.ShipFamilies);
            WriteJson(Path.Combine(outputFolder, "legacy-model-object-catalogue.json"), legacy.ModelObjects);
            WriteJson(Path.Combine(outputFolder, "legacy-dial-catalogue.json"), legacy.Dials);
            WriteJson(Path.Combine(outputFolder, "legacy-ship-reference-catalogue.json"), legacy.ShipReferences);
            WriteJson(Path.Combine(outputFolder, "legacy-physical-base-token-catalogue.json"), legacy.PhysicalBaseTokens);
            WriteJson(Path.Combine(outputFolder, "legacy-card-catalogue.json"), legacy.Cards);
            WriteJson(Path.Combine(outputFolder, "legacy-ignored-models.json"), legacy.IgnoredObjects);
            WriteJson(Path.Combine(outputFolder, "legacy-ship-asset-catalogue.json"), legacy);
            WriteJson(Path.Combine(outputFolder, "hybrid-ship-definitions.json"), document);
            WriteCoverageCsv(Path.Combine(outputFolder, "hybrid-ship-coverage.csv"), document);

            Console.WriteLine($"Semantic mapping:          {document.SemanticMappingVersion}");
            Console.WriteLine($"Semantic ships:            {document.Summary.ShipCount}");
            Console.WriteLine($"1E base definitions:       {FirstEditionBaseDefinitionCatalogue.Definitions.Count}");
            Console.WriteLine($"2.5 size conversions:      {baseConversions.Count(x => x.ConversionRequired)}");
            Console.WriteLine($"Medium bases removed:      {baseConversions.Count(x => x.MediumRemoved)}");
            Console.WriteLine($"Raw Lua lines:             {spawnerLua.Normalization.RawLineCount}");
            Console.WriteLine($"Normalized Lua lines:      {spawnerLua.Normalization.NormalizedLineCount}");
            Console.WriteLine($"Lua decode passes:         {spawnerLua.Normalization.DecodePassesApplied}");
            Console.WriteLine($"Bundle modules:            {spawnerLua.Summary.BundleModuleCount}");
            Console.WriteLine($"Lua functions catalogued:  {spawnerLua.Summary.FunctionCount}");
            Console.WriteLine($"Spawner entry points:       {spawnerLua.Summary.EntryPointCount}");
            Console.WriteLine($"spawnObjectJSON calls:      {spawnerLua.Summary.SpawnJsonCallCount}");
            Console.WriteLine($"Referenced object GUIDs:    {spawnerLua.Summary.ReferencedGuidCount}");
            Console.WriteLine($"Resolved object GUIDs:      {spawnerLua.Summary.ResolvedGuidCount}");
            Console.WriteLine($"1E spawner definitions:     {spawnerLua.FirstEditionDefinitions.Count}");
            Console.WriteLine($"Focused spawner functions:  {shipRecipe.Summary.FocusedFunctionCount}");
            Console.WriteLine($"Spawner call-graph edges:   {shipRecipe.Summary.CallGraphEdgeCount}");
            Console.WriteLine($"Indirect GUID references:   {shipRecipe.Summary.IndirectReferenceCount}");
            Console.WriteLine($"Resolved indirect GUIDs:    {shipRecipe.Summary.ResolvedIndirectReferenceCount}");
            Console.WriteLine($"Composite base resolved:    {shipRecipe.Summary.CompositeBaseResolved}");
            Console.WriteLine($"1E construction recipes:    {shipRecipe.Summary.FirstEditionRecipeCount}");
            Console.WriteLine($"Framework candidates:      {document.Summary.FrameworkTemplateCount}");
            Console.WriteLine($"Legacy model objects:      {document.Summary.LegacyModelObjectCount}");
            Console.WriteLine($"Legacy ship families:      {document.Summary.LegacyShipFamilyCount}");
            Console.WriteLine($"Unique appearances:        {document.Summary.UniqueAppearanceCount}");
            Console.WriteLine($"Ships with appearance:     {document.Summary.ShipsWithAppearance}");
            Console.WriteLine($"Ships with dial:           {document.Summary.ShipsWithDial}");
            Console.WriteLine($"Ships with ship reference: {document.Summary.ShipsWithShipReference}");
            Console.WriteLine($"Ships with base token:     {document.Summary.ShipsWithPhysicalBaseToken}");
            Console.WriteLine($"Framework ready:           {document.Summary.ShipsFrameworkReady}");
            Console.WriteLine($"Appearance ready:          {document.Summary.ShipsAppearanceReady}");
            Console.WriteLine($"Edition-assets ready:      {document.Summary.ShipsEditionAssetsReady}");
            Console.WriteLine($"Ready for object builder:  {document.Summary.ShipsReadyForObjectBuilder}");
            Console.WriteLine();
            Console.WriteLine($"Output folder: {outputFolder}");
            Console.WriteLine();
            Console.WriteLine("The concrete clone/spawn/attachment recipe is extracted from Game.Component.Spawner.Spawner. First Edition construction recipes expose only Small, Large and Epic; Medium is rejected.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Hybrid ship definition build failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));


    private static void WriteBaseConversionCsv(string path, IReadOnlyList<ShipBaseSizeConversion> conversions)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("ShipId,ShipName,Source25ShipId,Source25BaseSize,FirstEditionBaseSize,ConversionType,ConversionRequired,MediumRemoved,ValidationStatus,Notes");
        foreach (var item in conversions)
        {
            var row = new[]
            {
                item.ShipId, item.ShipName, item.Source25ShipId, item.Source25BaseSize, item.FirstEditionBaseSize.ToString(), item.ConversionType.ToString(),
                item.ConversionRequired.ToString(), item.MediumRemoved.ToString(), item.ValidationStatus, string.Join(" | ", item.Notes)
            };
            writer.WriteLine(string.Join(',', row.Select(Csv)));
        }
    }

    private static void WriteCoverageCsv(string path, HybridShipDefinitionDocument document)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("ShipId,ShipName,Factions,FirstEditionBaseSize,Source25BaseSize,BaseConversionRequired,MediumRemoved,Pilots,FrameworkGuid,Family,FamilyPath,FamilyScore,UniqueAppearances,ShipReferences,PhysicalBaseTokens,Dials,Cards,FrameworkReady,AppearanceReady,EditionAssetsReady,ObjectBuilderReady,CompleteSaveReady,Issues");
        foreach (var ship in document.Ships)
        {
            var row = new[]
            {
                ship.SemanticData.Id, ship.SemanticData.Name, string.Join("|", ship.SemanticData.Factions), ship.BaseDefinition.Size.ToString(), ship.BaseSizeConversion?.Source25BaseSize ?? "", ship.BaseSizeConversion?.ConversionRequired.ToString() ?? "False", ship.BaseSizeConversion?.MediumRemoved.ToString() ?? "False", ship.Pilots.Count.ToString(),
                ship.SpawnFramework?.SourceGuid ?? "", ship.LegacyShipFamily?.DisplayName ?? "", ship.LegacyShipFamily?.SourcePath ?? "", ship.LegacyShipFamily?.MatchScore.ToString() ?? "",
                ship.AppearanceVariants.Count.ToString(), ship.EditionAssets.ShipReferences.Count.ToString(), ship.EditionAssets.PhysicalBaseTokens.Count.ToString(), ship.EditionAssets.Dials.Count.ToString(), ship.EditionAssets.Cards.Count.ToString(),
                ship.Readiness.FrameworkReady.ToString(), ship.Readiness.AppearanceReady.ToString(), ship.Readiness.EditionAssetsReady.ToString(), ship.Readiness.ReadyForObjectBuilder.ToString(),
                ship.Readiness.CompleteSaveReady.ToString(), string.Join(" | ", ship.Readiness.Issues)
            };
            writer.WriteLine(string.Join(',', row.Select(Csv)));
        }
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string ResolveMappingFolder(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) { if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase)) i++; continue; }
            return Path.GetFullPath(args[i]);
        }
        return Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
    }
    private static string ResolveOutputFolder(string[] args, string defaultPath)
    {
        for (var i = 0; i < args.Length - 1; i++) if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(args[i + 1]);
        return defaultPath;
    }
}
