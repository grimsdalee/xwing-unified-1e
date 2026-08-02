using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class AuditPrototypeAssetDependenciesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            if (!Directory.Exists(repositoryRoot))
                throw new DirectoryNotFoundException(repositoryRoot);

            var referenceSave = ResolveOption(
                args,
                "--reference-save",
                Path.Combine(
                    repositoryRoot,
                    "source", "unified-2.5", "2486128992.json"));
            if (!File.Exists(referenceSave))
                throw new FileNotFoundException(
                    "Reference save was not found.", referenceSave);

            var outputFolder = ResolveOption(
                args,
                "--output",
                Path.Combine(
                    repositoryRoot,
                    "_unifiedtoolkit_reports", "phase13",
                    "prototype-asset-dependencies"));

            var service = new PrototypeAssetDependencyAuditService();
            var audit = service.Run(repositoryRoot, referenceSave);
            PrototypeAssetDependencyAuditService.WriteReports(outputFolder, audit);

            Console.WriteLine("UnifiedToolkit Effective Prototype Asset Dependency Audit");
            Console.WriteLine("==========================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine($"Reference save:         {referenceSave}");
            Console.WriteLine($"Scan mode:              {audit.ScanMode}");
            Console.WriteLine($"Files scanned:          {audit.FilesScanned}");
            Console.WriteLine($"Unique dependencies:    {audit.UniqueDependencies}");
            Console.WriteLine($"Environment:            {audit.EnvironmentDependencies}");
            Console.WriteLine($"Runtime:                {audit.RuntimeDependencies}");
            Console.WriteLine($"Ship:                   {audit.ShipDependencies}");
            Console.WriteLine($"Supporting:             {audit.SupportingDependencies}");
            Console.WriteLine($"Already migrated:       {audit.AlreadyMigrated}");
            Console.WriteLine($"Unified25 dependencies: {audit.Unified25Dependencies}");
            Console.WriteLine($"Repository dependencies:{audit.RepositoryDependencies,3}");
            Console.WriteLine($"Upstream dependencies:  {audit.UpstreamDependencies}");
            Console.WriteLine($"External dependencies:  {audit.ExternalDependencies}");
            Console.WriteLine($"Missing repo files:     {audit.MissingRepositoryFiles}");
            Console.WriteLine($"Warnings:               {audit.ScanWarnings.Count}");
            Console.WriteLine($"Reports folder:         {outputFolder}");
            Console.WriteLine();
            Console.WriteLine(
                "Audit only. Embedded Lua/state text and historical reference-save objects were not scanned.");

            return audit.ScanWarnings.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Prototype asset dependency audit failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveOption(
        IReadOnlyList<string> args,
        string option,
        string fallback)
    {
        for (var i = 1; i < args.Count - 1; i++)
        {
            if (args[i].Equals(option, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[i + 1]);
        }
        return Path.GetFullPath(fallback);
    }

    private static void ShowUsage() => Console.Error.WriteLine(
        "Usage: audit-prototype-asset-dependencies " +
        "<first-edition-repo-folder> " +
        "[--reference-save <file>] [--output <folder>]");
}
