using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public static class OfficialShipMatcher
{
    public static IReadOnlyList<OfficialShipMatch> Match(
        IReadOnlyList<ShipDefinition> sourceShips,
        IReadOnlyList<FirstEditionDataShip> targetShips,
        ConversionMappingSet existingMappings)
    {
        var targetsById = targetShips
            .GroupBy(ship => ship.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var targetsByName = targetShips
            .GroupBy(ship => FirstEditionDataLoader.NormaliseId(ship.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var existingBySource = existingMappings.Ships
            .ToDictionary(mapping => mapping.SourceId, StringComparer.OrdinalIgnoreCase);

        var results = new List<OfficialShipMatch>();

        foreach (var source in sourceShips.OrderBy(ship => ship.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (existingBySource.TryGetValue(source.Id, out var existing))
            {
                results.Add(new OfficialShipMatch
                {
                    Source = source,
                    Target = targetShips.FirstOrDefault(ship =>
                        ship.Id.Equals(existing.TargetId, StringComparison.OrdinalIgnoreCase)),
                    MatchMethod = "ExistingMapping",
                    Confidence = 1.00m,
                    Decision = existing.Kind == ConversionKind.Excluded ? "Excluded" : "AlreadyMapped",
                    Notes = existing.Kind == ConversionKind.Excluded
                        ? existing.ExclusionReason
                        : $"Existing mapping {existing.MappingId} is retained."
                });
                continue;
            }

            var matches = FindMatches(source, targetsById, targetsByName);
            if (matches.Count == 1)
            {
                var target = matches[0].Target;
                var mapping = BuildMapping(source, target);
                results.Add(new OfficialShipMatch
                {
                    Source = source,
                    Target = target,
                    MatchMethod = matches[0].Method,
                    Confidence = matches[0].Confidence,
                    Decision = matches[0].Confidence >= 0.90m ? "ProposedDirect" : "Review",
                    Notes = "Official First Edition identity found. Review size, faction and action normalisation before accepting.",
                    ProposedMapping = mapping
                });
            }
            else if (matches.Count > 1)
            {
                results.Add(new OfficialShipMatch
                {
                    Source = source,
                    MatchMethod = "Ambiguous",
                    Confidence = 0.25m,
                    Decision = "ReviewAmbiguous",
                    Notes = $"Multiple First Edition candidates: {string.Join(", ", matches.Select(match => match.Target.Id))}"
                });
            }
            else
            {
                results.Add(new OfficialShipMatch
                {
                    Source = source,
                    MatchMethod = "None",
                    Confidence = 0m,
                    Decision = "NotInOfficialDataset",
                    Notes = "No exact First Edition ID or normalised-name match was found. This ship may require a custom conversion or explicit exclusion."
                });
            }
        }

        return results;
    }

    private static List<(FirstEditionDataShip Target, string Method, decimal Confidence)> FindMatches(
        ShipDefinition source,
        IReadOnlyDictionary<string, List<FirstEditionDataShip>> targetsById,
        IReadOnlyDictionary<string, List<FirstEditionDataShip>> targetsByName)
    {
        var results = new Dictionary<string, (FirstEditionDataShip, string, decimal)>(StringComparer.OrdinalIgnoreCase);

        if (targetsById.TryGetValue(source.Id, out var idMatches))
        {
            foreach (var target in idMatches)
                results[target.Id] = (target, "ExactId", 1.00m);
        }

        var normalisedName = FirstEditionDataLoader.NormaliseId(source.Name);
        if (targetsById.TryGetValue(normalisedName, out var derivedIdMatches))
        {
            foreach (var target in derivedIdMatches)
                results.TryAdd(target.Id, (target, "NameDerivedId", 0.95m));
        }

        if (targetsByName.TryGetValue(normalisedName, out var nameMatches))
        {
            foreach (var target in nameMatches)
                results.TryAdd(target.Id, (target, "NormalisedName", 0.90m));
        }

        return results.Values
            .Select(value => (value.Item1, value.Item2, value.Item3))
            .ToList();
    }

    private static ShipMapping BuildMapping(ShipDefinition source, FirstEditionDataShip target)
    {
        return new ShipMapping
        {
            MappingId = $"ship-{target.Id}-direct-v1",
            SourceId = source.Id,
            TargetId = target.Id,
            Kind = ConversionKind.Direct,
            Name = target.Name,
            Size = target.Size,
            Attack = target.Attack,
            Agility = target.Agility,
            Hull = target.Hull,
            Shields = target.Shields,
            Actions = target.Actions,
            Factions = target.Factions
        };
    }
}
