namespace UnifiedToolkit.Lua.Model;

public sealed class LuaEntity
{
    public required string TableName { get; init; }

    public required string Id { get; init; }

    public required LuaTableValue Value { get; init; }

    public int SourceIndex { get; init; }

    public IReadOnlyDictionary<string, LuaValue> Fields =>
        Value.Fields;

    public bool TryGetValue(
        string fieldName,
        out LuaValue? value)
    {
        return Value.TryGetValue(fieldName, out value);
    }

    public string GetString(
        string fieldName,
        string defaultValue = "")
    {
        if (!TryGetValue(fieldName, out var value))
            return defaultValue;

        return value switch
        {
            LuaStringValue stringValue =>
                stringValue.Value,

            LuaIdentifierValue identifierValue =>
                identifierValue.Identifier,

            _ => defaultValue
        };
    }

    public int GetInt(
        string fieldName,
        int defaultValue = 0)
    {
        if (!TryGetValue(fieldName, out var value))
            return defaultValue;

        if (value is not LuaNumberValue numberValue)
            return defaultValue;

        if (numberValue.Value < int.MinValue ||
            numberValue.Value > int.MaxValue)
        {
            return defaultValue;
        }

        return decimal.ToInt32(numberValue.Value);
    }

    public bool GetBool(
        string fieldName,
        bool defaultValue = false)
    {
        if (!TryGetValue(fieldName, out var value))
            return defaultValue;

        return value is LuaBooleanValue booleanValue
            ? booleanValue.Value
            : defaultValue;
    }

    public IReadOnlyList<string> GetStringList(
        string fieldName)
    {
        if (!TryGetValue(fieldName, out var value))
            return Array.Empty<string>();

        if (value is not LuaTableValue table)
            return Array.Empty<string>();

        return table.Items
            .Select(item => item switch
            {
                LuaStringValue stringValue =>
                    stringValue.Value,

                LuaIdentifierValue identifierValue =>
                    identifierValue.Identifier,

                _ => null
            })
            .Where(item => item is not null)
            .Cast<string>()
            .ToList();
    }
}