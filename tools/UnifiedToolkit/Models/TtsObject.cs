using System.Text.Json.Nodes;

namespace UnifiedToolkit.Models;

public sealed class TtsObject
{
    public JsonObject Json { get; init; } = new();

    public TtsObject? Parent { get; set; }

    public string Guid { get; init; } = "";
    public string Name { get; init; } = "";
    public string Nickname { get; init; } = "";
    public string Description { get; init; } = "";
    public string GMNotes { get; init; } = "";
    public string Type { get; init; } = "";

    public bool HasLua { get; init; }
    public bool HasXml { get; init; }

    public List<TtsObject> Children { get; } = new();

    public bool IsContainer => Children.Count > 0;

    public bool IsCard =>
        Type == "Card" ||
        Type == "CardCustom";

    public bool IsDeck =>
        Type == "Deck" ||
        Type == "DeckCustom";

    public bool IsBag =>
        Type == "Bag" ||
        Type == "Custom_Model_Bag" ||
        Type == "Custom_Model_Infinite_Bag";

    public bool IsInfiniteBag =>
        Type == "Custom_Model_Infinite_Bag";

    public bool IsModel =>
        Type.StartsWith("Custom_Model");

    public IEnumerable<TtsObject> AllChildren()
    {
        foreach (var child in Children)
        {
            yield return child;

            foreach (var descendant in child.AllChildren())
                yield return descendant;
        }
    }
}