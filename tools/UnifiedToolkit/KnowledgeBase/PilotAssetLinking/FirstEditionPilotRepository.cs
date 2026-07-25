using System.Text.Json;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

/// <summary>
/// Loads the complete First Edition pilot domain used by UKB linking.
///
/// Mapped pilots are stored in pilots.json. Native First Edition pilots that
/// have no Unified 2.5 source are stored in official-pilots.json. Consumers
/// must use the merged collection rather than reading either file directly.
/// </summary>
public sealed class FirstEditionPilotRepository
{
    public IReadOnlyList<FirstEditionPilotRecord> Load(
        string mappedPilotsFile,
        string officialPilotsFile)
    {
        if (string.IsNullOrWhiteSpace(mappedPilotsFile))
            throw new ArgumentException("Mapped pilots file is required.", nameof(mappedPilotsFile));

        if (!File.Exists(mappedPilotsFile))
            throw new FileNotFoundException("Mapped First Edition pilots file was not found.", mappedPilotsFile);

        var mapped = ShipAssetJson.Read<List<FirstEditionPilotRecord>>(mappedPilotsFile);
        var official = File.Exists(officialPilotsFile)
            ? ReadOfficialPilots(officialPilotsFile)
            : new List<FirstEditionPilotRecord>();

        var merged = new Dictionary<string, FirstEditionPilotRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var pilot in mapped)
            Add(merged, pilot, mappedPilotsFile);

        foreach (var pilot in official)
            Add(merged, pilot, officialPilotsFile);

        return merged.Values
            .OrderBy(pilot => pilot.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pilot => pilot.ShipId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pilot => pilot.Faction, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<FirstEditionPilotRecord> ReadOfficialPilots(string path)
    {
        var json = File.ReadAllText(path);
        var records = JsonSerializer.Deserialize<List<OfficialFirstEditionPilotRecord>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (records is null)
            throw new InvalidDataException($"Could not parse official First Edition pilots: {path}");

        return records.Select(record => new FirstEditionPilotRecord
        {
            MappingId = record.ImportId,
            SourceId = string.Empty,
            TargetId = record.Id,
            Name = record.Name,
            ShipId = record.ShipId,
            Faction = record.Faction,
            PilotSkill = record.PilotSkill,
            SquadPointCost = record.SquadPointCost,
            Unique = record.Unique
        }).ToList();
    }

    private static void Add(
        IDictionary<string, FirstEditionPilotRecord> merged,
        FirstEditionPilotRecord pilot,
        string sourceFile)
    {
        Validate(pilot, sourceFile);

        var key = BuildIdentityKey(pilot);
        if (merged.TryGetValue(key, out var existing))
        {
            if (Equivalent(existing, pilot))
                return;

            throw new InvalidDataException(
                $"Pilot identity '{pilot.TargetId}' for ship '{pilot.ShipId}' and faction " +
                $"'{pilot.Faction}' is defined more than once with conflicting data.");
        }

        merged.Add(key, pilot);
    }

    private static void Validate(FirstEditionPilotRecord pilot, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(pilot.TargetId))
            throw new InvalidDataException($"Pilot in '{sourceFile}' has no target/id value.");

        if (string.IsNullOrWhiteSpace(pilot.Name))
            throw new InvalidDataException($"Pilot '{pilot.TargetId}' in '{sourceFile}' has no name.");

        if (string.IsNullOrWhiteSpace(pilot.ShipId))
            throw new InvalidDataException($"Pilot '{pilot.TargetId}' in '{sourceFile}' has no shipId.");

        if (string.IsNullOrWhiteSpace(pilot.Faction))
            throw new InvalidDataException($"Pilot '{pilot.TargetId}' in '{sourceFile}' has no faction.");
    }

    private static string BuildIdentityKey(FirstEditionPilotRecord pilot) =>
        $"{pilot.TargetId}\u001f{pilot.ShipId}\u001f{pilot.Faction}";

    private static bool Equivalent(
        FirstEditionPilotRecord left,
        FirstEditionPilotRecord right) =>
        left.TargetId.Equals(right.TargetId, StringComparison.OrdinalIgnoreCase)
        && left.Name.Equals(right.Name, StringComparison.Ordinal)
        && left.ShipId.Equals(right.ShipId, StringComparison.OrdinalIgnoreCase)
        && left.Faction.Equals(right.Faction, StringComparison.OrdinalIgnoreCase)
        && left.PilotSkill == right.PilotSkill
        && left.SquadPointCost == right.SquadPointCost
        && left.Unique == right.Unique;

    private sealed class OfficialFirstEditionPilotRecord
    {
        public string ImportId { get; init; } = string.Empty;
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string ShipId { get; init; } = string.Empty;
        public string Faction { get; init; } = string.Empty;
        public int PilotSkill { get; init; }
        public int SquadPointCost { get; init; }
        public bool Unique { get; init; }
    }
}
