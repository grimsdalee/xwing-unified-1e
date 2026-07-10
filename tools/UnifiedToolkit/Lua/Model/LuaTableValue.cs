namespace UnifiedToolkit.Lua.Model;

public sealed class LuaTableValue : LuaValue
{
    private readonly Dictionary<string, LuaValue> _fields =
        new(StringComparer.Ordinal);

    private readonly List<LuaValue> _items = new();

    public override LuaValueKind Kind =>
        LuaValueKind.Table;

    public IReadOnlyDictionary<string, LuaValue> Fields =>
        _fields;

    public IReadOnlyList<LuaValue> Items =>
        _items;

    public void SetField(
        string key,
        LuaValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        _fields[key] = value;
    }

    public void AddItem(LuaValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _items.Add(value);
    }

    public bool TryGetValue(
        string key,
        out LuaValue? value)
    {
        return _fields.TryGetValue(key, out value);
    }

    public override string ToString()
    {
        return $"table ({Fields.Count} fields, " +
               $"{Items.Count} items)";
    }
}