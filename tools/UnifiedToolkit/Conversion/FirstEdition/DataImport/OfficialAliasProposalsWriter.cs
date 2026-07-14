using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public static class OfficialAliasProposalsWriter
{
    public static string Write(string outputFolder, IReadOnlyList<OfficialAliasCandidate> candidates)
    {
        Directory.CreateDirectory(outputFolder);
        var path = Path.Combine(outputFolder, "official-alias-mappings.proposed.json");
        var mappings = candidates
            .Where(x => x.Decision == "ProposedAlias" && x.ProposedMapping is not null)
            .Select(x => x.ProposedMapping!)
            .OrderBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(mappings, options) + Environment.NewLine);
        return path;
    }
}
