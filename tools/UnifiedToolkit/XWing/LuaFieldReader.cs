using System.Globalization;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.XWing;

public static class LuaFieldReader
{
    public static string ReadString(
        string block,
        string fieldName)
    {
        var match = CreateStringFieldRegex(fieldName).Match(block);

        return match.Success
            ? UnescapeLuaString(match.Groups["value"].Value)
            : "";
    }

    public static int ReadInt(
        string block,
        string fieldName)
    {
        var match = CreateNumberFieldRegex(fieldName).Match(block);

        if (!match.Success)
            return 0;

        return int.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : 0;
    }

    public static bool ReadBool(
        string block,
        string fieldName)
    {
        var match = CreateBooleanFieldRegex(fieldName).Match(block);

        return match.Success &&
               bool.TryParse(
                   match.Groups["value"].Value,
                   out var result) &&
               result;
    }

    public static List<string> ReadStringList(
        string block,
        string fieldName)
    {
        var tableText = ReadTable(block, fieldName);

        if (string.IsNullOrWhiteSpace(tableText))
            return new List<string>();

        var values = new List<string>();

        var stringRegex = new Regex(
            @"['""](?<value>(?:\\.|[^'""])*)['""]",
            RegexOptions.Compiled);

        foreach (Match match in stringRegex.Matches(tableText))
        {
            var value = UnescapeLuaString(
                match.Groups["value"].Value);

            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
        }

        return values;
    }

    public static string ReadTable(
        string block,
        string fieldName)
    {
        var fieldRegex = new Regex(
            $@"(?:\[\s*['""]{Regex.Escape(fieldName)}['""]\s*\]|" +
            $@"\b{Regex.Escape(fieldName)}\b)\s*=\s*\{{",
            RegexOptions.Compiled);

        var match = fieldRegex.Match(block);

        if (!match.Success)
            return "";

        var openingBraceIndex = block.IndexOf(
            '{',
            match.Index + match.Length - 1);

        if (openingBraceIndex < 0)
            return "";

        var closingBraceIndex = FindMatchingBrace(
            block,
            openingBraceIndex);

        if (closingBraceIndex < 0)
            return "";

        return block[
            openingBraceIndex..
            (closingBraceIndex + 1)];
    }

    private static Regex CreateStringFieldRegex(
        string fieldName)
    {
        return new Regex(
            $@"(?:\[\s*['""]{Regex.Escape(fieldName)}['""]\s*\]|" +
            $@"\b{Regex.Escape(fieldName)}\b)\s*=\s*" +
            @"(?<quote>['""])(?<value>(?:\\.|(?!\k<quote>).)*)\k<quote>",
            RegexOptions.Compiled);
    }

    private static Regex CreateNumberFieldRegex(
        string fieldName)
    {
        return new Regex(
            $@"(?:\[\s*['""]{Regex.Escape(fieldName)}['""]\s*\]|" +
            $@"\b{Regex.Escape(fieldName)}\b)\s*=\s*" +
            @"(?<value>-?\d+)",
            RegexOptions.Compiled);
    }

    private static Regex CreateBooleanFieldRegex(
        string fieldName)
    {
        return new Regex(
            $@"(?:\[\s*['""]{Regex.Escape(fieldName)}['""]\s*\]|" +
            $@"\b{Regex.Escape(fieldName)}\b)\s*=\s*" +
            @"(?<value>true|false)",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);
    }

    private static int FindMatchingBrace(
        string text,
        int openingBraceIndex)
    {
        var depth = 0;
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var escaped = false;

        for (var index = openingBraceIndex;
             index < text.Length;
             index++)
        {
            var current = text[index];

            if (inSingleQuotedString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '\'')
                    inSingleQuotedString = false;

                continue;
            }

            if (inDoubleQuotedString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                    inDoubleQuotedString = false;

                continue;
            }

            if (current == '\'')
            {
                inSingleQuotedString = true;
                continue;
            }

            if (current == '"')
            {
                inDoubleQuotedString = true;
                continue;
            }

            if (current == '{')
            {
                depth++;
                continue;
            }

            if (current != '}')
                continue;

            depth--;

            if (depth == 0)
                return index;
        }

        return -1;
    }

    private static string UnescapeLuaString(
        string value)
    {
        return value
            .Replace("\\'", "'")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\");
    }
}