namespace UnifiedToolkit.Lua.Model;

public sealed class LuaDatabaseSchema
{
    public required string DatabaseName { get; init; }

    public required string TableName { get; init; }

    public int EntityCount { get; init; }

    public required IReadOnlyList<LuaFieldSchema> Fields
    {
        get;
        init;
    }
}