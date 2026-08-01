using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class QuarantineObsoleteModelsCommand
{
    public static int Run(string[] args)
    {
        try
        {
            var (root, audit) = ObsoleteModelCommandSupport.Resolve(args);
            var service = new ObsoleteModelMaintenanceService();
            var manifest = service.Verify(root, audit, "Quarantine");
            service.Quarantine(root, manifest);
            service.WriteReports(root, manifest);

            Console.WriteLine("UnifiedToolkit Obsolete Model Quarantine");
            Console.WriteLine("=========================================");
            ObsoleteModelCommandSupport.PrintSummary(manifest);
            Console.WriteLine();
            Console.WriteLine("Only verified-unused OBJ files were moved. Nothing was deleted.");
            return manifest.MissingReplacement > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Obsolete-model quarantine failed: {ex.Message}");
            return 1;
        }
    }
}
