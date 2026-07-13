using UnifiedToolkit.Conversion.Issues;

namespace UnifiedToolkit.Conversion.FirstEdition.Validation;

public static class FirstEditionRepositoryValidator
{
    private static readonly HashSet<string> SupportedSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        "small",
        "large",
        "huge"
    };

    public static IReadOnlyList<ConversionIssue> Validate(FirstEditionRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var issues = new List<ConversionIssue>();

        ValidateDuplicateIds(repository.Ships, issues);

        foreach (var ship in repository.Ships)
        {
            if (string.IsNullOrWhiteSpace(ship.Id))
                issues.Add(ShipError(ship, "BlankTargetShipId", "The target ship ID is blank."));
            if (string.IsNullOrWhiteSpace(ship.Name))
                issues.Add(ShipError(ship, "BlankTargetShipName", "The target ship name is blank."));
            if (!SupportedSizes.Contains(ship.Size))
                issues.Add(ShipError(ship, "UnknownFirstEditionShipSize", $"Unknown First Edition ship size '{ship.Size}'."));
            if (ship.Attack < 0 || ship.Agility < 0 || ship.Hull <= 0 || ship.Shields < 0)
                issues.Add(ShipError(ship, "InvalidFirstEditionShipStats", "Target ship statistics are invalid."));
            if (ship.Factions.Count == 0)
                issues.Add(ShipError(ship, "MissingFirstEditionShipFaction", "The target ship has no faction."));
            if (ship.Factions.Any(string.IsNullOrWhiteSpace))
                issues.Add(ShipError(ship, "BlankFirstEditionShipFaction", "The target ship contains a blank faction."));
            if (ship.Actions.Any(string.IsNullOrWhiteSpace))
                issues.Add(ShipError(ship, "BlankFirstEditionShipAction", "The target ship contains a blank action."));
            if (string.IsNullOrWhiteSpace(ship.Provenance.SourceId) ||
                string.IsNullOrWhiteSpace(ship.Provenance.MappingId) ||
                string.IsNullOrWhiteSpace(ship.Provenance.MappingVersion))
            {
                issues.Add(ShipError(ship, "IncompleteConversionProvenance", "The target ship conversion provenance is incomplete."));
            }
        }

        return issues;
    }

    private static void ValidateDuplicateIds(
        IEnumerable<FirstEditionShip> ships,
        ICollection<ConversionIssue> issues)
    {
        foreach (var group in ships
                     .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                     .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
        {
            issues.Add(new ConversionIssue
            {
                Severity = "Error",
                Category = "TargetValidation",
                Code = "DuplicateFirstEditionShipId",
                SourceType = "Ship",
                TargetId = group.Key,
                Message = $"Multiple converted ships use target ID '{group.Key}'."
            });
        }
    }

    private static ConversionIssue ShipError(FirstEditionShip ship, string code, string message) => new()
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
}
