using UnifiedToolkit.Lua.Model;

namespace UnifiedToolkit.Lua.Parsing;

public static class LuaSchemaAnalyzer
{
    public static LuaDatabaseSchema Analyze(
        string databaseName,
        string tableName,
        IEnumerable<LuaEntity> entities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databaseName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            tableName);

        ArgumentNullException.ThrowIfNull(entities);

        var entityList = entities.ToList();

        var fields = new Dictionary<string, LuaFieldSchema>(
            StringComparer.Ordinal);

        foreach (var entity in entityList)
        {
            foreach (var field in entity.Fields)
            {
                if (!fields.TryGetValue(
                        field.Key,
                        out var schema))
                {
                    schema = new LuaFieldSchema
                    {
                        FieldName = field.Key
                    };

                    fields.Add(field.Key, schema);
                }

                schema.Observe(field.Value);
            }
        }

        return new LuaDatabaseSchema
        {
            DatabaseName = databaseName,
            TableName = tableName,
            EntityCount = entityList.Count,

            Fields = fields.Values
                .OrderByDescending(
                    field => field.OccurrenceCount)
                .ThenBy(
                    field => field.FieldName,
                    StringComparer.Ordinal)
                .ToList()
        };
    }
}