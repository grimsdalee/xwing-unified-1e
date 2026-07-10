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
        var identifier = ParseIdentifier();

        return identifier switch
        {
            "true" => new LuaBooleanValue(true),
            "false" => new LuaBooleanValue(false),
            "nil" => LuaNilValue.Instance,
            _ => new LuaIdentifierValue(identifier)
        };
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