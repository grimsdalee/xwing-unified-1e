using System.Text.RegularExpressions;
using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.Lua.Parsing;

public static class LuaDatabaseParser
{
    public static List<LuaEntity> Parse(
        string text,
        string tableName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var pattern =
            $@"{Regex.Escape(tableName)}\s*" +
            @"\[\s*(['""])(?<id>.*?)\1\s*\]\s*=\s*";

        var entryRegex = new Regex(
            pattern,
            RegexOptions.Compiled);

        var entities = new List<LuaEntity>();

        foreach (Match match in entryRegex.Matches(text))
        {
            var valueStart = match.Index + match.Length;

            var valueParser = new LuaValueParser(
                text,
                valueStart);

            var value = valueParser.ParseValue();

            if (value is not LuaTableValue tableValue)
            {
                throw new LuaParseException(
                    $"{tableName}['{match.Groups["id"].Value}'] " +
                    "does not contain a Lua table",
                    valueStart);
            }

            entities.Add(new LuaEntity
            {
                TableName = tableName,
                Id = match.Groups["id"].Value,
                Value = tableValue,
                SourceIndex = match.Index
            });
        }

        return entities;
    }

    public static List<LuaEntity> ParseFile(
        string filePath,
        string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Lua database file not found: {filePath}",
                filePath);
        }

        return Parse(
            File.ReadAllText(filePath),
            tableName);
    }
}