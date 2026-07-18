namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class LegacyAssetContextMatcher
{
    public bool IsPlausibleForRole(IReadOnlyList<LegacyAssetContext> contexts, string role) =>
        contexts.Any(context => GetRoleAffinity(context, role).Points > 0);

    public (int Points, string Reason) FindBestMatch(
        IReadOnlyList<LegacyAssetContext> contexts,
        string role,
        ShipIdentityProfile identity,
        string baseSize)
    {
        var bestPoints = 0;
        var bestReason = string.Empty;

        foreach (var context in contexts)
        {
            var affinity = GetRoleAffinity(context, role);
            if (affinity.Points == 0)
            {
                continue;
            }

            var identityPoints = ScoreIdentity(context.SearchText, identity);
            if (identityPoints > 0)
            {
                var points = Math.Min(70, identityPoints + affinity.Points);
                if (points > bestPoints)
                {
                    bestPoints = points;
                    var label = FirstNonEmpty(context.ObjectNickname, context.ObjectName, context.ObjectGuid, "legacy object");
                    bestReason = $"legacy object context '{label}' via {affinity.Reason}";
                }
            }

            var sharedMatch = ScoreSharedRole(context, role, baseSize, affinity);
            if (sharedMatch.Points > bestPoints)
            {
                bestPoints = sharedMatch.Points;
                bestReason = sharedMatch.Reason;
            }
        }

        return (bestPoints, bestReason);
    }

    private static int ScoreIdentity(string searchText, ShipIdentityProfile identity)
    {
        var compactText = ShipAssetText.Compact(searchText);
        foreach (var alias in identity.StrongAliases.OrderByDescending(value => value.Length))
        {
            var compactAlias = ShipAssetText.Compact(alias);
            if (compactAlias.Length >= 3
                && compactText.Contains(compactAlias, StringComparison.OrdinalIgnoreCase))
            {
                return compactAlias.Length >= 8 ? 42 : 34;
            }
        }

        foreach (var alias in identity.WeakAliases.OrderByDescending(value => value.Length))
        {
            var compactAlias = ShipAssetText.Compact(alias);
            if (compactAlias.Length >= 4
                && compactText.Contains(compactAlias, StringComparison.OrdinalIgnoreCase))
            {
                return 18;
            }
        }

        return 0;
    }

    private static (int Points, string Reason) ScoreSharedRole(
        LegacyAssetContext context,
        string role,
        string baseSize,
        (int Points, string Reason) affinity)
    {
        if (!baseSize.Equals("epic", StringComparison.OrdinalIgnoreCase)
            || !role.Equals("DialTexture", StringComparison.OrdinalIgnoreCase))
        {
            return (0, string.Empty);
        }

        var text = context.SearchText;
        if (!ContainsAny(text, "huge ship", "epic ship", "huge maneuver", "huge dial"))
        {
            return (0, string.Empty);
        }

        var label = FirstNonEmpty(context.ObjectNickname, context.ObjectName, context.ObjectGuid, "shared huge ship dial");
        return (70, $"shared epic dial context '{label}' via {affinity.Reason}");
    }

    private static (int Points, string Reason) GetRoleAffinity(LegacyAssetContext context, string role)
    {
        var property = context.PropertyName;
        var text = $"{context.ObjectName} {context.ObjectNickname} {context.ContainerText} {context.JsonPath}";

        return role switch
        {
            "ShipModel" when property.Contains("MeshURL", StringComparison.OrdinalIgnoreCase) =>
                (24, property),

            "ShipTexture" when property.Contains("DiffuseURL", StringComparison.OrdinalIgnoreCase)
                && !ContainsAny(text, "dial", "maneuver", "token", "base") =>
                (24, property),

            "BaseToken" when ContainsAny(text, "ship token", "base token", "pilot token", "token")
                && IsImageProperty(property) =>
                (28, $"{property} token context"),

            "DialTexture" when ContainsAny(text, "dial", "maneuver")
                && IsImageProperty(property) =>
                (30, $"{property} dial context"),

            "DialModel" when property.Contains("MeshURL", StringComparison.OrdinalIgnoreCase)
                && ContainsAny(text, "dial", "maneuver") =>
                (30, $"{property} dial context"),

            _ => (0, string.Empty)
        };
    }

    private static bool IsImageProperty(string property) =>
        property.Contains("ImageURL", StringComparison.OrdinalIgnoreCase)
        || property.Contains("DiffuseURL", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(params string[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value));
}
