namespace UnifiedToolkit.Lua.Parsing;

public sealed class LuaParseException : Exception
{
    public LuaParseException(
        string message,
        int position)
        : base($"{message} Position: {position}.")
    {
        Position = position;
    }

    public int Position { get; }
}