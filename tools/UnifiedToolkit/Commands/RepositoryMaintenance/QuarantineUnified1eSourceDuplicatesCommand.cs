using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class QuarantineUnified1eSourceDuplicatesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "Usage: quarantine-unified1e-source-duplicates " +
                "<first-edition-repo-folder>");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var audit = Unified1eSourceDuplicateAuditService.Quarantine(
                repositoryRoot);
            Unified1eSourceDuplicateAuditService.WriteReports(
                repositoryRoot,
                audit);

            var quarantined = audit.Entries.Count(entry =>
                entry.Status == Unified1eSourceDuplicateStatuses.Quarantined);

            Console.WriteLine(
                "UnifiedToolkit Unified 1E Source Duplicate Quarantine");
            Console.WriteLine(
                "=====================================================");
            Console.WriteLine($"Repository:             {repositoryRoot}");
            Console.WriteLine();
            Console.WriteLine($"Files scanned:          {audit.FilesScanned}");
            Console.WriteLine($"Quarantined:            {quarantined}");
            Console.WriteLine($"Blocked by references:  {audit.BlockedByReferences}");
            Console.WriteLine($"No Unified1e duplicate: {audit.NoUnified1eDuplicate}");
            Console.WriteLine();
            Console.WriteLine(
                "Only byte-identical, unreferenced old-source files were " +
                "moved. Nothing was deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Source duplicate quarantine failed: {ex.Message}");
            return 1;
        }
    }
}
