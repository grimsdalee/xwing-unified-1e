using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Repository;

public static class RepositoryValidator
{
    public static List<RepositoryValidationIssue> Validate(
        Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var issues = new List<RepositoryValidationIssue>();

        AddPilotValidationIssues(repository, issues);
        AddUpgradeValidationIssues(repository, issues);

        ValidatePilotShipLinks(repository, issues);
        ValidateUpgradeRestrictions(repository, issues);

        return issues
            .OrderBy(issue => SeverityOrder(issue.Severity))
            .ThenBy(issue => issue.Category)
            .ThenBy(issue => issue.Code)
            .ThenBy(issue => issue.EntityType)
            .ThenBy(issue => issue.EntityName)
            .ThenBy(issue => issue.EntityId)
            .ThenBy(issue => issue.FieldName)
            .ToList();
    }

    private static void AddPilotValidationIssues(
        Repository repository,
        ICollection<RepositoryValidationIssue> issues)
    {
        var pilotIssues =
            PilotValidator.Validate(repository.Pilots);

        foreach (var issue in pilotIssues)
        {
            issues.Add(new RepositoryValidationIssue
            {
                Severity = issue.Severity,
                Category = "Pilot",
                Code = issue.Code,
                EntityType = "Pilot",
                EntityId = issue.PilotId,
                EntityName = issue.PilotName,
                FieldName = "",
                Message = issue.Message
            });
        }
    }

    private static void AddUpgradeValidationIssues(
        Repository repository,
        ICollection<RepositoryValidationIssue> issues)
    {
        var upgradeIssues =
            UpgradeValidator.Validate(repository.Upgrades);

        foreach (var issue in upgradeIssues)
        {
            issues.Add(new RepositoryValidationIssue
            {
                Severity = issue.Severity,
                Category = "Upgrade",
                Code = issue.Code,
                EntityType = "Upgrade",
                EntityId = issue.UpgradeId,
                EntityName = issue.UpgradeName,
                FieldName = issue.FieldName,
                Message = issue.Message
            });
        }
    }

    private static void ValidatePilotShipLinks(
        Repository repository,
        ICollection<RepositoryValidationIssue> issues)
    {
        foreach (var pilot in repository.Pilots)
        {
            if (pilot.Ship is not null)
                continue;

            issues.Add(new RepositoryValidationIssue
            {
                Severity = "Error",
                Category = "CrossReference",
                Code = "UnlinkedPilotShip",
                EntityType = "Pilot",
                EntityId = pilot.Id,
                EntityName = pilot.Name,
                FieldName = "ship_type",
                Message =
                    $"Pilot references ship type " +
                    $"'{pilot.ShipType}', but no linked ship " +
                    "is available in the repository."
            });
        }
    }

    private static void ValidateUpgradeRestrictions(
        Repository repository,
        ICollection<RepositoryValidationIssue> issues)
    {
        var knownFactions = BuildKnownFactions(repository);
        var knownSizes = BuildKnownSizes(repository);

        foreach (var upgrade in repository.Upgrades)
        {
            ValidateRestrictedShips(
                repository,
                upgrade,
                issues);

            ValidateRestrictedFactions(
                upgrade,
                knownFactions,
                issues);

            ValidateRestrictedSizes(
                upgrade,
                knownSizes,
                issues);

            ValidateInitiativeRestriction(
                upgrade,
                issues);
        }
    }

    private static void ValidateRestrictedShips(
        Repository repository,
        UpgradeDefinition upgrade,
        ICollection<RepositoryValidationIssue> issues)
    {
        foreach (var shipId in
                 upgrade.ParsedRestrictions.Ships)
        {
            if (repository.FindShip(shipId) is not null)
                continue;

            issues.Add(new RepositoryValidationIssue
            {
                Severity = "Error",
                Category = "CrossReference",
                Code = "UnknownRestrictedShip",
                EntityType = "Upgrade",
                EntityId = upgrade.Id,
                EntityName = upgrade.Name,
                FieldName = "restriction.ship",
                Message =
                    $"Upgrade restricts usage to ship " +
                    $"'{shipId}', but that ship does not exist " +
                    "in the repository."
            });
        }
    }

    private static void ValidateRestrictedFactions(
        UpgradeDefinition upgrade,
        IReadOnlySet<string> knownFactions,
        ICollection<RepositoryValidationIssue> issues)
    {
        foreach (var faction in
                 upgrade.ParsedRestrictions.Factions)
        {
            if (knownFactions.Contains(faction))
                continue;

            issues.Add(new RepositoryValidationIssue
            {
                Severity = "Warning",
                Category = "CrossReference",
                Code = "UnknownRestrictedFaction",
                EntityType = "Upgrade",
                EntityId = upgrade.Id,
                EntityName = upgrade.Name,
                FieldName = "restriction.faction",
                Message =
                    $"Upgrade uses unknown faction restriction " +
                    $"'{faction}'."
            });
        }
    }

    private static void ValidateRestrictedSizes(
        UpgradeDefinition upgrade,
        IReadOnlySet<string> knownSizes,
        ICollection<RepositoryValidationIssue> issues)
    {
        foreach (var size in
                 upgrade.ParsedRestrictions.Sizes)
        {
            if (knownSizes.Contains(size))
                continue;

            issues.Add(new RepositoryValidationIssue
            {
                Severity = "Warning",
                Category = "CrossReference",
                Code = "UnknownRestrictedSize",
                EntityType = "Upgrade",
                EntityId = upgrade.Id,
                EntityName = upgrade.Name,
                FieldName = "restriction.size",
                Message =
                    $"Upgrade uses unknown ship-size restriction " +
                    $"'{size}'."
            });
        }
    }

    private static void ValidateInitiativeRestriction(
        UpgradeDefinition upgrade,
        ICollection<RepositoryValidationIssue> issues)
    {
        var initiative =
            upgrade.ParsedRestrictions.InitiativeGreaterThan;

        if (!initiative.HasValue)
            return;

        if (initiative.Value is >= 0 and <= 10)
            return;

        issues.Add(new RepositoryValidationIssue
        {
            Severity = "Warning",
            Category = "Upgrade",
            Code = "UnusualInitiativeRestriction",
            EntityType = "Upgrade",
            EntityId = upgrade.Id,
            EntityName = upgrade.Name,
            FieldName =
                "restriction.initiative_greater_than",
            Message =
                $"Upgrade has unusual initiative restriction " +
                $"value {initiative.Value}."
        });
    }

    private static HashSet<string> BuildKnownFactions(
        Repository repository)
    {
        var factions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var ship in repository.Ships)
        {
            factions.UnionWith(ship.Factions);
        }

        foreach (var pilot in repository.Pilots)
        {
            if (!string.IsNullOrWhiteSpace(pilot.Faction))
                factions.Add(pilot.Faction);
        }

        return factions;
    }

    private static HashSet<string> BuildKnownSizes(
        Repository repository)
    {
        return repository.Ships
            .Select(ship => ship.Size)
            .Where(size => !string.IsNullOrWhiteSpace(size))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static int SeverityOrder(string severity)
    {
        return severity switch
        {
            "Error" => 0,
            "Warning" => 1,
            _ => 2
        };
    }
}