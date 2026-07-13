using UnifiedToolkit.Conversion;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class InspectMappingCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        var repoFolder = Path.GetFullPath(args[0]);
        var sourceId = args[1];
        var mappingFolder = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");

        if (!Directory.Exists(repoFolder))
        {
            Console.Error.WriteLine($"Repo folder not found: {repoFolder}");
            return 1;
        }

        if (!Directory.Exists(mappingFolder))
        {
            Console.Error.WriteLine($"Mapping folder not found: {mappingFolder}");
            return 1;
        }

        try
        {
            var repository = RepositoryLoader.Load(repoFolder);
            var source = repository.FindShip(sourceId);
            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var mapping = mappings.Ships.FirstOrDefault(x =>
                x.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase));

            Console.WriteLine("UnifiedToolkit Ship Mapping Inspection");
            Console.WriteLine("======================================");
            Console.WriteLine();
            Console.WriteLine($"Mapping version: {mappings.Version}");
            Console.WriteLine($"Source ID:       {sourceId}");

            if (source is null)
            {
                Console.WriteLine("Source status:   Not found in repository");
                return 2;
            }

            Console.WriteLine($"Source name:     {source.Name}");
            Console.WriteLine($"Source size:     {source.Size}");
            Console.WriteLine($"Source hull:     {source.Hull}");
            Console.WriteLine($"Source shields:  {source.Shield}");
            Console.WriteLine($"Source agility:  {source.Agility}");
            Console.WriteLine($"Source factions: {string.Join(", ", source.Factions)}");
            Console.WriteLine();

            if (mapping is null)
            {
                Console.WriteLine("Mapping status:  Unmapped");
                return 0;
            }

            Console.WriteLine($"Mapping status:  {(mapping.Kind == ConversionKind.Excluded ? "Excluded" : "Converted")}");
            Console.WriteLine($"Mapping ID:      {mapping.MappingId}");
            Console.WriteLine($"Kind:            {mapping.Kind}");

            if (mapping.Kind == ConversionKind.Excluded)
            {
                Console.WriteLine($"Reason:          {mapping.ExclusionReason}");
                return 0;
            }

            Console.WriteLine($"Target ID:       {mapping.TargetId}");
            Console.WriteLine($"Target name:     {mapping.Name}");
            Console.WriteLine($"Target size:     {mapping.Size}");
            Console.WriteLine($"Target stats:    Attack {mapping.Attack}, Agility {mapping.Agility}, Hull {mapping.Hull}, Shields {mapping.Shields}");
            Console.WriteLine($"Target factions: {string.Join(", ", mapping.Factions)}");
            Console.WriteLine($"Target actions:  {string.Join(", ", mapping.Actions)}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Mapping inspection failed: {exception.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  UnifiedToolkit inspect-mapping <repo-folder> <source-ship-id> [mapping-folder]");
    }
}
