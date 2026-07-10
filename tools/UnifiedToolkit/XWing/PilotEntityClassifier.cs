using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.XWing;

public static class PilotEntityClassifier
{
    public static List<LuaEntityClassification> Classify(
        IEnumerable<LuaEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        return entities
            .Select(Classify)
            .ToList();
    }

    private static LuaEntityClassification Classify(
        LuaEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Id) &&
            string.IsNullOrWhiteSpace(
                entity.GetString("name")) &&
            entity.GetString("faction")
                .Equals(
                    "dummy",
                    StringComparison.OrdinalIgnoreCase))
        {
            return new LuaEntityClassification
            {
                Entity = entity,
                Classification = "Ignored",
                Reason = "Dummy PilotDb template entry"
            };
        }

        return new LuaEntityClassification
        {
            Entity = entity,
            Classification = "SemanticCandidate",
            Reason = ""
        };
    }
}