using System.Text.RegularExpressions;

namespace UnifiedToolkit.XWing;

public static class ShipParser
{
    private static readonly Regex EntryStartRegex = new(
        @"masterShipDB\[['""](?<id>[^'""]+)['""]\]\s*=\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex NameRegex = new(
        @"(?:\['name'\]|name)\s*=\s*['""](?<value>[^'""]*)['""]",
        RegexOptions.Compiled);

    private static readonly Regex SizeRegex = new(
        @"(?:\['size'\]|size)\s*=\s*['""](?<value>[^'""]*)['""]",
        RegexOptions.Compiled);

    private static readonly Regex HullRegex = new(
        @"(?:\['hull'\]|hull)\s*=\s*(?<value>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex ShieldRegex = new(
        @"(?:\['shield'\]|shield)\s*=\s*(?<value>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex AgilityRegex = new(
        @"(?:\['agility'\]|agility)\s*=\s*(?<value>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex FactionRegex = new(
        @"\[['""](?<value>[^'""]+)['""]\]\s*=\s*true",
        RegexOptions.Compiled);

    public static List<ShipDefinition> ParseFromRepo(string repoFolder)
    {
        var path = Path.Combine(
            repoFolder,
            "TTS_xwing",
            "src",
            "Game",
            "Component",
            "Spawner",
            "ShipDb.lua");

        if (!File.Exists(path))
            throw new FileNotFoundException($"ShipDb.lua not found: {path}", path);

        var text = File.ReadAllText(path);
        return Parse(text);
    }

    public static List<ShipDefinition> Parse(string text)
    {
        var entries = new List<ShipDefinition>();
        var matches = EntryStartRegex.Matches(text);

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var block = text[start..end];

            var id = matches[i].Groups["id"].Value;
            var name = MatchValue(NameRegex, block);

            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (string.IsNullOrWhiteSpace(id) || id == "0")
                continue;

            var ship = new ShipDefinition
            {
                Id = id,
                Name = name,
                Size = MatchValue(SizeRegex, block),
                Hull = MatchInt(HullRegex, block),
                Shield = MatchInt(ShieldRegex, block),
                Agility = MatchInt(AgilityRegex, block)
            };

            foreach (var faction in ExtractFactions(block))
                ship.Factions.Add(faction);

            entries.Add(ship);
        }

        return entries
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToList();
    }

    private static string MatchValue(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups["value"].Value : "";
    }

    private static int MatchInt(Regex regex, string text)
    {
        var value = MatchValue(regex, text);
        return int.TryParse(value, out var result) ? result : 0;
    }

    private static IEnumerable<string> ExtractFactions(string block)
    {
        var factionStart = block.IndexOf("factions", StringComparison.OrdinalIgnoreCase);

        if (factionStart < 0)
            yield break;

        var factionBlock = block[factionStart..Math.Min(block.Length, factionStart + 300)];

        foreach (Match match in FactionRegex.Matches(factionBlock))
        {
            var value = match.Groups["value"].Value;

            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }
}