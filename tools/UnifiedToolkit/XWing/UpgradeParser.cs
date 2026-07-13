using UnifiedToolkit.Lua.Parsing;

namespace UnifiedToolkit.XWing;

public static class UpgradeParser
{
    private const string UpgradeTableName =
        "masterUpgradesDB";

    public static List<UpgradeDefinition> ParseFromRepo(
        string repoFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            repoFolder);

        var path = Path.Combine(
            repoFolder,
            "TTS_xwing",
            "src",
            "Game",
            "Component",
            "Spawner",
            "UpgradeDb.lua");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"UpgradeDb.lua not found: {path}",
                path);
        }

        var entities = LuaDatabaseParser.ParseFile(
            path,
            UpgradeTableName);

        return UpgradeMapper.MapMany(entities);
    }

    public static List<UpgradeDefinition> Parse(
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var entities = LuaDatabaseParser.Parse(
            text,
            UpgradeTableName);

        return UpgradeMapper.MapMany(entities);
    }
}