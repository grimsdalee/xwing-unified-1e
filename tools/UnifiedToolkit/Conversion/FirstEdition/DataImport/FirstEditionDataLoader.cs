using System.Text.Json;

namespace UnifiedToolkit.Conversion.FirstEdition.DataImport;

public static class FirstEditionDataLoader
{
    public static IReadOnlyList<FirstEditionDataShip> LoadShips(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        var fullRoot = Path.GetFullPath(dataRoot);
        if (!Directory.Exists(fullRoot))
            throw new DirectoryNotFoundException($"First Edition data folder not found: {fullRoot}");

        var candidates = new[]
        {
            Path.Combine(fullRoot, "data", "ships.js"),
            Path.Combine(fullRoot, "data", "ships.json"),
            Path.Combine(fullRoot, "ships.js"),
            Path.Combine(fullRoot, "ships.json")
        };

        var shipsFile = candidates.FirstOrDefault(File.Exists)
            ?? Directory.EnumerateFiles(fullRoot, "ships.*", SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                    path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".js", StringComparison.OrdinalIgnoreCase));

        if (shipsFile is null)
        {
            throw new FileNotFoundException(
                "Could not find ships.js or ships.json in the First Edition data folder.");
        }

        var text = File.ReadAllText(shipsFile).Trim();
        text = UnwrapJavaScriptAssignment(text);

        using var document = JsonDocument.Parse(text, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Expected a JSON array in {shipsFile}.");

        var ships = new List<FirstEditionDataShip>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var name = ReadString(element, "name");
            var id = FirstNonEmpty(
                ReadString(element, "xws"),
                ReadString(element, "id"),
                NormaliseId(name));

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                continue;

            ships.Add(new FirstEditionDataShip
            {
                Id = id,
                Name = name,
                Size = NormaliseSize(ReadString(element, "size"), ReadBoolean(element, "large")),
                Attack = ReadInt(element, "attack"),
                Agility = ReadInt(element, "agility"),
                Hull = ReadInt(element, "hull"),
                Shields = ReadInt(element, "shields"),
                Actions = ReadStringList(element, "actions"),
                Factions = ReadStringList(element, "faction", "factions"),
                SourceFile = shipsFile
            });
        }

        return ships
            .OrderBy(ship => ship.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(ship => ship.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string UnwrapJavaScriptAssignment(string text)
    {
        var firstBracket = text.IndexOf('[');
        var lastBracket = text.LastIndexOf(']');

        if (firstBracket >= 0 && lastBracket > firstBracket)
            return text[firstBracket..(lastBracket + 1)];

        return text;
    }

    private static string ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return "";

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    private static int ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            return number;

        return 0;
    }

    private static bool ReadBoolean(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               value.GetBoolean();
    }

    private static List<string> ReadStringList(JsonElement element, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!element.TryGetProperty(property, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return new List<string> { NormaliseToken(value.GetString() ?? "") };

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => NormaliseToken(item.GetString() ?? ""))
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        return new List<string>();
    }

    private static string NormaliseSize(string size, bool large)
    {
        if (!string.IsNullOrWhiteSpace(size))
            return NormaliseToken(size);

        return large ? "large" : "small";
    }

    private static string NormaliseToken(string value)
    {
        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    public static string NormaliseId(string value) => NormaliseToken(value);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
