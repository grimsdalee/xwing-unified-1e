using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class AuditShipModelInventoryCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "Usage: audit-ship-model-inventory <first-edition-repo-folder> " +
                "[--output <folder>]");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);

            var service = new ShipModelInventoryService();
            var manifest = service.Audit(repositoryRoot);
            service.WriteReports(outputFolder, manifest);

            Console.WriteLine("UnifiedToolkit Ship Model Inventory Audit");
            Console.WriteLine("=========================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine("Included folders:       small, medium, large");
            Console.WriteLine("Excluded folders:       huge (2.5 Huge = 1E Epic)");
            Console.WriteLine();
            Console.WriteLine($"OBJ files scanned:      {manifest.ObjFilesScanned}");
            Console.WriteLine($"Used primary:           {manifest.UsedPrimary}");
            Console.WriteLine($"Used multipart:         {manifest.UsedMultipart}");
            Console.WriteLine($"Used configured:        {manifest.UsedConfigured}");
            Console.WriteLine($"Review candidates:      {manifest.ReviewCandidates}");
            Console.WriteLine($"Duplicate hash groups:  {manifest.DuplicateHashGroups}");
            Console.WriteLine($"Missing configured OBJ: {manifest.MissingConfiguredModels.Count}");
            Console.WriteLine($"Multipart errors:       {manifest.MultipartErrors.Count}");
            Console.WriteLine();
            Console.WriteLine($"Reports folder:         {outputFolder}");
            Console.WriteLine();
            Console.WriteLine("No files were moved or deleted.");

            return manifest.MissingConfiguredModels.Count == 0
                   && manifest.MultipartErrors.Count == 0
                ? 0
                : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Ship-model inventory audit failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveOutputFolder(
        string repositoryRoot,
        IReadOnlyList<string> args)
    {
        for (var index = 1; index < args.Count; index++)
        {
            if (!args[index].Equals("--output", StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= args.Count)
                throw new ArgumentException("--output requires a folder path.");

            return Path.GetFullPath(args[index + 1]);
        }

        return Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "model-inventory");
    }
}
