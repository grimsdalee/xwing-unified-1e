using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.FirstEdition.Export;
using UnifiedToolkit.Conversion.FirstEdition.Validation;
using UnifiedToolkit.Reports;

namespace UnifiedToolkit.Commands;

public static class FirstEditionRepositoryCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: UnifiedToolkit first-edition-repository <repo-folder> [mapping-folder] [--allow-source-errors] [--output <json-file>]");
            return 1;
        }

        try
        {
            var repositoryFolder = Path.GetFullPath(args[0]);
            var allowSourceErrors = args.Any(x => x.Equals("--allow-source-errors", StringComparison.OrdinalIgnoreCase));
            var mappingFolder = ResolveMappingFolder(args.Skip(1).ToArray());
            var reportFolder = Path.Combine(repositoryFolder, "_unifiedtoolkit_reports", "conversion");
            var outputPath = ResolveOutputPath(args, Path.Combine(reportFolder, "first-edition-database.json"));
            var validationPath = Path.Combine(reportFolder, "first-edition-database-validation.csv");

            Console.WriteLine("UnifiedToolkit First Edition Repository");
            Console.WriteLine("=======================================");
            Console.WriteLine();
            Console.WriteLine($"Repo folder:       {repositoryFolder}");
            Console.WriteLine($"Mapping folder:    {mappingFolder}");
            Console.WriteLine();

            var build = FirstEditionRepositoryBuilder.Build(repositoryFolder, mappingFolder, allowSourceErrors);
            var validation = FirstEditionRepositoryValidator.Validate(build.Repository);
            FirstEditionDatabaseExporter.Write(build.Repository, build.MappingVersion, outputPath);
            ConversionIssuesReport.Write(validation, validationPath);

            var errors = validation.Count(x => x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
            var warnings = validation.Count(x => x.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"Mapping version:    {build.MappingVersion}");
            Console.WriteLine($"Source errors:      {build.SourceValidationErrorCount}");
            Console.WriteLine($"Ships:              {build.Repository.Ships.Count}");
            Console.WriteLine($"Pilots:             {build.Repository.Pilots.Count}");
            Console.WriteLine($"Upgrades:           {build.Repository.Upgrades.Count}");
            Console.WriteLine($"Validation errors:  {errors}");
            Console.WriteLine($"Validation warnings:{warnings,4}");
            Console.WriteLine($"Database export:    {Path.GetFullPath(outputPath)}");
            Console.WriteLine($"Validation report:  {validationPath}");

            return errors > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"First Edition repository export failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveMappingFolder(string[] args)
    {
        var positional = args
            .Where((value, index) =>
                !value.StartsWith("--", StringComparison.Ordinal) &&
                (index == 0 || !args[index - 1].Equals("--output", StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault();

        return positional is null
            ? Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition")
            : Path.GetFullPath(positional);
    }

    private static string ResolveOutputPath(string[] args, string defaultPath)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[i + 1]);
        }
        return defaultPath;
    }
}
