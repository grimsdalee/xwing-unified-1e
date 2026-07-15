using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Conversion.FirstEdition.Export;

public static class FirstEditionDatabaseExporter
{
    public static void Write(FirstEditionRepository repository, string mappingVersion, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var document = new FirstEditionDatabaseDocument
        {
            MappingVersion = mappingVersion,
            GeneratedUtc = DateTimeOffset.UtcNow,
            Summary = new FirstEditionDatabaseSummary
            {
                ShipCount = repository.Ships.Count,
                PilotCount = repository.Pilots.Count,
                UpgradeCount = repository.Upgrades.Count,
                ShipsByFaction = repository.Ships
                    .SelectMany(ship => ship.Factions.Distinct(StringComparer.OrdinalIgnoreCase))
                    .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase),
                PilotsByFaction = repository.Pilots
                    .GroupBy(x => x.Faction, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase),
                UpgradesBySlot = repository.Upgrades
                    .GroupBy(x => x.Slot, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase)
            },
            Ships = repository.Ships,
            Pilots = repository.Pilots,
            Upgrades = repository.Upgrades
        };

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(fullPath, JsonSerializer.Serialize(document, options));
    }
}
