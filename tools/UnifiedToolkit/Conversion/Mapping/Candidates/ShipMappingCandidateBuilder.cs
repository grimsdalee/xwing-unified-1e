using System.Globalization;
using System.Text;
using UnifiedToolkit.Conversion;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Conversion.Mapping.Candidates;

public static class ShipMappingCandidateBuilder
{
    public static IReadOnlyList<ShipMappingCandidate> Build(
        IEnumerable<ShipDefinition> sourceShips,
        ConversionMappingSet mappings)
    {
        ArgumentNullException.ThrowIfNull(sourceShips);
        ArgumentNullException.ThrowIfNull(mappings);

        var mappingsBySourceId = mappings.Ships.ToDictionary(
            mapping => mapping.SourceId,
            StringComparer.OrdinalIgnoreCase);

        var candidates = new List<ShipMappingCandidate>();

        foreach (var ship in sourceShips.OrderBy(x => x.Name).ThenBy(x => x.Id))
        {
            if (mappingsBySourceId.TryGetValue(ship.Id, out var existing))
            {
                var excluded = existing.Kind == ConversionKind.Excluded;

                candidates.Add(new ShipMappingCandidate
                {
                    SourceId = ship.Id,
                    SourceName = ship.Name,
                    SourceFaction = string.Join(";", ship.Factions.OrderBy(x => x)),
                    SourceSize = ship.Size,
                    SourceHull = ship.Hull,
                    SourceShields = ship.Shield,
                    SourceAgility = ship.Agility,
                    SuggestedTargetId = excluded ? "" : existing.TargetId,
                    SuggestedTargetName = excluded ? "" : existing.Name,
                    MatchMethod = "ExistingMapping",
                    Confidence = 1.00m,
                    Decision = excluded ? "Excluded" : "Mapped",
                    ExistingMappingId = existing.MappingId,
                    ExistingConversionKind = existing.Kind.ToString(),
                    Notes = excluded ? existing.ExclusionReason : "Existing mapping retained."
                });

                continue;
            }

            candidates.Add(new ShipMappingCandidate
            {
                SourceId = ship.Id,
                SourceName = ship.Name,
                SourceFaction = string.Join(";", ship.Factions.OrderBy(x => x)),
                SourceSize = ship.Size,
                SourceHull = ship.Hull,
                SourceShields = ship.Shield,
                SourceAgility = ship.Agility,
                SuggestedTargetId = CreateCandidateId(ship.Name),
                SuggestedTargetName = ship.Name,
                MatchMethod = "NameDerived",
                Confidence = 0.35m,
                Decision = "Review",
                Notes = "Candidate identity only. First Edition status, statistics, actions and factions require confirmation from an authoritative dataset."
            });
        }

        MarkTargetIdCollisions(candidates);
        return candidates;
    }

    private static void MarkTargetIdCollisions(List<ShipMappingCandidate> candidates)
    {
        var collisions = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.SuggestedTargetId))
            .GroupBy(x => x.SuggestedTargetId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (!collisions.TryGetValue(candidate.SuggestedTargetId, out var count))
                continue;

            candidates[index] = new ShipMappingCandidate
            {
                SourceId = candidate.SourceId,
                SourceName = candidate.SourceName,
                SourceFaction = candidate.SourceFaction,
                SourceSize = candidate.SourceSize,
                SourceHull = candidate.SourceHull,
                SourceShields = candidate.SourceShields,
                SourceAgility = candidate.SourceAgility,
                SuggestedTargetId = candidate.SuggestedTargetId,
                SuggestedTargetName = candidate.SuggestedTargetName,
                MatchMethod = candidate.MatchMethod,
                Confidence = Math.Min(candidate.Confidence, 0.20m),
                Decision = candidate.Decision == "Mapped" ? candidate.Decision : "ReviewCollision",
                ExistingMappingId = candidate.ExistingMappingId,
                ExistingConversionKind = candidate.ExistingConversionKind,
                Notes = AppendNote(candidate.Notes, $"Suggested target ID is shared by {count} source ships and requires an explicit identity decision.")
            };
        }
    }

    private static string CreateCandidateId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var normalized = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string AppendNote(string existing, string note)
    {
        return string.IsNullOrWhiteSpace(existing)
            ? note
            : $"{existing} {note}";
    }
}
