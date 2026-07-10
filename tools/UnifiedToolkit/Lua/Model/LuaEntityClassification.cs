namespace UnifiedToolkit.Lua.Model;

public sealed class LuaEntityClassification
{
    public required LuaEntity Entity { get; init; }

    public required string Classification { get; init; }

    public required string Reason { get; init; }

    public bool IsSemanticCandidate =>
        Classification.Equals(
            "SemanticCandidate",
            StringComparison.Ordinal);
}