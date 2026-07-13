namespace UnifiedToolkit.Lua.Model;

public sealed class LuaEntity
{
    public required string TableName { get; init; }

    public required string Id { get; init; }

    public required LuaTableValue Value { get; init; }

    public int SourceIndex { get; init; }

    public IReadOnlyDictionary<string, LuaValue> Fields =>
        Value.Fields;

    public bool Contains(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        return Fields.ContainsKey(fieldName);
    }

    public bool TryGetValue(
        string fieldName,
        out LuaValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        return Value.TryGetValue(fieldName, out value);
    }

    public LuaFieldReadResult<string> ReadString(
        string fieldName,
        bool allowIdentifier = false)
    {
        if (!TryGetValue(fieldName, out var value) ||
            value is null)
        {
            return LuaFieldReadResult<string>.Missing();
        }

        if (value is LuaStringValue stringValue)
        {
            return LuaFieldReadResult<string>.Success(
                stringValue.Value);
        }

        if (allowIdentifier &&
            value is LuaIdentifierValue identifierValue)
        {
            return LuaFieldReadResult<string>.Success(
                identifierValue.Identifier);
        }

        return LuaFieldReadResult<string>.WrongType(
            value.Kind);
    }

    public LuaFieldReadResult<decimal> ReadNumber(
        string fieldName)
    {
        if (!TryGetValue(fieldName, out var value) ||
            value is null)
        {
            return LuaFieldReadResult<decimal>.Missing();
        }

        if (value is LuaNumberValue numberValue)
        {
            return LuaFieldReadResult<decimal>.Success(
                numberValue.Value);
        }

        return LuaFieldReadResult<decimal>.WrongType(
            value.Kind);
    }

    public LuaFieldReadResult<int> ReadInt(
    string fieldName)
    {
        var numberResult = ReadNumber(fieldName);

        if (numberResult.Status ==
            LuaFieldReadStatus.Missing)
        {
            return LuaFieldReadResult<int>.Missing();
        }

        if (numberResult.Status ==
            LuaFieldReadStatus.WrongType)
        {
            return LuaFieldReadResult<int>.WrongType(
                numberResult.ActualKind ??
                LuaValueKind.Number);
        }

        if (!numberResult.IsSuccess)
        {
            return LuaFieldReadResult<int>.OutOfRange(
                LuaValueKind.Number);
        }

        var number = numberResult.Value;

        if (number < int.MinValue ||
            number > int.MaxValue ||
            decimal.Truncate(number) != number)
        {
            return LuaFieldReadResult<int>.OutOfRange(
                LuaValueKind.Number);
        }

        return LuaFieldReadResult<int>.Success(
            decimal.ToInt32(number));
    }

    public LuaFieldReadResult<bool> ReadBool(
        string fieldName)
    {
        if (!TryGetValue(fieldName, out var value) ||
            value is null)
        {
            return LuaFieldReadResult<bool>.Missing();
        }

        if (value is LuaBooleanValue booleanValue)
        {
            return LuaFieldReadResult<bool>.Success(
                booleanValue.Value);
        }

        return LuaFieldReadResult<bool>.WrongType(
            value.Kind);
    }

    public LuaFieldReadResult<LuaTableValue> ReadTable(
        string fieldName)
    {
        if (!TryGetValue(fieldName, out var value) ||
            value is null)
        {
            return LuaFieldReadResult<LuaTableValue>.Missing();
        }

        if (value is LuaTableValue tableValue)
        {
            return LuaFieldReadResult<LuaTableValue>.Success(
                tableValue);
        }

        return LuaFieldReadResult<LuaTableValue>.WrongType(
            value.Kind);
    }

    public LuaFieldReadResult<IReadOnlyList<string>>
        ReadStringList(
            string fieldName,
            bool allowIdentifiers = false)
    {
        var tableResult = ReadTable(fieldName);

        if (tableResult.Status ==
            LuaFieldReadStatus.Missing)
        {
            return LuaFieldReadResult<
                IReadOnlyList<string>>.Missing();
        }

        if (!tableResult.IsSuccess ||
            tableResult.Value is null)
        {
            return LuaFieldReadResult<
                IReadOnlyList<string>>.WrongType(
                    tableResult.ActualKind ??
                    LuaValueKind.Table);
        }

        var values = new List<string>();

        foreach (var item in tableResult.Value.Items)
        {
            switch (item)
            {
                case LuaStringValue stringValue:
                    values.Add(stringValue.Value);
                    break;

                case LuaIdentifierValue identifierValue
                    when allowIdentifiers:
                    values.Add(identifierValue.Identifier);
                    break;

                default:
                    return LuaFieldReadResult<
                        IReadOnlyList<string>>.WrongType(
                            item.Kind);
            }
        }

        return LuaFieldReadResult<
            IReadOnlyList<string>>.Success(values);
    }

    public LuaFieldReadResult<IReadOnlyList<string>>
    ReadEnabledKeys(string fieldName)
    {
        var tableResult = ReadTable(fieldName);

        if (tableResult.Status ==
            LuaFieldReadStatus.Missing)
        {
            return LuaFieldReadResult<
                IReadOnlyList<string>>.Missing();
        }

        if (!tableResult.IsSuccess ||
            tableResult.Value is null)
        {
            return LuaFieldReadResult<
                IReadOnlyList<string>>.WrongType(
                    tableResult.ActualKind ??
                    LuaValueKind.Table);
        }

        var keys = new List<string>();

        foreach (var field in tableResult.Value.Fields)
        {
            if (field.Value is not LuaBooleanValue booleanValue)
            {
                return LuaFieldReadResult<
                    IReadOnlyList<string>>.WrongType(
                        field.Value.Kind);
            }

            if (booleanValue.Value)
                keys.Add(field.Key);
        }

        return LuaFieldReadResult<
            IReadOnlyList<string>>.Success(keys);
    }

    public string RequireString(
        string fieldName,
        bool allowIdentifier = false)
    {
        var result = ReadString(
            fieldName,
            allowIdentifier);

        return result.IsSuccess &&
               result.Value is not null
            ? result.Value
            : throw CreateRequiredFieldException(
                fieldName,
                "String",
                result.Status,
                result.ActualKind);
    }

    public int RequireInt(string fieldName)
    {
        var result = ReadInt(fieldName);

        return result.IsSuccess
            ? result.Value
            : throw CreateRequiredFieldException(
                fieldName,
                "integer Number",
                result.Status,
                result.ActualKind);
    }

    public bool RequireBool(string fieldName)
    {
        var result = ReadBool(fieldName);

        return result.IsSuccess
            ? result.Value
            : throw CreateRequiredFieldException(
                fieldName,
                "Boolean",
                result.Status,
                result.ActualKind);
    }

    public LuaTableValue RequireTable(
        string fieldName)
    {
        var result = ReadTable(fieldName);

        return result.IsSuccess &&
               result.Value is not null
            ? result.Value
            : throw CreateRequiredFieldException(
                fieldName,
                "Table",
                result.Status,
                result.ActualKind);
    }

    /*
     * Compatibility helpers.
     *
     * Keep these until all semantic mappers have migrated to
     * the typed Read... and Require... methods.
     */

    public string GetString(
        string fieldName,
        string defaultValue = "")
    {
        return ReadString(
                fieldName,
                allowIdentifier: true)
            .ValueOrDefault(defaultValue);
    }

    public int GetInt(
        string fieldName,
        int defaultValue = 0)
    {
        return ReadInt(fieldName)
            .ValueOrDefault(defaultValue);
    }

    public bool GetBool(
        string fieldName,
        bool defaultValue = false)
    {
        return ReadBool(fieldName)
            .ValueOrDefault(defaultValue);
    }

    public IReadOnlyList<string> GetStringList(
        string fieldName)
    {
        return ReadStringList(
                fieldName,
                allowIdentifiers: true)
            .ValueOrDefault(Array.Empty<string>());
    }

    private InvalidDataException
        CreateRequiredFieldException(
            string fieldName,
            string expectedType,
            LuaFieldReadStatus status,
            LuaValueKind? actualKind)
    {
        var reason = status switch
        {
            LuaFieldReadStatus.Missing =>
                "the field is missing",

            LuaFieldReadStatus.WrongType =>
                $"its value is {actualKind}",

            LuaFieldReadStatus.OutOfRange =>
                "its numeric value is not a valid integer",

            _ =>
                "the value could not be read"
        };

        return new InvalidDataException(
            $"{TableName}['{Id}'] requires field " +
            $"'{fieldName}' to be {expectedType}, but " +
            $"{reason}. Source position: {SourceIndex}.");
    }
}