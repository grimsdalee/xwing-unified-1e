using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class PurgeQuarantinedModelsCommand
{
    public static int Run(string[] args)
    {
        try
        {
            if (!args.Any(value => value.Equals("--confirm-purge", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine(
                    "Permanent deletion requires the explicit --confirm-purge option.");
                return 1;
            }

            var (root, audit) = ObsoleteModelCommandSupport.Resolve(args);
            var service = new ObsoleteModelMaintenanceService();
            var manifest = service.Purge(root, audit);
            service.WriteReports(root, manifest);

            Console.WriteLine("UnifiedToolkit Quarantined Model Purge");
            Console.WriteLine("=====================================");
            ObsoleteModelCommandSupport.PrintSummary(manifest);
            Console.WriteLine();
            Console.WriteLine("Quarantined OBJ files were permanently deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Quarantined-model purge failed: {ex.Message}");
            return 1;
        }
    }
}
