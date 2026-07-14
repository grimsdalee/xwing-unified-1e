using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Dispositions;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public static class DeferredOfficialAliasResolver
{
    private static readonly IReadOnlyDictionary<string, string[]> TargetHints =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cr90corvette"] = ["cr90corvette"],
            ["fangfighter"] = ["protectoratestarfighter"],
            ["firesprayclasspatrolcraft"] = ["firespray31"],
            ["modifiedyt1300lightfreighter"] = ["yt1300"],
            ["raiderclasscorvette"] = ["raiderclasscorvette", "raider"],
            ["tieadvancedv1"] = ["tieadvprototype", "tieadvancedprototype"],
            ["tieadvancedx1"] = ["tieadvanced"],
            ["tieagaggressor"] = ["tieaggressor"],
            ["tiecapunisher"] = ["tiepunisher"],
            ["tiededefender"] = ["tiedefender"],
            ["tieininterceptor"] = ["tieinterceptor"],
            ["tielnfighter"] = ["tiefighter"],
            ["tiephphantom"] = ["tiephantom"],
            ["tiesabomber"] = ["tiebomber"],
            ["tieskstriker"] = ["tiestriker"]
        };

    public static IReadOnlyList<OfficialAliasCandidate> Resolve(
        IReadOnlyList<ShipDefinition> sourceShips,
        IReadOnlyList<FirstEditionDataShip> officialShips,
        ConversionMappingSet mappings)
    {
        var sourceById = sourceShips.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var officialById = officialShips
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var existingMappingIds = mappings.Ships
            .Select(x => x.SourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<OfficialAliasCandidate>();
        foreach (var disposition in mappings.ShipDispositions
                     .Where(x => x.Kind == ShipDispositionKind.Deferred)
                     .OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase))
        {
            if (existingMappingIds.Contains(disposition.SourceId) ||
                !sourceById.TryGetValue(disposition.SourceId, out var source))
            {
                continue;
            }

            if (!TargetHints.TryGetValue(source.Id, out var hints))
            {
                candidates.Add(new OfficialAliasCandidate
                {
                    Source = source,
                    MatchMethod = "NoCuratedHint",
                    Confidence = 0m,
                    Decision = "RemainDeferred",
                    Notes = "No curated official First Edition alias hint exists for this deferred ship."
                });
                continue;
            }

            var matches = hints
                .Where(officialById.ContainsKey)
                .Select(hint => officialById[hint])
                .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (matches.Count == 0)
            {
                var epicComposite = source.Id.Equals("cr90corvette", StringComparison.OrdinalIgnoreCase) ||
                                    source.Id.Equals("raiderclasscorvette", StringComparison.OrdinalIgnoreCase);
                candidates.Add(new OfficialAliasCandidate
                {
                    Source = source,
                    SuggestedTargetId = string.Join(";", hints),
                    MatchMethod = epicComposite ? "EpicCompositeModelRequired" : "CuratedHintMissing",
                    Confidence = epicComposite ? 1.00m : 0.35m,
                    Decision = epicComposite ? "RequiresEpicCompositeModel" : "ReviewMissingTarget",
                    Notes = epicComposite
                        ? "This official First Edition Epic ship uses multiple ship sections and cannot be represented accurately by the current single-stat-line FirstEditionShip model."
                        : "A curated alias hint exists, but no matching target ID was found in the supplied First Edition dataset."
                });
                continue;
            }

            if (matches.Count > 1)
            {
                candidates.Add(new OfficialAliasCandidate
                {
                    Source = source,
                    SuggestedTargetId = string.Join(";", matches.Select(x => x.Id)),
                    MatchMethod = "CuratedHintAmbiguous",
                    Confidence = 0.50m,
                    Decision = "ReviewAmbiguous",
                    Notes = "More than one curated First Edition target exists. Select one explicitly before promotion."
                });
                continue;
            }

            var target = matches[0];
            candidates.Add(new OfficialAliasCandidate
            {
                Source = source,
                Target = target,
                SuggestedTargetId = target.Id,
                MatchMethod = "CuratedCanonicalId",
                Confidence = 1.00m,
                Decision = "ProposedAlias",
                Notes = "Curated source-to-canonical First Edition identity resolved against the supplied dataset.",
                ProposedMapping = BuildMapping(source, target)
            });
        }

        return candidates;
    }

    private static ShipMapping BuildMapping(ShipDefinition source, FirstEditionDataShip target) => new()
    {
        MappingId = $"ship-{target.Id}-alias-v1",
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
