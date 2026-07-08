namespace UnifiedToolkit.Models;

public sealed class ObjectIndexEntry
{
    public int Index { get; set; }
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool HasLua { get; set; }
    public bool HasXml { get; set; }
    public string Description { get; set; } = "";
    public string GMNotes { get; set; } = "";
    public int ContainedCount { get; set; }
    public string CardID { get; set; } = "";
}