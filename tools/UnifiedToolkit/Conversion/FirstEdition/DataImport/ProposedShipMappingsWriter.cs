using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.Mapping;

namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public static class ProposedShipMappingsWriter
{
    public static string Write(
        string outputFolder,
        IReadOnlyList<ShipMapping> existingMappings,
        IReadOnlyList<OfficialShipMatch> matches)
    {
        Directory.CreateDirectory(outputFolder);
        var path = Path.Combine(outputFolder, "ships.proposed.json");

        var proposed = existingMappings
            .Concat(matches
                .Where(match => match.Decision == "ProposedDirect" && match.ProposedMapping is not null)
                .Select(match => match.ProposedMapping!))
            .GroupBy(mapping => mapping.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(mapping => mapping.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        File.WriteAllText(path, JsonSerializer.Serialize(proposed, options));
        return path;
    }
}
