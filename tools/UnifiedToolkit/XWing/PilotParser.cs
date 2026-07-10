namespace UnifiedToolkit.XWing;

public static class PilotParser
{
    public static List<PilotDefinition> ParseFromRepo(
        string repoFolder)
    {
        var path = Path.Combine(
            repoFolder,
            "TTS_xwing",
            "src",
            "Game",
            "Component",
            "Spawner",
            "PilotDb.lua");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"PilotDb.lua not found: {path}",
                path);
        }

        var text = File.ReadAllText(path);

        return Parse(text);
    }

    public static List<PilotDefinition> Parse(
        string text)
    {
        var pilots = new List<PilotDefinition>();

        var entries = LuaTableEntryScanner.Scan(
            text,
            "masterPilotDB");

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
                continue;

            var name = LuaFieldReader.ReadString(
                entry.Text,
                "name");
            
            var pilot = new PilotDefinition
            {
                Id = entry.Id,
                Name = name,
                Title = LuaFieldReader.ReadString(
                    entry.Text,
                    "title"),

                Faction = LuaFieldReader.ReadString(
                    entry.Text,
                    "faction"),

                ShipType = LuaFieldReader.ReadString(
                    entry.Text,
                    "ship_type"),

                Initiative = LuaFieldReader.ReadInt(
                    entry.Text,
                    "initiative"),

                Limited = LuaFieldReader.ReadInt(
                    entry.Text,
                    "limited"),

                Force = LuaFieldReader.ReadInt(
                    entry.Text,
                    "force"),

                Charges = LuaFieldReader.ReadInt(
                    entry.Text,
                    "charge"),

                ShieldModifier = LuaFieldReader.ReadInt(
                    entry.Text,
                    "shield"),

                Texture = LuaFieldReader.ReadString(
                    entry.Text,
                    "texture"),

                Docking = LuaFieldReader.ReadBool(
                    entry.Text,
                    "docking")
            };

            pilot.Actions.AddRange(
                LuaFieldReader.ReadStringList(
                    entry.Text,
                    "action_set"));

            pilot.Keywords.AddRange(
                LuaFieldReader.ReadStringList(
                    entry.Text,
                    "keywords"));

            pilot.AddedSlots.AddRange(
                LuaFieldReader.ReadStringList(
                    entry.Text,
                    "add_slots"));

            pilots.Add(pilot);
        }

        return pilots
            .OrderBy(x => x.Faction)
            .ThenBy(x => x.ShipType)
            .ThenByDescending(x => x.Initiative)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToList();
    }
}