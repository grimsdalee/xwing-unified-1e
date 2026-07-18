using System.Text.RegularExpressions;
using UnifiedToolkit.KnowledgeBase;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class ShipAliasBuilder
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "class", "fighter", "assault", "light", "freighter", "star", "wing", "ship"
    };

    public ShipIdentityProfile Build(FirstEditionShipRecord ship)
    {
        ArgumentNullException.ThrowIfNull(ship);

        var strongAliases = new[] { ship.SourceId, ship.TargetId, ship.Name }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => new[]
            {
                ShipAssetText.Normalize(value),
                ShipAssetText.Compact(value)
            })
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var weakAliases = Regex.Split(ship.Name ?? string.Empty, "[^A-Za-z0-9]+")
            .Select(ShipAssetText.Normalize)
            .Where(value => value.Length >= 4 && !StopWords.Contains(value))
            .Where(value => !strongAliases.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ShipIdentityProfile
        {
            StrongAliases = strongAliases,
            WeakAliases = weakAliases
        };
    }
}
