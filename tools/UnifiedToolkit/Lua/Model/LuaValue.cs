namespace UnifiedToolkit.Lua.Model;

public enum LuaValueKind
{
    String,
    Number,
    Boolean,
    Nil,
    Identifier,
    Expression,
    Table
}

public abstract class LuaValue
{
    public abstract LuaValueKind Kind { get; }

    public virtual string ToDisplayString()
    {
        return ToString() ?? string.Empty;
    }
}

public sealed class LuaStringValue : LuaValue
{
    public LuaStringValue(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public override LuaValueKind Kind =>
        LuaValueKind.String;

    public override string ToString() => Value;
}

public sealed class LuaNumberValue : LuaValue
{
    public LuaNumberValue(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public override LuaValueKind Kind =>
        LuaValueKind.Number;

    public override string ToString()
    {
        return Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class LuaBooleanValue : LuaValue
{
    public LuaBooleanValue(bool value)
    {
        Value = value;
    }

    public bool Value { get; }

    public override LuaValueKind Kind =>
        LuaValueKind.Boolean;

    public override string ToString()
    {
        return Value ? "true" : "false";
    }
}

public sealed class LuaNilValue : LuaValue
{
    private LuaNilValue()
    {
    }

    public static LuaNilValue Instance { get; } = new();

    public override LuaValueKind Kind =>
        LuaValueKind.Nil;

    public override string ToString() => "nil";
}

public sealed class LuaIdentifierValue : LuaValue
{
    public LuaIdentifierValue(string identifier)
    {
        Identifier = identifier;
    }

    public string Identifier { get; }

    public override LuaValueKind Kind =>
        LuaValueKind.Identifier;

    public override string ToString() => Identifier;
}

public sealed class LuaExpressionValue : LuaValue
{
    public LuaExpressionValue(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        Expression = expression;
    }

    public string Expression { get; }

    public override LuaValueKind Kind =>
        LuaValueKind.Expression;

    public override string ToString() => Expression;
}