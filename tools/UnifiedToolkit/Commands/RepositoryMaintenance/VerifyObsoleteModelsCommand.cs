using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class VerifyObsoleteModelsCommand
{
    public static int Run(string[] args)
    {
        try
        {
            var (root, audit) = ObsoleteModelCommandSupport.Resolve(args);
            var service = new ObsoleteModelMaintenanceService();
            var manifest = service.Verify(root, audit, "Verify");
            service.WriteReports(root, manifest);

            Console.WriteLine("UnifiedToolkit Obsolete Model Verification");
            Console.WriteLine("==========================================");
            ObsoleteModelCommandSupport.PrintSummary(manifest);
            Console.WriteLine();
            Console.WriteLine("No files were moved or deleted.");
            return manifest.MissingReplacement > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Obsolete-model verification failed: {ex.Message}");
            return 1;
        }
    }
}
