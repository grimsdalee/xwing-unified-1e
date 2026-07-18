using UnifiedToolkit.KnowledgeBase;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

internal static class ShipAssetText
{
    public static bool IsImage(string extension) =>
        extension is ".png" or ".jpg" or ".jpeg" or ".webp";

    public static string Normalize(string value) =>
        value.Replace('\\', '/').ToLowerInvariant();

    public static string Compact(string value) =>
        Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", string.Empty);
}
