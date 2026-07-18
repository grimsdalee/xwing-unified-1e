using System.Text;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking.Reports;

public sealed class UnresolvedShipAssetRoleCsvWriter
{
    public void Write(
        string path,
        IReadOnlyCollection<KnowledgeBaseShip> ships)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(ships);

        using var writer = new StreamWriter(
            path,
            append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.WriteLine("ShipId,SourceId,TargetId,ShipName,BaseSize,Factions,Role,Required,Status,Resolution");

        foreach (var ship in ships.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var role in ship.AssetRoles
                         .Where(item => item.Required && item.Candidates.Count == 0)
                         .OrderBy(item => item.Role, StringComparer.OrdinalIgnoreCase))
            {
                WriteRow(writer, ship, role);
            }
        }
    }

    private static void WriteRow(
        TextWriter writer,
        KnowledgeBaseShip ship,
        KnowledgeBaseShipAssetRole role)
    {
        var resolution = role.Role switch
        {
            "BaseToken" => "Acquire or generate First Edition pilot ship-token artwork.",
            "DialTexture" when ship.BaseSize.Equals("epic", StringComparison.OrdinalIgnoreCase) =>
                "Acquire or generate the First Edition epic-ship maneuver dial.",
            "DialTexture" => "Acquire or generate the First Edition maneuver dial texture.",
            "ShipModel" => "Locate an authoritative ship model or approve a Unified 2.5 fallback.",
            "ShipTexture" => "Locate an authoritative ship texture or approve a Unified 2.5 fallback.",
            _ => "Locate or generate an authoritative asset for this required role."
        };

        writer.WriteLine(string.Join(",", new[]
        {
            Csv(ship.ShipId),
            Csv(ship.SourceId),
            Csv(ship.TargetId),
            Csv(ship.Name),
            Csv(ship.BaseSize),
            Csv(string.Join(" | ", ship.Factions)),
            Csv(role.Role),
            Csv(role.Required ? "true" : "false"),
            Csv(role.Status),
            Csv(resolution)
        }));
    }

    private static string Csv(string value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
