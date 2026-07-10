namespace UnifiedToolkit.Lua.Model;

public sealed class LuaFieldSchema
{
    private readonly HashSet<LuaValueKind> _valueKinds = new();
    private readonly List<string> _exampleValues = new();

    public required string FieldName { get; init; }

    public int OccurrenceCount { get; private set; }

    public IReadOnlyCollection<LuaValueKind> ValueKinds =>
        _valueKinds;

    public IReadOnlyList<string> ExampleValues =>
        _exampleValues;

    public bool HasMixedTypes =>
        _valueKinds.Count > 1;

    public void Observe(
        LuaValue value,
        int maximumExamples = 5)
    {
        ArgumentNullException.ThrowIfNull(value);

        OccurrenceCount++;
        _valueKinds.Add(value.Kind);

        if (_exampleValues.Count >= maximumExamples)
            return;

        var example = LuaValueFormatter.Format(value);

        if (string.IsNullOrWhiteSpace(example))
            return;

        if (_exampleValues.Contains(
                example,
                StringComparer.Ordinal))
        {
            return;
        }

        _exampleValues.Add(example);
    }
}