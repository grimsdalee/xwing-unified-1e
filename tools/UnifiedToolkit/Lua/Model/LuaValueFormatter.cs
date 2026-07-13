using System.Globalization;

namespace UnifiedToolkit.Lua.Model;

public static class LuaValueFormatter
{
    private const int MaximumTextLength = 100;

    public static string Format(LuaValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            LuaStringValue stringValue =>
                Quote(Trim(stringValue.Value)),

            LuaNumberValue numberValue =>
                numberValue.Value.ToString(
                    CultureInfo.InvariantCulture),

            LuaBooleanValue booleanValue =>
                booleanValue.Value ? "true" : "false",

            LuaNilValue =>
                "nil",

            LuaIdentifierValue identifierValue =>
                identifierValue.Identifier,

            LuaExpressionValue expressionValue =>
                Trim(expressionValue.Expression),

            LuaTableValue tableValue =>
                FormatTable(tableValue),

            _ =>
                value.ToDisplayString()
        };
    }

    private static string FormatTable(
        LuaTableValue table)
    {
        if (table.Fields.Count == 0 &&
            table.Items.Count == 0)
        {
            return "{}";
        }

        var parts = new List<string>();

        foreach (var field in table.Fields.Take(3))
        {
            parts.Add(
                $"{field.Key}={Format(field.Value)}");
        }

        foreach (var item in table.Items.Take(
                     Math.Max(0, 3 - parts.Count)))
        {
            parts.Add(Format(item));
        }

        var representedCount = parts.Count;
        var totalCount =
            table.Fields.Count + table.Items.Count;

        if (representedCount < totalCount)
            parts.Add("...");

        return Trim($"{{ {string.Join(", ", parts)} }}");
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }

    private static string Trim(string value)
    {
        if (value.Length <= MaximumTextLength)
            return value;

        return value[..(MaximumTextLength - 3)] + "...";
    }
}