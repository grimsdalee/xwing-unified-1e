using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Commands;

public static class ApproveShipMappingsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        var proposedPath = Path.GetFullPath(args[0]);
        var apply = args.Any(argument =>
            argument.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        var targetVersion = ReadOption(args, "--version") ?? "0.3.0";
        var mappingFolderArgument = args
            .Skip(1)
            .FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
        var mappingFolder = mappingFolderArgument is not null
            ? Path.GetFullPath(mappingFolderArgument)
            : ResolveDefaultSourceMappingFolder();

        try
        {
            var result = apply
                ? ShipMappingApprovalService.Apply(proposedPath, mappingFolder, targetVersion)
                : ShipMappingApprovalService.Preview(proposedPath, mappingFolder, targetVersion);

            Console.WriteLine("UnifiedToolkit Ship Mapping Approval");
            Console.WriteLine("====================================");
            Console.WriteLine();
            Console.WriteLine($"Proposed mappings:   {result.ProposedMappingsPath}");
            Console.WriteLine($"Mapping folder:      {result.MappingFolder}");
            Console.WriteLine($"Current version:     {result.CurrentVersion}");
            Console.WriteLine($"Target version:      {result.TargetVersion}");
            Console.WriteLine();
            Console.WriteLine($"Existing mappings:   {result.ExistingCount}");
            Console.WriteLine($"Proposed mappings:   {result.ProposedCount}");
            Console.WriteLine($"New mappings:        {result.AddedCount}");
            Console.WriteLine($"Unchanged mappings:  {result.UnchangedCount}");
            Console.WriteLine($"Merged total:        {result.MergedMappings.Count}");
            Console.WriteLine($"Validation errors:   {result.ValidationIssues.Count(issue => issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))}");
            Console.WriteLine($"Validation warnings: {result.ValidationIssues.Count(issue => issue.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase))}");

            foreach (var issue in result.ValidationIssues.Take(20))
                Console.WriteLine($"  {issue.Severity}: {issue.Code} - {issue.Message}");

            if (result.ValidationIssues.Count > 20)
                Console.WriteLine($"  ...and {result.ValidationIssues.Count - 20} more issues.");

            Console.WriteLine();
            if (result.Applied)
            {
                Console.WriteLine("Mappings applied successfully.");
                Console.WriteLine($"Backup folder:       {result.BackupFolder}");
            }
            else if (apply)
            {
                Console.WriteLine("Mappings were not applied because validation errors were found.");
            }
            else
            {
                Console.WriteLine("Preview only. No files were changed.");
                Console.WriteLine("Add --apply after reviewing the preview.");
            }

            return result.ValidationIssues.Any(issue =>
                issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Ship mapping approval failed: {exception.Message}");
            return 1;
        }
    }

    private static string ResolveDefaultSourceMappingFolder()
    {
        var sourceFolder = Path.Combine(
            Directory.GetCurrentDirectory(),
            "ConversionData",
            "first-edition");

        if (Directory.Exists(sourceFolder))
            return Path.GetFullPath(sourceFolder);

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "ConversionData",
            "first-edition"));
    }

    private static string? ReadOption(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  UnifiedToolkit approve-ship-mappings <ships.proposed.json> [mapping-folder] [--version <version>] [--apply]");
    }
}
