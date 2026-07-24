using UnifiedToolkit.KnowledgeBase.AssetExtraction;

namespace UnifiedToolkit.Commands;

public static class ImportAssetsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  import-assets <first-edition-repo-folder> <profile>");
            Console.WriteLine();
            Console.WriteLine("Profiles:");
            foreach (var importer in AssetImportCoordinator.CreateDefault().Importers)
                Console.WriteLine($"  {importer.Profile,-24} {importer.Description}");
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var profile = args[1];
            var coordinator = AssetImportCoordinator.CreateDefault();
            var importer = coordinator.Importers.FirstOrDefault(value => value.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase));

            Console.WriteLine("UnifiedToolkit Generic Asset Import");
            Console.WriteLine("===================================");
            Console.WriteLine();
            Console.WriteLine($"Repository: {repository}");
            Console.WriteLine($"Profile:    {profile}");
            if (importer is not null) Console.WriteLine($"Purpose:    {importer.Description}");
            Console.WriteLine();

            var result = coordinator.Import(repository, profile);
            Console.WriteLine($"Assets scanned:  {result.Scanned}");
            Console.WriteLine($"Imported/linked: {result.Imported}");
            Console.WriteLine($"Updated:         {result.Updated}");
            Console.WriteLine($"Already linked:  {result.AlreadyLinked}");
            Console.WriteLine($"Warnings:        {result.Warnings}");
            Console.WriteLine($"Errors:          {result.Errors}");
            Console.WriteLine();
            if (!string.IsNullOrWhiteSpace(result.ManifestFile)) Console.WriteLine($"Manifest:        {result.ManifestFile}");
            if (!string.IsNullOrWhiteSpace(result.ReportFile)) Console.WriteLine($"Report:          {result.ReportFile}");
            if (!string.IsNullOrWhiteSpace(result.AssetManifestRoot)) Console.WriteLine($"Asset register:  {result.AssetManifestRoot}");
            if (!string.IsNullOrWhiteSpace(result.KnowledgeBaseRoot)) Console.WriteLine($"Knowledge base:  {result.KnowledgeBaseRoot}");
            Console.WriteLine();
            Console.WriteLine(result.Errors == 0
                ? $"Asset import profile '{result.Profile}' completed successfully."
                : $"Asset import profile '{result.Profile}' completed with errors.");
            return result.Errors == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }
}
