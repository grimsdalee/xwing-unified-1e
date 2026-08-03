using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class AuditUnified1eSourceDuplicatesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "Usage: audit-unified1e-source-duplicates " +
                "<first-edition-repo-folder>");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var audit = Unified1eSourceDuplicateAuditService.Audit(
                repositoryRoot);
            Unified1eSourceDuplicateAuditService.WriteReports(
                repositoryRoot,
                audit);

            Console.WriteLine("UnifiedToolkit Unified 1E Source Duplicate Audit");
            Console.WriteLine("================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine();
            Console.WriteLine($"Files scanned:          {audit.FilesScanned}");
            Console.WriteLine($"Exact duplicates:       {audit.ExactDuplicates}");
            Console.WriteLine($"Ready to quarantine:    {audit.ReadyToQuarantine}");
            Console.WriteLine($"Blocked by references:  {audit.BlockedByReferences}");
            Console.WriteLine($"No Unified1e duplicate: {audit.NoUnified1eDuplicate}");
            Console.WriteLine($"Ready bytes:            {audit.ReadyBytes}");
            Console.WriteLine();
            Console.WriteLine(
                "Audit only. No files were moved or deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Source duplicate audit failed: {ex.Message}");
            return 1;
        }
    }
}
