using UnifiedToolkit.Lua.Model;
using UnifiedToolkit.Lua.Parsing;

namespace UnifiedToolkit.XWing;

public static class PilotParser
{
    private const string PilotTableName =
        "masterPilotDB";

    public static List<PilotDefinition> ParseFromRepo(
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
            "PilotDb.lua");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"PilotDb.lua not found: {path}",
                path);
        }

        var entities = LuaDatabaseParser.ParseFile(
            path,
            PilotTableName);

        return MapSemanticCandidates(entities);
    }

    public static List<PilotDefinition> Parse(
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var entities = LuaDatabaseParser.Parse(
            text,
            PilotTableName);

        return MapSemanticCandidates(entities);
    }

    private static List<PilotDefinition>
        MapSemanticCandidates(
            IEnumerable<LuaEntity> entities)
    {
        var classifications =
            PilotEntityClassifier.Classify(entities);

        var semanticEntities = classifications
            .Where(item => item.IsSemanticCandidate)
            .Select(item => item.Entity);

        return PilotMapper.MapMany(semanticEntities);
    }
}