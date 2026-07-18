using System.Text.RegularExpressions;

namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed class PilotIdentityProfile
{
    public IReadOnlyList<string> StrongAliases { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WeakAliases { get; init; } = Array.Empty<string>();

    public static PilotIdentityProfile Create(FirstEditionPilotRecord pilot)
    {
        var strong = new[] { pilot.TargetId, pilot.SourceId, pilot.Name }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Compact)
            .Where(value => value.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var weak = Regex.Matches(pilot.Name.ToLowerInvariant(), "[a-z0-9]+")
            .Select(match => match.Value)
            .Where(value => value.Length >= 4 && value is not "pilot" and not "squadron")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PilotIdentityProfile { StrongAliases = strong, WeakAliases = weak };
    }

    public static string Compact(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", string.Empty);
}
