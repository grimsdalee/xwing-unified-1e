using UnifiedToolkit.KnowledgeBase;
using System.Text;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking.Reports;

public sealed class ShipAssetLinkReviewCsvWriter
{
    public void Write(string path, IEnumerable<KnowledgeBaseShip> ships)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("ShipId,TargetId,ShipName,BaseSize,Role,Required,Status,Rank,Score,Confidence,Warehouse,AssetId,RepositoryPath,Reasons");

        foreach (var ship in ships)
        {
            foreach (var role in ship.AssetRoles)
            {
                if (role.Candidates.Count == 0)
                {
                    writer.WriteLine(string.Join(',',
                        Csv(ship.ShipId),
                        Csv(ship.TargetId),
                        Csv(ship.Name),
                        Csv(ship.BaseSize),
                        Csv(role.Role),
                        role.Required,
                        Csv(role.Status),
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty));
                }

                for (var index = 0; index < role.Candidates.Count; index++)
                {
                    var candidate = role.Candidates[index];
                    writer.WriteLine(string.Join(',',
                        Csv(ship.ShipId),
                        Csv(ship.TargetId),
                        Csv(ship.Name),
                        Csv(ship.BaseSize),
                        Csv(role.Role),
                        role.Required,
                        Csv(role.Status),
                        index + 1,
                        candidate.Score,
                        Csv(candidate.Confidence),
                        Csv(candidate.Warehouse),
                        Csv(candidate.AssetId),
                        Csv(candidate.RepositoryPath),
                        Csv(string.Join("; ", candidate.Reasons))));
                }
            }
        }
    }

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
