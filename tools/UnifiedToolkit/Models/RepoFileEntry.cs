namespace UnifiedToolkit.Models;

public sealed class RepoFileEntry
{
    public string Path { get; init; } = "";
    public string Extension { get; init; } = "";
    public string Category { get; init; } = "";
    public long SizeBytes { get; init; }
    public int LineCount { get; init; }
}