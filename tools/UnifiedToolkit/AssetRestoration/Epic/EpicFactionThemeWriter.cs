using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicFactionThemeWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Write(
        string repositoryRoot,
        string? outputPath = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        outputPath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-faction-themes.json");
        outputPath = Path.GetFullPath(outputPath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(outputPath)
            ?? throw new InvalidDataException(
                "Faction-theme output has no parent directory."));

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(
                EpicFactionThemeCatalogue.All.Values,
                JsonOptions),
            new UTF8Encoding(false));

        return outputPath;
    }
}
