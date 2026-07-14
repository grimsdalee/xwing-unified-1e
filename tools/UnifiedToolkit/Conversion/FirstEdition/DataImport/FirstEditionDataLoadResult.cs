namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public sealed class FirstEditionDataLoadResult
{
    public IReadOnlyList<FirstEditionDataShip> Ships { get; init; } = Array.Empty<FirstEditionDataShip>();
    public IReadOnlyList<FirstEditionDataSourceFile> SourceFiles { get; init; } = Array.Empty<FirstEditionDataSourceFile>();
}

public sealed class FirstEditionDataSourceFile
{
    public string Path { get; init; } = "";
    public string DataType { get; init; } = "";
    public int RecordsRead { get; init; }
    public string Notes { get; init; } = "";
}
