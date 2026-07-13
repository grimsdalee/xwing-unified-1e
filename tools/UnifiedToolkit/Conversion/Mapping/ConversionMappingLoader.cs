using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Conversion.Mapping;

public static class ConversionMappingLoader
{
    public static ConversionMappingSet Load(string mappingFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingFolder);

        var fullFolder = Path.GetFullPath(mappingFolder);
        var manifestPath = Path.Combine(fullFolder, "mapping-set.json");
        var shipsPath = Path.Combine(fullFolder, "ships.json");

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Conversion mapping manifest not found.", manifestPath);

        if (!File.Exists(shipsPath))
            throw new FileNotFoundException("Ship mapping file not found.", shipsPath);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var manifest = JsonSerializer.Deserialize<MappingManifest>(File.ReadAllText(manifestPath), options)
            ?? throw new InvalidDataException("Unable to deserialize mapping-set.json.");
        var ships = JsonSerializer.Deserialize<List<ShipMapping>>(File.ReadAllText(shipsPath), options)
            ?? throw new InvalidDataException("Unable to deserialize ships.json.");

        return new ConversionMappingSet
        {
            Version = manifest.Version,
            Ships = ships
        };
    }

    private sealed class MappingManifest
    {
        public string Version { get; init; } = "";
    }
}
