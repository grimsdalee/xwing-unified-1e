namespace UnifiedToolkit.Models;

public sealed class TtsGame
{
    public string SourcePath { get; init; } = "";

    public string GlobalLua { get; set; } = "";
    public string GlobalXml { get; set; } = "";

    public List<TtsObject> Objects { get; } = new();

    public IEnumerable<TtsObject> AllObjects()
    {
        foreach (var obj in Objects)
        {
            yield return obj;

            foreach (var child in obj.AllChildren())
                yield return child;
        }
    }
}