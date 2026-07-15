using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Conversion.FirstEdition.Pilots;
using UnifiedToolkit.Conversion.FirstEdition.Upgrades;

namespace UnifiedToolkit.Commands;

public static class InspectFirstEditionCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: UnifiedToolkit inspect-first-edition <repo-folder> <ship|pilot|upgrade> <target-id> [mapping-folder] [--allow-source-errors]");
            return 1;
        }

        try
        {
            var repositoryFolder = Path.GetFullPath(args[0]);
            var entityType = args[1].ToLowerInvariant();
            var targetId = args[2];
            var mappingFolder = args.Length > 3 && !args[3].StartsWith("--", StringComparison.Ordinal)
                ? Path.GetFullPath(args[3])
                : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
            var allowSourceErrors = args.Any(x => x.Equals("--allow-source-errors", StringComparison.OrdinalIgnoreCase));

            var build = FirstEditionRepositoryBuilder.Build(repositoryFolder, mappingFolder, allowSourceErrors);

            Console.WriteLine("UnifiedToolkit First Edition Inspection");
            Console.WriteLine("=======================================");
            Console.WriteLine();
            Console.WriteLine($"Mapping version: {build.MappingVersion}");
            Console.WriteLine();

            return entityType switch
            {
                "ship" => ShowShip(build.Repository, targetId),
                "pilot" => ShowPilots(build.Repository.FindPilotsById(targetId), targetId),
                "upgrade" => ShowUpgrades(build.Repository.FindUpgradesById(targetId), targetId),
                _ => UnknownType(entityType)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Inspection failed: {ex.Message}");
            return 1;
        }
    }

    private static int ShowShip(FirstEditionRepository repository, string id)
    {
        var ship = repository.FindShip(id);
        if (ship is null)
        {
            Console.WriteLine($"First Edition ship '{id}' was not found.");
            return 2;
        }

        var pilots = repository.FindPilotsByShip(ship.Id);
        var restrictedUpgrades = repository.FindUpgradesRestrictedToShip(ship.Id);

        Console.WriteLine($"Type:             Ship");
        Console.WriteLine($"ID:               {ship.Id}");
        Console.WriteLine($"Name:             {ship.Name}");
        Console.WriteLine($"Size:             {ship.Size}");
        Console.WriteLine($"Stats:            Attack {ship.Attack}, Agility {ship.Agility}, Hull {ship.Hull}, Shields {ship.Shields}");
        Console.WriteLine($"Factions:         {Join(ship.Factions)}");
        Console.WriteLine($"Actions:          {Join(ship.Actions)}");
        Console.WriteLine($"Pilots:           {pilots.Count}");
        Console.WriteLine($"Restricted cards: {restrictedUpgrades.Count}");
        Console.WriteLine($"Source ID:        {ship.Provenance.SourceId}");
        Console.WriteLine($"Mapping ID:       {ship.Provenance.MappingId}");

        if (pilots.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Pilots:");
            foreach (var pilot in pilots.OrderByDescending(x => x.PilotSkill).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"  {pilot.Id,-32} PS {pilot.PilotSkill,2}  {pilot.SquadPointCost,3} points  {pilot.Faction}");
        }

        return 0;
    }

    private static int ShowPilots(IReadOnlyList<FirstEditionPilot> pilots, string id)
    {
        if (pilots.Count == 0)
        {
            Console.WriteLine($"First Edition pilot '{id}' was not found.");
            return 2;
        }

        Console.WriteLine($"Type:    Pilot");
        Console.WriteLine($"Matches: {pilots.Count}");
        foreach (var pilot in pilots)
        {
            Console.WriteLine();
            Console.WriteLine($"ID:            {pilot.Id}");
            Console.WriteLine($"Name:          {pilot.Name}");
            Console.WriteLine($"Ship:          {pilot.ShipId}");
            Console.WriteLine($"Faction:       {pilot.Faction}");
            Console.WriteLine($"Pilot skill:   {pilot.PilotSkill}");
            Console.WriteLine($"Cost:          {pilot.SquadPointCost}");
            Console.WriteLine($"Unique:        {pilot.Unique}");
            Console.WriteLine($"Upgrade slots: {Join(pilot.UpgradeSlots)}");
            Console.WriteLine($"Source ID:     {pilot.Provenance.SourceId}");
            Console.WriteLine($"Mapping ID:    {pilot.Provenance.MappingId}");
        }
        return 0;
    }

    private static int ShowUpgrades(IReadOnlyList<FirstEditionUpgrade> upgrades, string id)
    {
        if (upgrades.Count == 0)
        {
            Console.WriteLine($"First Edition upgrade '{id}' was not found.");
            return 2;
        }

        Console.WriteLine($"Type:    Upgrade");
        Console.WriteLine($"Matches: {upgrades.Count}");
        foreach (var upgrade in upgrades)
        {
            Console.WriteLine();
            Console.WriteLine($"ID:                {upgrade.Id}");
            Console.WriteLine($"Name:              {upgrade.Name}");
            Console.WriteLine($"Slot:              {upgrade.Slot}");
            Console.WriteLine($"Cost:              {upgrade.SquadPointCost}");
            Console.WriteLine($"Unique:            {upgrade.Unique}");
            Console.WriteLine($"Factions:          {Join(upgrade.Factions)}");
            Console.WriteLine($"Ship restrictions: {Join(upgrade.ShipRestrictions)}");
            Console.WriteLine($"Size restrictions: {Join(upgrade.SizeRestrictions)}");
            Console.WriteLine($"Source ID:         {upgrade.Provenance.SourceId}");
            Console.WriteLine($"Mapping ID:        {upgrade.Provenance.MappingId}");
            Console.WriteLine($"Text:              {upgrade.Text}");
        }
        return 0;
    }

    private static int UnknownType(string type)
    {
        Console.WriteLine($"Unknown First Edition entity type '{type}'. Use ship, pilot, or upgrade.");
        return 1;
    }

    private static string Join(IEnumerable<string> values)
    {
        var list = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }
}
