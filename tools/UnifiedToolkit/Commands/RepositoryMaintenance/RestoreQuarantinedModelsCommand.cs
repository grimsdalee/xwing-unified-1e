using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class RestoreQuarantinedModelsCommand
{
    public static int Run(string[] args)
    {
        try
        {
            var (root, audit) = ObsoleteModelCommandSupport.Resolve(args);
            var service = new ObsoleteModelMaintenanceService();
            var manifest = service.Restore(root, audit);
            service.WriteReports(root, manifest);

            Console.WriteLine("UnifiedToolkit Obsolete Model Restore");
            Console.WriteLine("=====================================");
            ObsoleteModelCommandSupport.PrintSummary(manifest);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Obsolete-model restore failed: {ex.Message}");
            return 1;
        }
    }
}
