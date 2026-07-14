using System.Text.Json;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class ReviewAmbiguousUpgradesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("Usage: UnifiedToolkit review-ambiguous-upgrades <repo-folder> <xwing-data-folder> [mapping-folder]"); return 1; }
        var repoFolder = Path.GetFullPath(args[0]);
        var dataFolder = Path.GetFullPath(args[1]);
        var mappingFolder = args.Length > 2 ? Path.GetFullPath(args[2]) : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
        try
        {
            var repository = RepositoryLoader.Load(repoFolder);
            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var official = FirstEditionDataLoader.LoadUpgrades(dataFolder);
            var sourceById = repository.Upgrades.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var rows = mappings.UpgradeDispositions.Where(x => x.Kind.ToString() == "Ambiguous").Select(d =>
            {
                var source = sourceById[d.SourceId];
                var token = Token(source.Name);
                return new
                {
                    sourceId = source.Id,
                    sourceName = source.Name,
                    sourceSlot = source.Slot,
                    decision = "Unreviewed",
                    selectedTargetId = "",
                    reason = "",
                    candidates = official.Where(x => Token(x.Name) == token).Select(x => new { x.Id, x.Name, x.Slot, x.SquadPointCost, x.Unique, x.Factions, x.ShipRestrictions, x.SizeRestrictions, x.Text }).ToList()
                };
            }).ToList();
            var folder = Path.Combine(repoFolder, "_unifiedtoolkit_reports", "conversion");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "ambiguous-upgrade-resolutions.review.json");
            File.WriteAllText(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("UnifiedToolkit Ambiguous Upgrade Review");
            Console.WriteLine("=======================================");
            Console.WriteLine();
            Console.WriteLine($"Ambiguous source upgrades: {rows.Count}");
            Console.WriteLine($"Editable review:           {path}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Ambiguous upgrade review failed: {ex.Message}"); return 1; }
    }
    private static string Token(string value) => new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
