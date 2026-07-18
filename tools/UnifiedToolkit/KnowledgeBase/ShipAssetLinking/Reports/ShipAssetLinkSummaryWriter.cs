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

        var missingRequiredRoleCount = ships.Sum(
            ship => ship.AssetRoles.Count(
                role => role.Required && role.Candidates.Count == 0));

        using var writer = new StreamWriter(
            path,
            append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.WriteLine("# Ship Asset Link Summary");
        writer.WriteLine();
        writer.WriteLine($"Ships: **{ships.Count}**  ");
        writer.WriteLine($"Candidate links: **{candidateLinkCount}**  ");
        writer.WriteLine($"Clear role matches: **{clearRoleMatchCount}**  ");
        writer.WriteLine($"Missing required roles: **{missingRequiredRoleCount}**");
        writer.WriteLine();
        writer.WriteLine(
            "No candidate is approved by this command. " +
            "Review `ship-link-review.csv` first.");
    }
}
