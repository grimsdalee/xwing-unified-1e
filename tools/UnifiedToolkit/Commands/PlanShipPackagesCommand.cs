using System.Text.Json;
using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.FirstEdition.Export;
using System.Text.Json.Serialization;
using UnifiedToolkit.KnowledgeBase;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class PlanShipPackagesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: UnifiedToolkit plan-ship-packages <first-edition-repo-folder> [mapping-folder] [--allow-source-errors] [--output <folder>]");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var mappingFolder = ResolveMappingFolder(args.Skip(1).ToArray());
            var allowSourceErrors = args.Any(x => x.Equals("--allow-source-errors", StringComparison.OrdinalIgnoreCase));
            var outputFolder = ResolveOption(args, "--output")
                               ?? Path.Combine(repositoryRoot, "_unifiedtoolkit_reports", "phase11", "ship-package-planning");
            outputFolder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(outputFolder);

            Console.WriteLine("UnifiedToolkit Phase 11A Ship Package Planner");
            Console.WriteLine("==============================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:       {repositoryRoot}");
            Console.WriteLine($"Mapping folder:   {mappingFolder}");
            Console.WriteLine($"Output folder:    {outputFolder}");
            Console.WriteLine();

            var semanticBuild = LoadSemanticRepository(repositoryRoot, mappingFolder, allowSourceErrors);
            var knowledgeBase = new KnowledgeBaseQueryService().Load(repositoryRoot);
            var document = FirstEditionShipPackagePlanner.Build(
                repositoryRoot,
                semanticBuild.Repository,
                semanticBuild.MappingVersion,
                knowledgeBase);

            WriteJson(Path.Combine(outputFolder, "ship-package-plans.json"), document);
            WriteResolutionCsv(Path.Combine(outputFolder, "ship-package-asset-resolution.csv"), document);
            WriteFilteredCsv(Path.Combine(outputFolder, "unresolved-package-assets.csv"), document, "Missing");
            WriteFilteredCsv(Path.Combine(outputFolder, "ambiguous-package-assets.csv"), document, "Ambiguous");
            WriteMarkdown(Path.Combine(outputFolder, "SHIP-PACKAGE-PLANNING-REPORT.md"), document);

            var summary = document.Summary;
            Console.WriteLine($"Ships:                       {summary.ShipCount}");
            Console.WriteLine($"Pilots / packages:           {summary.PackageCount}");
            Console.WriteLine($"Ready:                       {summary.ReadyCount}");
            Console.WriteLine($"Ready, optional missing:     {summary.ReadyWithOptionalAssetsMissingCount}");
            Console.WriteLine($"Unresolved required assets:  {summary.UnresolvedRequiredAssetsCount}");
            Console.WriteLine($"Ambiguous required assets:   {summary.AmbiguousRequiredAssetsCount}");
            Console.WriteLine($"Invalid semantic data:       {summary.InvalidSemanticDataCount}");
            Console.WriteLine($"Required roles resolved:     {summary.ResolvedRequiredRoleCount}/{summary.RequiredRoleCount}");
            Console.WriteLine();
            Console.WriteLine($"Plan:                         {Path.Combine(outputFolder, "ship-package-plans.json")}");
            Console.WriteLine($"Report:                       {Path.Combine(outputFolder, "SHIP-PACKAGE-PLANNING-REPORT.md")}");
            Console.WriteLine();
            Console.WriteLine("This command plans and validates packages only. It does not generate TTS objects.");

            return summary.InvalidSemanticDataCount > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ship package planning failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteJson(string path, FirstEditionShipPackagePlanDocument document) =>
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));

    private static void WriteResolutionCsv(string path, FirstEditionShipPackagePlanDocument document)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("PackageId,Faction,ShipId,ShipName,PilotId,PilotName,BaseSize,PackageStatus,Role,Required,ResolutionStatus,ResolutionSource,SelectedAssetId,SelectedWarehouse,SelectedRepositoryPath,CandidateCount,Note");
        foreach (var package in document.Packages)
        foreach (var requirement in package.Requirements)
            WriteRow(writer, package, requirement);
    }

    private static void WriteFilteredCsv(string path, FirstEditionShipPackagePlanDocument document, string status)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("PackageId,Faction,ShipId,ShipName,PilotId,PilotName,BaseSize,PackageStatus,Role,Required,ResolutionStatus,ResolutionSource,SelectedAssetId,SelectedWarehouse,SelectedRepositoryPath,CandidateCount,Note");
        foreach (var package in document.Packages)
        foreach (var requirement in package.Requirements.Where(x => x.Required && x.ResolutionStatus.Equals(status, StringComparison.OrdinalIgnoreCase)))
            WriteRow(writer, package, requirement);
    }

    private static void WriteRow(StreamWriter writer, FirstEditionShipPackagePlan package, FirstEditionShipPackageRequirement requirement)
    {
        writer.WriteLine(string.Join(',', new[]
        {
            package.PackageId, package.Faction, package.ShipId, package.ShipName, package.PilotId, package.PilotName,
            package.BaseSize, package.PackageStatus, requirement.Role, requirement.Required.ToString(),
            requirement.ResolutionStatus, requirement.ResolutionSource, requirement.SelectedAsset?.AssetId ?? "",
            requirement.SelectedAsset?.Warehouse ?? "", requirement.SelectedAsset?.RepositoryPath ?? "",
            requirement.Candidates.Count.ToString(), requirement.Note
        }.Select(Csv)));
    }

    private static void WriteMarkdown(string path, FirstEditionShipPackagePlanDocument document)
    {
        var s = document.Summary;
        var lines = new List<string>
        {
            "# Phase 11A – First Edition Ship Package Planning Report", "",
            $"- Generated: {document.GeneratedUtc:O}",
            $"- Mapping version: {document.MappingVersion}",
            $"- Ships: **{s.ShipCount}**",
            $"- Pilots / packages: **{s.PackageCount}**", "",
            "## Package status", "",
            $"- Ready: **{s.ReadyCount}**",
            $"- Ready with optional assets missing: **{s.ReadyWithOptionalAssetsMissingCount}**",
            $"- Unresolved required assets: **{s.UnresolvedRequiredAssetsCount}**",
            $"- Ambiguous required assets: **{s.AmbiguousRequiredAssetsCount}**",
            $"- Invalid semantic data: **{s.InvalidSemanticDataCount}**", "",
            "## Required role coverage", "",
            $"- Required roles: **{s.RequiredRoleCount}**",
            $"- Resolved: **{s.ResolvedRequiredRoleCount}**",
            $"- Ambiguous: **{s.AmbiguousRequiredRoleCount}**",
            $"- Missing: **{s.MissingRequiredRoleCount}**", "",
            "## First Edition base validation", ""
        };

        var invalidBases = document.Packages.Where(x => x.ValidationErrors.Count > 0).ToList();
        lines.Add(invalidBases.Count == 0
            ? "All packages use Small, Large or Epic bases. No Medium bases were accepted."
            : $"**{invalidBases.Count} package(s) failed semantic/base validation.**");

        lines.AddRange(new[] { "", "## Packages requiring attention", "" });
        foreach (var package in document.Packages.Where(x => x.PackageStatus != ShipPackageStatuses.Ready).Take(200))
        {
            var problems = package.Requirements
                .Where(x => x.Required && x.ResolutionStatus != "Resolved")
                .Select(x => $"{x.Role}: {x.ResolutionStatus}")
                .Concat(package.ValidationErrors)
                .ToList();
            lines.Add($"- `{package.PackageId}` — {package.PilotName} / {package.ShipName}: {package.PackageStatus}{(problems.Count == 0 ? "" : " — " + string.Join("; ", problems))}");
        }
        lines.AddRange(new[] { "", "This phase creates canonical package plans only. No Tabletop Simulator objects are generated or modified.", "" });
        File.WriteAllLines(path, lines);
    }

    private static string ResolveMappingFolder(string[] args)
    {
        var positional = args.Where((value, index) =>
                !value.StartsWith("--", StringComparison.Ordinal)
                && (index == 0 || !args[index - 1].Equals("--output", StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault();
        return positional is null
            ? Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition")
            : Path.GetFullPath(positional);
    }

    private static string? ResolveOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static FirstEditionRepositoryBuildResult LoadSemanticRepository(
        string repositoryRoot,
        string mappingFolder,
        bool allowSourceErrors)
    {
        var databasePath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "conversion",
            "first-edition-database.json");

        if (File.Exists(databasePath))
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var database = JsonSerializer.Deserialize<FirstEditionDatabaseDocument>(
                File.ReadAllText(databasePath),
                options) ?? throw new InvalidDataException(
                    $"Could not deserialize the First Edition semantic database: {databasePath}");

            var repository = new FirstEditionRepository(
                database.Ships,
                database.Pilots,
                database.Upgrades);

            Console.WriteLine($"Semantic source:   {databasePath}");
            Console.WriteLine();

            return new FirstEditionRepositoryBuildResult
            {
                Repository = repository,
                MappingVersion = string.IsNullOrWhiteSpace(database.MappingVersion)
                    ? "exported"
                    : database.MappingVersion,
                SourceValidationErrorCount = 0
            };
        }

        var unified25RepositoryRoot = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified25");

        var shipDatabasePath = Path.Combine(
            unified25RepositoryRoot,
            "TTS_xwing",
            "src",
            "Game",
            "Component",
            "Spawner",
            "ShipDb.lua");

        if (!File.Exists(shipDatabasePath))
        {
            throw new FileNotFoundException(
                "The validated First Edition database export was not found, and the Unified 2.5 source " +
                $"repository could not be located. Expected ShipDb.lua at: {shipDatabasePath}",
                shipDatabasePath);
        }

        Console.WriteLine($"Semantic source:   Unified 2.5 source + First Edition mappings");
        Console.WriteLine($"Source repository: {unified25RepositoryRoot}");
        Console.WriteLine();

        return FirstEditionRepositoryBuilder.Build(
            unified25RepositoryRoot,
            mappingFolder,
            allowSourceErrors);
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
