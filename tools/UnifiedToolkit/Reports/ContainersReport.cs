using UnifiedToolkit.Models;

namespace UnifiedToolkit.Reports;

public sealed class ContainersReport : CsvReport
{
    public override string FileName => "containers.csv";

    public override void Generate(TtsGame game, IReadOnlyList<TtsObject> objects, string reportsFolder)
    {
        using var writer = CreateWriter(reportsFolder, FileName);

        writer.WriteLine("Guid,Nickname,Type,ContainerKind,ParentGuid,ParentNickname,ChildCount,DescendantCount,HasLua,HasXml");

        foreach (var obj in objects
                     .Where(x => x.IsContainer)
                     .OrderByDescending(x => x.Children.Count)
                     .ThenBy(x => x.Nickname))
        {
            writer.WriteLine(string.Join(",",
                Csv(obj.Guid),
                Csv(obj.Nickname),
                Csv(obj.Type),
                Csv(GetContainerKind(obj)),
                Csv(obj.Parent?.Guid ?? ""),
                Csv(obj.Parent?.Nickname ?? ""),
                Csv(obj.Children.Count.ToString()),
                Csv(obj.AllChildren().Count().ToString()),
                Csv(obj.HasLua.ToString()),
                Csv(obj.HasXml.ToString())));
        }
    }

    private static string GetContainerKind(TtsObject obj)
    {
        if (obj.IsInfiniteBag)
            return "Infinite Bag";

        if (obj.IsBag)
            return "Bag";

        if (obj.IsDeck)
            return "Deck";

        return "Container";
    }
}