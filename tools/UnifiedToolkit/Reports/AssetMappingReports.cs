using System.Text;
using System.Text.Json;
using UnifiedToolkit.Assets;

namespace UnifiedToolkit.Reports;

public static class AssetMappingReports
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void WriteJson<T>(T value, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8);
    }

    public static void WriteValidation(IEnumerable<string> issues, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("Severity,Code,Message");
        foreach (var issue in issues)
            writer.WriteLine($"Error,AssetMappingValidation,{Csv(issue)}");
    }

    public static void WriteSummary(AssetResolutionApprovalResult result, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("Category,Count");
        writer.WriteLine($"ApprovedIndividual,{result.Mappings.Count}");
        writer.WriteLine($"SharedAssetDefinitions,{result.SharedAssets.Count}");
        writer.WriteLine($"SharedAssignments,{result.SharedAssets.Sum(x => x.SemanticKeys.Count)}");
        writer.WriteLine($"PendingReview,{result.PendingReview}");
        writer.WriteLine($"OptionalNotRequired,{result.OptionalNotRequired}");
        writer.WriteLine($"MissingRequired,{result.MissingRequired}");
        writer.WriteLine($"ValidationIssues,{result.ValidationIssues.Count}");
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n')) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
