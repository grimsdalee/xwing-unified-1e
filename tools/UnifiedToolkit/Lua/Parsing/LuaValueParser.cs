using System.Globalization;
using System.Text;
using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.Lua.Parsing;

public sealed class LuaValueParser
{
    private readonly string _text;
    private int _position;

    public LuaValueParser(
        string text,
        int startPosition = 0)
    {
        ArgumentNullException.ThrowIfNull(text);

        _text = text;
        _position = startPosition;
    }

    public int Position => _position;

    public LuaValue ParseValue()
    {
        SkipTrivia();

        if (IsAtEnd)
        {
            throw Error(
                "Expected a Lua value, but reached the end of input");
        }

        var current = Current;

        if (current == '{')
            return ParseTable();

        if (current == '\'' || current == '"')
            return new LuaStringValue(ParseString());

        if (current == '-' || char.IsDigit(current))
            return ParseNumber();

        if (IsIdentifierStart(current))
            return ParseIdentifierValue();

        throw Error(
            $"Unexpected character '{current}' while reading a value");
    }

    private LuaTableValue ParseTable()
    {
        Expect('{');

        var table = new LuaTableValue();

        SkipTrivia();

        while (!IsAtEnd && Current != '}')
        {
            var startPosition = _position;

            if (TryParseFieldKey(out var key))
            {
                SkipTrivia();

                if (TryConsume('='))
                {
                    var value = ParseValue();
                    table.SetField(key, value);
                }
                else
                {
                    _position = startPosition;

                    var value = ParseValue();
                    table.AddItem(value);
                }
            }
            else
            {
                _position = startPosition;

                var value = ParseValue();
                table.AddItem(value);
            }

            SkipTrivia();

            if (TryConsume(',') || TryConsume(';'))
            {
                SkipTrivia();
                continue;
            }

            if (Current == '}')
                break;

            throw Error(
                "Expected ',', ';', or '}' after Lua table value");
        }

        Expect('}');

        return table;
    }

    private bool TryParseFieldKey(out string key)
    {
        key = string.Empty;

        SkipTrivia();

        if (IsAtEnd)
            return false;

        var originalPosition = _position;

        if (Current == '[')
        {
            _position++;
            SkipTrivia();

            if (IsAtEnd)
            {
                _position = originalPosition;
                return false;
            }

            if (Current == '\'' || Current == '"')
            {
                key = ParseString();
            }
            else if (Current == '-' ||
                     char.IsDigit(Current))
            {
                key = ParseNumberText();
            }
            else
            {
                _position = originalPosition;
                return false;
            }

            SkipTrivia();

            if (!TryConsume(']'))
            {
                _position = originalPosition;
                key = string.Empty;
                return false;
            }

            return true;
        }

        if (!IsIdentifierStart(Current))
            return false;

        key = ParseIdentifier();
        return true;
    }

    private LuaValue ParseIdentifierValue()
    {
        var startPosition = _position;
        var identifier = ParseIdentifier();

        if (identifier == "true")
            return new LuaBooleanValue(true);

        if (identifier == "false")
            return new LuaBooleanValue(false);

        if (identifier == "nil")
            return LuaNilValue.Instance;

        SkipTrivia();

        if (IsValueTerminator(Current))
        {
            return new LuaIdentifierValue(identifier);
        }

        _position = startPosition;

        return ParseExpression();
    }

    private LuaExpressionValue ParseExpression()
    {
        var start = _position;

        var parenthesisDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var luaBlockDepth = 0;

        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var escaped = false;

        while (!IsAtEnd)
        {
            var current = Current;
            var next = Peek(1);

            if (inSingleQuotedString)
            {
                _position++;

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
                _position++;

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
                SkipComment();
                continue;
            }

            if (current == '\'')
            {
                inSingleQuotedString = true;
                _position++;
                continue;
            }

            if (current == '"')
            {
                inDoubleQuotedString = true;
                _position++;
                continue;
            }

            switch (current)
            {
                case '(':
                    parenthesisDepth++;
                    _position++;
                    continue;

                case ')':
                    if (parenthesisDepth > 0)
                        parenthesisDepth--;

                    _position++;
                    continue;

                case '[':
                    bracketDepth++;
                    _position++;
                    continue;

                case ']':
                    if (bracketDepth > 0)
                        bracketDepth--;

                    _position++;
                    continue;

                case '{':
                    braceDepth++;
                    _position++;
                    continue;

                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                        _position++;
                        continue;
                    }

                    if (parenthesisDepth == 0 &&
                        bracketDepth == 0 &&
                        luaBlockDepth == 0)
                    {
                        return CreateExpression(start);
                    }

                    _position++;
                    continue;
            }

            if (IsIdentifierStart(current))
            {
                var token = ParseIdentifier();

                switch (token)
                {
                    case "function":
                    case "if":
                    case "for":
                    case "while":
                    case "repeat":
                        luaBlockDepth++;
                        break;

                    case "end":
                    case "until":
                        if (luaBlockDepth > 0)
                            luaBlockDepth--;

                        break;
                }

                continue;
            }

            if ((current == ',' || current == ';') &&
                parenthesisDepth == 0 &&
                bracketDepth == 0 &&
                braceDepth == 0 &&
                luaBlockDepth == 0)
            {
                return CreateExpression(start);
            }

            _position++;
        }

        return CreateExpression(start);
    }

    private LuaExpressionValue CreateExpression(
        int startPosition)
    {
        var expression = _text[
                startPosition.._position]
            .Trim();

        if (string.IsNullOrWhiteSpace(expression))
        {
            throw Error("Expected a Lua expression");
        }

        return new LuaExpressionValue(expression);
    }

    private static bool IsValueTerminator(char value)
    {
        return value == ',' ||
            value == ';' ||
            value == '}' ||
            value == '\0';
    }

    private LuaNumberValue ParseNumber()
    {
        var numberText = ParseNumberText();

        if (!decimal.TryParse(
                numberText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw Error(
                $"Invalid Lua number '{numberText}'");
        }

        return new LuaNumberValue(value);
    }

    private string ParseNumberText()
    {
        var start = _position;

        if (Current == '-')
            _position++;

        while (!IsAtEnd &&
               char.IsDigit(Current))
        {
            _position++;
        }

        if (!IsAtEnd && Current == '.')
        {
            _position++;

            while (!IsAtEnd &&
                   char.IsDigit(Current))
            {
                _position++;
            }
        }

        if (!IsAtEnd &&
            (Current == 'e' || Current == 'E'))
        {
            _position++;

            if (!IsAtEnd &&
                (Current == '+' || Current == '-'))
            {
                _position++;
            }

            while (!IsAtEnd &&
                   char.IsDigit(Current))
            {
                _position++;
            }
        }

        return _text[start.._position];
    }

    private string ParseIdentifier()
    {
        if (IsAtEnd ||
            !IsIdentifierStart(Current))
        {
            throw Error("Expected a Lua identifier");
        }

        var start = _position;
        _position++;

        while (!IsAtEnd &&
               IsIdentifierPart(Current))
        {
            _position++;
        }

        return _text[start.._position];
    }

    private string ParseString()
    {
        var quote = Current;
        _position++;

        var builder = new StringBuilder();

        while (!IsAtEnd)
        {
            var current = Current;
            _position++;

            if (current == quote)
                return builder.ToString();

            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (IsAtEnd)
            {
                throw Error(
                    "Unterminated escape sequence in Lua string");
            }

            var escaped = Current;
            _position++;

            builder.Append(escaped switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                _ => escaped
            });
        }

        throw Error("Unterminated Lua string");
    }

    private void SkipTrivia()
    {
        while (!IsAtEnd)
        {
            if (char.IsWhiteSpace(Current))
            {
                _position++;
                continue;
            }

            if (Current == '-' &&
                Peek(1) == '-')
            {
                SkipComment();
                continue;
            }

            break;
        }
    }

    private void SkipComment()
    {
        _position += 2;

        if (Current == '[' &&
            Peek(1) == '[')
        {
            _position += 2;

            while (!IsAtEnd)
            {
                if (Current == ']' &&
                    Peek(1) == ']')
                {
                    _position += 2;
                    return;
                }

                _position++;
            }

            return;
        }

        while (!IsAtEnd &&
               Current != '\n')
        {
            _position++;
        }
    }

    private bool TryConsume(char expected)
    {
        SkipTrivia();

        if (IsAtEnd || Current != expected)
            return false;

        _position++;
        return true;
    }

    private void Expect(char expected)
    {
        SkipTrivia();

        if (IsAtEnd || Current != expected)
        {
            throw Error(
                $"Expected '{expected}'");
        }

        _position++;
    }

    private char Peek(int offset)
    {
        var index = _position + offset;

        return index >= 0 && index < _text.Length
            ? _text[index]
            : '\0';
    }

    private bool IsAtEnd =>
        _position >= _text.Length;

    private char Current =>
        IsAtEnd ? '\0' : _text[_position];

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) ||
               value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) ||
               value == '_';
    }

    private LuaParseException Error(string message)
    {
        return new LuaParseException(
            message,
            _position);
    }
}