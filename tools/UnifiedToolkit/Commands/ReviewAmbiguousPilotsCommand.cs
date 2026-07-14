using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Pilots;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Commands;

public static class ReviewAmbiguousPilotsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: UnifiedToolkit review-ambiguous-pilots <repo-folder> <xwing-data-folder> [mapping-folder]");
            return 1;
        }
        try
        {
            var repoFolder = Path.GetFullPath(args[0]);
            var dataFolder = Path.GetFullPath(args[1]);
            var mappingFolder = args.Length >= 3 ? Path.GetFullPath(args[2]) : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
            var repository = RepositoryLoader.Load(repoFolder);
            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var officialPilots = FirstEditionDataLoader.LoadPilots(dataFolder);
            var shipMappings = mappings.Ships.ToDictionary(x => x.SourceId, x => x.TargetId, StringComparer.OrdinalIgnoreCase);
            var reviews = new List<object>();

            foreach (var disposition in mappings.PilotDispositions.Where(x => x.Kind == PilotDispositionKind.Ambiguous))
            {
                var source = repository.Pilots.First(x => x.Id.Equals(disposition.SourceId, StringComparison.OrdinalIgnoreCase));
                var sourceShipId = source.Ship?.Id ?? source.ShipType;
                shipMappings.TryGetValue(sourceShipId, out var targetShipId);
                var candidates = officialPilots
                    .Where(x => x.ShipId.Equals(targetShipId, StringComparison.OrdinalIgnoreCase) && Normalise(x.Name) == Normalise(source.Name))
                    .Select(x => new AmbiguousPilotCandidate
                    {
                        Id = x.Id,
                        Name = x.Name,
                        ShipId = x.ShipId,
                        Faction = x.Faction,
                        PilotSkill = x.PilotSkill,
                        SquadPointCost = x.SquadPointCost,
                        Unique = x.Unique,
                        UpgradeSlots = x.UpgradeSlots.ToArray()
                    }).ToList();

                reviews.Add(new
                {
                    sourceId = source.Id,
                    sourceName = source.Name,
                    sourceShipId,
                    sourceFaction = source.Faction,
                    targetShipId,
                    decision = AmbiguousPilotResolutionDecision.Unreviewed,
                    selectedTargetId = "",
                    disposition = (PilotDispositionKind?)null,
                    reason = "",
                    candidates
                });
            }

            var reportFolder = Path.Combine(repoFolder, "_unifiedtoolkit_reports", "conversion");
            Directory.CreateDirectory(reportFolder);
            var path = Path.Combine(reportFolder, "ambiguous-pilot-resolutions.review.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            File.WriteAllText(path, JsonSerializer.Serialize(reviews, options) + Environment.NewLine);
            Console.WriteLine("UnifiedToolkit Ambiguous Pilot Review");
            Console.WriteLine("====================================");
            Console.WriteLine();
            Console.WriteLine($"Ambiguous source pilots: {reviews.Count}");
            Console.WriteLine($"Editable review:         {path}");
            Console.WriteLine();
            Console.WriteLine("Set decision to Map or Disposition, select a valid target where required, and provide a reason.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ambiguous pilot review failed: {ex.Message}");
            return 1;
        }
    }

    private static string Normalise(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
