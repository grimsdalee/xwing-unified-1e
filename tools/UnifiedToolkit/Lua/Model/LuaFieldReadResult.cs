namespace UnifiedToolkit.Lua.Model;

public enum LuaFieldReadStatus
{
    Success,
    Missing,
    WrongType,
    OutOfRange
}

public readonly record struct LuaFieldReadResult<T>
{
    private LuaFieldReadResult(
        LuaFieldReadStatus status,
        T? value,
        LuaValueKind? actualKind)
    {
        Status = status;
        Value = value;
        ActualKind = actualKind;
    }

    public LuaFieldReadStatus Status { get; }

    public T? Value { get; }

    public LuaValueKind? ActualKind { get; }

    public bool IsSuccess =>
        Status == LuaFieldReadStatus.Success;

    public static LuaFieldReadResult<T> Success(
        T value)
    {
        return new LuaFieldReadResult<T>(
            LuaFieldReadStatus.Success,
            value,
            actualKind: null);
    }

    public static LuaFieldReadResult<T> Missing()
    {
        return new LuaFieldReadResult<T>(
            LuaFieldReadStatus.Missing,
            default,
            actualKind: null);
    }

    public static LuaFieldReadResult<T> WrongType(
        LuaValueKind actualKind)
    {
        return new LuaFieldReadResult<T>(
            LuaFieldReadStatus.WrongType,
            default,
            actualKind);
    }

    public static LuaFieldReadResult<T> OutOfRange(
        LuaValueKind actualKind)
    {
        return new LuaFieldReadResult<T>(
            LuaFieldReadStatus.OutOfRange,
            default,
            actualKind);
    }

    public T ValueOrDefault(T defaultValue)
    {
        return IsSuccess && Value is not null
            ? Value
            : defaultValue;
    }
}