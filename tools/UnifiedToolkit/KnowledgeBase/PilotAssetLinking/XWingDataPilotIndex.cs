using System.Globalization;
using System.Text.Json;

namespace UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

public sealed class XWingDataPilotIndex
{
    private readonly List<XWingDataPilotRecord> records;

    private XWingDataPilotIndex(List<XWingDataPilotRecord> records)
    {
        this.records = records;
    }

    public static XWingDataPilotIndex Load(string path)
    {
        if (!File.Exists(path))
            return new XWingDataPilotIndex(new List<XWingDataPilotRecord>());

        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Expected an array of pilot records in '{path}'.");

        var records = new List<XWingDataPilotRecord>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            records.Add(new XWingDataPilotRecord
            {
                Name = ReadString(element, "name"),
                Xws = ReadString(element, "xws"),
                Ship = ReadString(element, "ship"),
                Skill = ReadNullableInt(element, "skill"),
                Points = ReadNullableInt(element, "points"),
                Faction = ReadString(element, "faction"),
                Image = ReadString(element, "image")
            });
        }

        return new XWingDataPilotIndex(records);
    }

    public XWingDataPilotRecord? Resolve(FirstEditionPilotRecord pilot)
    {
        var candidates = records
            .Where(record => Same(record.Name, pilot.Name))
            .Where(record => Same(record.Faction, pilot.Faction))
            .Where(record => CompatibleShip(record.Ship, pilot.ShipId))
            .ToList();

        if (candidates.Count == 0)
            return null;

        var exact = candidates
            .Where(record => record.Skill.HasValue && record.Skill.Value == pilot.PilotSkill)
            .Where(record => record.Points.HasValue && record.Points.Value == pilot.SquadPointCost)
            .ToList();

        if (exact.Count == 1)
            return exact[0];

        var skillMatches = candidates
            .Where(record => record.Skill.HasValue && record.Skill.Value == pilot.PilotSkill)
            .ToList();

        if (skillMatches.Count == 1)
            return skillMatches[0];

        var pointMatches = candidates
            .Where(record => record.Points.HasValue && record.Points.Value == pilot.SquadPointCost)
            .ToList();

        return pointMatches.Count == 1 ? pointMatches[0] : null;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => string.Empty
        };
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return number;
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool Same(string? left, string? right) =>
        PilotIdentityProfile.Compact(left ?? string.Empty)
            .Equals(PilotIdentityProfile.Compact(right ?? string.Empty), StringComparison.OrdinalIgnoreCase);

    private static bool CompatibleShip(string? dataShip, string semanticShip)
    {
        var left = PilotIdentityProfile.Compact(dataShip ?? string.Empty);
        var right = PilotIdentityProfile.Compact(semanticShip);
        if (left.Length == 0 || right.Length == 0)
            return false;

        return left.Equals(right, StringComparison.OrdinalIgnoreCase)
            || left.Contains(right, StringComparison.OrdinalIgnoreCase)
            || right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class XWingDataPilotRecord
{
    public string Name { get; init; } = string.Empty;
    public string Xws { get; init; } = string.Empty;
    public string Ship { get; init; } = string.Empty;
    public int? Skill { get; init; }
    public int? Points { get; init; }
    public string Faction { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
}
