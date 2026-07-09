using UnifiedToolkit.Models;

namespace UnifiedToolkit.Reports;

public interface IReport
{
    string FileName { get; }

    void Generate(
        TtsGame game,
        IReadOnlyList<TtsObject> objects,
        string reportsFolder);
}