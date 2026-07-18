using System.Text;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking.Reports;

public sealed class ShipAssetLinkSummaryWriter
{
    public void Write(string path, IReadOnlyCollection<KnowledgeBaseShip> ships)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(ships);

        var candidateLinkCount = ships.Sum(
            ship => ship.AssetRoles.Sum(role => role.Candidates.Count));

        var clearRoleMatchCount = ships.Sum(
            ship => ship.AssetRoles.Count(role => role.Status == "clear"));

        var reviewRoleCount = ships.Sum(
            ship => ship.AssetRoles.Count(role => role.Status == "review"));

        var unresolvedRequiredRoles = ships
            .SelectMany(ship => ship.AssetRoles
                .Where(role => role.Required && role.Candidates.Count == 0)
                .Select(role => new { Ship = ship, Role = role }))
            .ToList();

        using var writer = new StreamWriter(
            path,
            append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.WriteLine("# Ship Asset Link Summary");
        writer.WriteLine();
        writer.WriteLine($"Ships: **{ships.Count}**  ");
        writer.WriteLine($"Candidate links: **{candidateLinkCount}**  ");
        writer.WriteLine($"Clear role matches: **{clearRoleMatchCount}**  ");
        writer.WriteLine($"Roles requiring review: **{reviewRoleCount}**  ");
        writer.WriteLine($"Missing required roles: **{unresolvedRequiredRoles.Count}**");
        writer.WriteLine();

        if (unresolvedRequiredRoles.Count > 0)
        {
            writer.WriteLine("## Unresolved required roles");
            writer.WriteLine();

            foreach (var group in unresolvedRequiredRoles
                         .GroupBy(item => item.Role.Role)
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteLine($"- **{group.Key}:** {group.Count()}");
            }

            writer.WriteLine();
            writer.WriteLine(
                "See `unresolved-required-ship-assets.csv` for the affected ships and recommended resolution.");
            writer.WriteLine();
        }

        writer.WriteLine(
            "No candidate is approved by this command. " +
            "Review `ship-link-review.csv` first.");
    }
}
