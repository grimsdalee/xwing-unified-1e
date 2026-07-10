using System.Text.RegularExpressions;

namespace UnifiedToolkit.XWing;

public static class LuaTableEntryScanner
{
    public static List<LuaEntryBlock> Scan(
        string text,
        string tableName)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException(
                "A Lua table name is required.",
                nameof(tableName));
        }

        var entryStartRegex = new Regex(
            $@"{Regex.Escape(tableName)}\s*\[\s*['""](?<id>[^'""]*)['""]\s*\]\s*=\s*\{{",
            RegexOptions.Compiled);

        var results = new List<LuaEntryBlock>();

        foreach (Match match in entryStartRegex.Matches(text))
        {
            var openingBraceIndex = text.IndexOf(
                '{',
                match.Index + match.Length - 1);

            if (openingBraceIndex < 0)
                continue;

            var closingBraceIndex = FindMatchingBrace(
                text,
                openingBraceIndex);

            if (closingBraceIndex < 0)
            {
                throw new InvalidDataException(
                    $"Could not find the closing brace for " +
                    $"{tableName}['{match.Groups["id"].Value}'].");
            }

            results.Add(new LuaEntryBlock
            {
                Id = match.Groups["id"].Value,
                StartIndex = match.Index,
                Text = text[
                    match.Index..
                    (closingBraceIndex + 1)]
            });
        }

        return results;
    }

    private static int FindMatchingBrace(
        string text,
        int openingBraceIndex)
    {
        var depth = 0;
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var inLineComment = false;
        var inBlockComment = false;
        var escaped = false;

        for (var index = openingBraceIndex;
             index < text.Length;
             index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length
                ? text[index + 1]
                : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                    inLineComment = false;

                continue;
            }

            if (inBlockComment)
            {
                if (current == ']' && next == ']')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

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

            if (current == '-' && next == '-')
            {
                if (index + 3 < text.Length &&
                    text[index + 2] == '[' &&
                    text[index + 3] == '[')
                {
                    inBlockComment = true;
                    index += 3;
                }
                else
                {
                    inLineComment = true;
                    index++;
                }

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
}