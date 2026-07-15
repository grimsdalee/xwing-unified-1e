using UnifiedToolkit.Conversion.FirstEdition;
using UnifiedToolkit.Repository;

namespace UnifiedToolkit.Hybrid;

public enum FirstEditionBaseSize
{
    Small,
    Large,
    Epic
}

public sealed class FirstEditionBaseDefinition
{
    public required FirstEditionBaseSize Size { get; init; }
    public string DefinitionId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int PegCount { get; init; }
    public bool SupportsEpicMovementTool { get; init; }
    public bool IsGeneratedFromMediumFramework { get; init; }
}

public sealed class ShipBaseSizeConversion
{
    public string ShipId { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string Source25ShipId { get; init; } = "";
    public string Source25BaseSize { get; init; } = "";
    public FirstEditionBaseSize FirstEditionBaseSize { get; init; }
    public bool ConversionRequired { get; init; }
    public bool MediumRemoved { get; init; }
    public string ValidationStatus { get; init; } = "";
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public static class FirstEditionBaseDefinitionCatalogue
{
    public static IReadOnlyList<FirstEditionBaseDefinition> Definitions { get; } =
    [
        new() { Size = FirstEditionBaseSize.Small, DefinitionId = "first-edition-small", DisplayName = "First Edition Small Base", PegCount = 1 },
        new() { Size = FirstEditionBaseSize.Large, DefinitionId = "first-edition-large", DisplayName = "First Edition Large Base", PegCount = 1 },
        new() { Size = FirstEditionBaseSize.Epic, DefinitionId = "first-edition-epic", DisplayName = "First Edition Epic Base", PegCount = 2, SupportsEpicMovementTool = true }
    ];

    public static FirstEditionBaseDefinition Resolve(FirstEditionShip ship)
    {
        var size = ParseRequired(ship.Size, ship.Id, ship.Name);
        return Definitions.Single(x => x.Size == size);
    }

    public static FirstEditionBaseSize ParseRequired(string value, string shipId, string shipName)
    {
        var normalized = HybridText.Normalize(value);
        return normalized switch
        {
            "small" => FirstEditionBaseSize.Small,
            "large" => FirstEditionBaseSize.Large,
            "epic" or "huge" => FirstEditionBaseSize.Epic,
            "medium" => throw new InvalidDataException($"First Edition ship '{shipId}' ({shipName}) contains forbidden Medium base size."),
            _ => throw new InvalidDataException($"First Edition ship '{shipId}' ({shipName}) has unsupported base size '{value}'. Expected Small, Large or Epic.")
        };
    }

    public static IReadOnlyList<ShipBaseSizeConversion> BuildConversions(string repositoryFolder, FirstEditionRepository repository)
    {
        var source = RepositoryLoader.Load(Path.GetFullPath(repositoryFolder));
        var sourceById = source.Ships.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        return repository.Ships.Select(ship =>
        {
            sourceById.TryGetValue(ship.Provenance.SourceId, out var sourceShip);
            var sourceSize = sourceShip?.Size ?? "";
            var target = ParseRequired(ship.Size, ship.Id, ship.Name);
            var sourceNormalized = HybridText.Normalize(sourceSize);
            var targetNormalized = HybridText.Normalize(target.ToString());
            var conversionRequired = !string.IsNullOrWhiteSpace(sourceSize) && sourceNormalized != targetNormalized;
            var mediumRemoved = sourceNormalized == "medium";
            var notes = new List<string>();
            if (string.IsNullOrWhiteSpace(sourceSize)) notes.Add("No 2.5 source size was available; First Edition semantic size remains authoritative.");
            if (mediumRemoved) notes.Add($"2.5 Medium base is deliberately replaced by First Edition {target} base.");
            if (conversionRequired && !mediumRemoved) notes.Add($"Source size '{sourceSize}' is replaced by First Edition '{target}'.");

            return new ShipBaseSizeConversion
            {
                ShipId = ship.Id,
                ShipName = ship.Name,
                Source25ShipId = ship.Provenance.SourceId,
                Source25BaseSize = sourceSize,
                FirstEditionBaseSize = target,
                ConversionRequired = conversionRequired,
                MediumRemoved = mediumRemoved,
                ValidationStatus = "Valid",
                Notes = notes
            };
        }).OrderBy(x => x.ShipName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void ValidateNoMedium(
        IReadOnlyList<FirstEditionBaseDefinition> definitions,
        IReadOnlyList<ShipBaseSizeConversion> conversions)
    {
        if (definitions.Any(x => x.Size.ToString().Equals("Medium", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("First Edition base catalogue must not contain a Medium definition.");
        if (conversions.Any(x => x.FirstEditionBaseSize.ToString().Equals("Medium", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A generated First Edition conversion retained a Medium base.");
    }
}
