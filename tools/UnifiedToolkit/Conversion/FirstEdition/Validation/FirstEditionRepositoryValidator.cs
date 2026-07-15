using UnifiedToolkit.Conversion.FirstEdition.Pilots;
using UnifiedToolkit.Conversion.FirstEdition.Upgrades;
using UnifiedToolkit.Conversion.Issues;

namespace UnifiedToolkit.Conversion.FirstEdition.Validation;

public static class FirstEditionRepositoryValidator
{
    public static IReadOnlyList<ConversionIssue> Validate(FirstEditionRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var issues = new List<ConversionIssue>();

        foreach (var ship in repository.Ships)
        {
            if (string.IsNullOrWhiteSpace(ship.Id))
                issues.Add(Ship(ship, "BlankTargetShipId", "The target ship ID is blank."));
            if (string.IsNullOrWhiteSpace(ship.Name))
                issues.Add(Ship(ship, "BlankTargetShipName", "The target ship name is blank."));
            if (!FirstEditionVocabulary.ShipSizes.Contains(ship.Size))
                issues.Add(Ship(ship, "UnknownFirstEditionShipSize", $"Unknown First Edition ship size '{ship.Size}'."));
            if (ship.Attack < 0 || ship.Agility < 0 || ship.Hull <= 0 || ship.Shields < 0)
                issues.Add(Ship(ship, "InvalidFirstEditionShipStats", "Target ship statistics are invalid."));

            foreach (var faction in ship.Factions.Where(x => !FirstEditionVocabulary.Factions.Contains(x)))
                issues.Add(Ship(ship, "UnknownTargetShipFaction", $"Unknown First Edition faction '{faction}'."));
        }

        foreach (var pilot in repository.Pilots)
        {
            if (string.IsNullOrWhiteSpace(pilot.Id))
                issues.Add(Pilot(pilot, "BlankTargetPilotId", "The target pilot ID is blank."));
            if (repository.FindShip(pilot.ShipId) is null)
                issues.Add(Pilot(pilot, "UnknownTargetPilotShip", $"Target ship '{pilot.ShipId}' does not exist."));
            if (!FirstEditionVocabulary.Factions.Contains(pilot.Faction))
                issues.Add(Pilot(pilot, "UnknownTargetPilotFaction", $"Unknown First Edition faction '{pilot.Faction}'."));
            if (pilot.PilotSkill < 0 || pilot.PilotSkill > 12)
                issues.Add(Pilot(pilot, "InvalidPilotSkill", $"Pilot skill {pilot.PilotSkill} is invalid."));
            if (pilot.SquadPointCost < 0)
                issues.Add(Pilot(pilot, "InvalidPilotCost", $"Squad point cost {pilot.SquadPointCost} is invalid."));

            foreach (var slot in pilot.UpgradeSlots.Where(x => !FirstEditionVocabulary.UpgradeSlots.Contains(x)))
                issues.Add(Pilot(pilot, "UnknownPilotUpgradeSlot", $"Unknown First Edition upgrade slot '{slot}'."));
        }

        foreach (var upgrade in repository.Upgrades)
        {
            if (string.IsNullOrWhiteSpace(upgrade.Id))
                issues.Add(Upgrade(upgrade, "BlankTargetUpgradeId", "The target upgrade ID is blank."));
            if (string.IsNullOrWhiteSpace(upgrade.Slot))
                issues.Add(Upgrade(upgrade, "BlankTargetUpgradeSlot", "The target upgrade slot is blank."));
            else if (!FirstEditionVocabulary.UpgradeSlots.Contains(upgrade.Slot))
                issues.Add(Upgrade(upgrade, "UnknownTargetUpgradeSlot", $"Unknown First Edition upgrade slot '{upgrade.Slot}'."));
            if (upgrade.SquadPointCost < 0)
                issues.Add(Upgrade(upgrade, "InvalidUpgradeCost", $"Squad point cost {upgrade.SquadPointCost} is invalid."));

            foreach (var faction in upgrade.Factions.Where(x => !FirstEditionVocabulary.Factions.Contains(x)))
                issues.Add(Upgrade(upgrade, "UnknownUpgradeFactionRestriction", $"Unknown First Edition faction restriction '{faction}'."));

            foreach (var size in upgrade.SizeRestrictions.Where(x => !FirstEditionVocabulary.ShipSizes.Contains(x)))
                issues.Add(Upgrade(upgrade, "UnknownUpgradeSizeRestriction", $"Unknown First Edition size restriction '{size}'."));

            foreach (var shipId in upgrade.ShipRestrictions)
            {
                if (repository.FindShip(shipId) is not null) continue;
                if (FirstEditionVocabulary.DeferredEpicShipSectionIds.Contains(shipId)) continue;
                issues.Add(Upgrade(upgrade, "UnknownUpgradeShipRestriction", $"Target ship restriction '{shipId}' does not resolve to a converted ship or recognised Epic section."));
            }
        }

        return issues;
    }

    private static ConversionIssue Ship(FirstEditionShip ship, string code, string message) => new()
    {
        Severity = "Error",
        Category = "TargetValidation",
        Code = code,
        SourceType = "Ship",
        SourceId = ship.Provenance.SourceId,
        SourceName = ship.Name,
        TargetId = ship.Id,
        Message = message
    };

    private static ConversionIssue Upgrade(FirstEditionUpgrade upgrade, string code, string message) => new()
    {
        Severity = "Error",
        Category = "TargetValidation",
        Code = code,
        SourceType = "Upgrade",
        SourceId = upgrade.Provenance.SourceId,
        SourceName = upgrade.Name,
        TargetId = upgrade.Id,
        Message = message
    };

    private static ConversionIssue Pilot(FirstEditionPilot pilot, string code, string message) => new()
    {
        Severity = "Error",
        Category = "TargetValidation",
        Code = code,
        SourceType = "Pilot",
        SourceId = pilot.Provenance.SourceId,
        SourceName = pilot.Name,
        TargetId = pilot.Id,
        Message = message
    };
}
