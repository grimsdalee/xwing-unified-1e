using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Conversion.Mapping.Dispositions;
using UnifiedToolkit.Conversion.Mapping.Pilots;
using UnifiedToolkit.Conversion.Mapping.Upgrades;

namespace UnifiedToolkit.Conversion.Mapping;

public static class ConversionMappingLoader
{
    public static ConversionMappingSet Load(string mappingFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingFolder);
        var folder = Path.GetFullPath(mappingFolder);
        var options = CreateOptions();
        var manifestPath = Path.Combine(folder, "mapping-set.json");
        var shipsPath = Path.Combine(folder, "ships.json");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Conversion mapping manifest not found.", manifestPath);
        if (!File.Exists(shipsPath)) throw new FileNotFoundException("Ship mapping file not found.", shipsPath);
        var manifest = JsonSerializer.Deserialize<MappingManifest>(File.ReadAllText(manifestPath), options) ?? throw new InvalidDataException("Unable to deserialize mapping-set.json.");
        return new ConversionMappingSet
        {
            Version = manifest.Version,
            Ships = ReadRequired<ShipMapping>(shipsPath, options),
            ShipDispositions = ReadOptional<ShipDisposition>(Path.Combine(folder,"ship-dispositions.json"), options),
            Pilots = ReadOptional<PilotMapping>(Path.Combine(folder,"pilots.json"), options),
            PilotSourceAlternates = ReadOptional<PilotSourceAlternate>(Path.Combine(folder,"pilot-source-alternates.json"), options),
            PilotDispositions = ReadOptional<PilotDisposition>(Path.Combine(folder,"pilot-dispositions.json"), options),
            Upgrades = ReadOptional<UpgradeMapping>(Path.Combine(folder,"upgrades.json"), options),
            UpgradeSourceAlternates = ReadOptional<UpgradeSourceAlternate>(Path.Combine(folder,"upgrade-source-alternates.json"), options),
            UpgradeDispositions = ReadOptional<UpgradeDisposition>(Path.Combine(folder,"upgrade-dispositions.json"), options)
        };
    }
    private static IReadOnlyList<T> ReadRequired<T>(string path,JsonSerializerOptions options) => JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path),options) ?? throw new InvalidDataException($"Unable to deserialize {Path.GetFileName(path)}.");
    private static IReadOnlyList<T> ReadOptional<T>(string path,JsonSerializerOptions options) => File.Exists(path) ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path),options) ?? new List<T>() : new List<T>();
    private static JsonSerializerOptions CreateOptions(){var o=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,ReadCommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true};o.Converters.Add(new JsonStringEnumConverter());return o;}
    private sealed class MappingManifest { public string Version { get; init; } = ""; }
}
