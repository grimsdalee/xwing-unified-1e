using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

internal static class ObsoleteModelCommandSupport
{
    public static (string RepositoryRoot, string AuditPath) Resolve(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("A repository folder is required.");

        var repositoryRoot = Path.GetFullPath(args[0]);
        if (!Directory.Exists(repositoryRoot))
            throw new DirectoryNotFoundException(repositoryRoot);

        var auditPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "model-selection",
            "ship-model-selection-audit.json");

        var auditIndex = Array.FindIndex(args, value =>
            value.Equals("--audit", StringComparison.OrdinalIgnoreCase));
        if (auditIndex >= 0)
        {
            if (auditIndex + 1 >= args.Length)
                throw new ArgumentException("--audit requires a file path.");
            auditPath = Path.GetFullPath(args[auditIndex + 1]);
        }

        if (!File.Exists(auditPath))
            throw new FileNotFoundException("Model-selection audit was not found.", auditPath);

        return (repositoryRoot, auditPath);
    }

    public static void PrintSummary(ObsoleteModelVerificationManifest manifest)
    {
        Console.WriteLine($"Entries scanned:       {manifest.EntriesScanned}");
        Console.WriteLine($"Verified unused:       {manifest.VerifiedUnused}");
        Console.WriteLine($"Blocked:               {manifest.Blocked}");
        Console.WriteLine($"Missing original:      {manifest.MissingOriginal}");
        Console.WriteLine($"Missing replacement:   {manifest.MissingReplacement}");
        Console.WriteLine($"Quarantined:           {manifest.Quarantined}");
        Console.WriteLine($"Restored:              {manifest.Restored}");
        Console.WriteLine($"Purged:                {manifest.Purged}");
    }
}
