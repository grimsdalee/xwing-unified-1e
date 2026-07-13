using System.Globalization;
using System.Text;
using UnifiedToolkit.Conversion.Mapping.Candidates;

namespace UnifiedToolkit.Reports;

public static class ShipMappingCandidatesReport
{
    public const string FileName = "ship-mapping-candidates.csv";

    public static string Write(
        string reportsFolder,
        IEnumerable<ShipMappingCandidate> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportsFolder);
        ArgumentNullException.ThrowIfNull(candidates);

        Directory.CreateDirectory(reportsFolder);
        var path = Path.Combine(reportsFolder, FileName);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine(
            "SourceId,SourceName,SourceFaction,SourceSize,SourceHull,SourceShields,SourceAgility," +
            "SuggestedTargetId,SuggestedTargetName,MatchMethod,Confidence,Decision," +
            "ExistingMappingId,ExistingConversionKind,Notes");

        foreach (var candidate in candidates)
        {
            writer.WriteLine(string.Join(",",
                Csv(candidate.SourceId),
                Csv(candidate.SourceName),
                Csv(candidate.SourceFaction),
                Csv(candidate.SourceSize),
                candidate.SourceHull.ToString(CultureInfo.InvariantCulture),
                candidate.SourceShields.ToString(CultureInfo.InvariantCulture),
                candidate.SourceAgility.ToString(CultureInfo.InvariantCulture),
                Csv(candidate.SuggestedTargetId),
                Csv(candidate.SuggestedTargetName),
                Csv(candidate.MatchMethod),
                candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
                Csv(candidate.Decision),
                Csv(candidate.ExistingMappingId),
                Csv(candidate.ExistingConversionKind),
                Csv(candidate.Notes)));
        }

        return path;
    }

    private static string Csv(string value)
    {
        value ??= "";

        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            value = $"\"{value}\"";

        return value;
    }
}
