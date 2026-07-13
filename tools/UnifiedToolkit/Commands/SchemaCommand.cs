using UnifiedToolkit.Lua.Model;
using UnifiedToolkit.Lua.Parsing;
using UnifiedToolkit.Reports;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Commands;

public static class SchemaCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        var databaseKey = args[0].Trim().ToLowerInvariant();
        var repoFolder = Path.GetFullPath(args[1]);

        if (!Directory.Exists(repoFolder))
        {
            Console.Error.WriteLine(
                $"Repo folder not found: {repoFolder}");

            return 1;
        }

        if (!TryResolveDatabase(
                databaseKey,
                repoFolder,
                out var database))
        {
            Console.Error.WriteLine(
                $"Unknown database: {args[0]}");

            Console.Error.WriteLine();

            ShowUsage();
            return 1;
        }

        try
        {
            var entities = LuaDatabaseParser.ParseFile(
                database.FilePath,
                database.TableName);

            var classifications =
                ClassifyEntities(databaseKey, entities);

            var semanticCandidateCount =
                classifications.Count(
                    item => item.IsSemanticCandidate);

            var ignoredCount =
                classifications.Count - semanticCandidateCount;

            var schema = LuaSchemaAnalyzer.Analyze(
                database.DisplayName,
                database.TableName,
                entities);

            var reportsFolder = Path.Combine(
                repoFolder,
                "_unifiedtoolkit_reports");

            var reportPath = Path.Combine(
                reportsFolder,
                $"{database.ReportName}-schema.csv");

            var ignoredReportPath = Path.Combine(
                reportsFolder,
                $"{database.ReportName}-ignored-entities.csv");

            LuaSchemaReport.Write(schema, reportPath);

            IgnoredLuaEntitiesReport.Write(
                classifications,
                ignoredReportPath);

            PrintSummary(
                schema,
                database,
                reportPath,
                ignoredReportPath,
                semanticCandidateCount,
                ignoredCount);

            return 0;
        }
        catch (LuaParseException exception)
        {
            Console.Error.WriteLine(
                $"Unable to parse {database.DisplayName}: " +
                exception.Message);

            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Unable to inspect {database.DisplayName}: " +
                exception.Message);

            return 1;
        }
    }

    private static bool TryResolveDatabase(
        string databaseKey,
        string repoFolder,
        out DatabaseDescriptor database)
    {
        var spawnerFolder = Path.Combine(
            repoFolder,
            "TTS_xwing",
            "src",
            "Game",
            "Component",
            "Spawner");

        database = databaseKey switch
        {
            "pilots" or "pilot" => new DatabaseDescriptor(
                DisplayName: "PilotDb",
                TableName: "masterPilotDB",
                FilePath: Path.Combine(
                    spawnerFolder,
                    "PilotDb.lua"),
                ReportName: "pilots"),

            "ships" or "ship" => new DatabaseDescriptor(
                DisplayName: "ShipDb",
                TableName: "masterShipDB",
                FilePath: Path.Combine(
                    spawnerFolder,
                    "ShipDb.lua"),
                ReportName: "ships"),

            "upgrades" or "upgrade" => new DatabaseDescriptor(
                DisplayName: "UpgradeDb",
                TableName: "masterUpgradesDB",
                FilePath: Path.Combine(
                    spawnerFolder,
                    "UpgradeDb.lua"),
                ReportName: "upgrades"),

            _ => null!
        };

        return database is not null;
    }

    private static void PrintSummary(
    LuaDatabaseSchema schema,
    DatabaseDescriptor database,
    string reportPath,
    string ignoredReportPath,
    int semanticCandidateCount,
    int ignoredCount)
    {
        Console.WriteLine("UnifiedToolkit Schema");
        Console.WriteLine("=====================");
        Console.WriteLine();

        Console.WriteLine(
            $"Database:            {schema.DatabaseName}");

        Console.WriteLine(
            $"Lua table:           {schema.TableName}");

        Console.WriteLine(
            $"Source file:         {database.FilePath}");

        Console.WriteLine(
            $"Raw entities:        {schema.EntityCount}");

        Console.WriteLine(
            $"Semantic candidates: {semanticCandidateCount}");

        Console.WriteLine(
            $"Ignored entities:    {ignoredCount}");

        Console.WriteLine(
            $"Distinct fields:     {schema.Fields.Count}");

        Console.WriteLine(
            $"Schema report:       {reportPath}");

        Console.WriteLine(
            $"Ignored report:      {ignoredReportPath}");

        Console.WriteLine();
        Console.WriteLine("Fields");
        Console.WriteLine("------");

        foreach (var field in schema.Fields)
        {
            var kinds = string.Join(
                ", ",
                field.ValueKinds
                    .OrderBy(kind => kind)
                    .Select(kind => kind.ToString()));

            var mixedMarker = field.HasMixedTypes
                ? " [mixed]"
                : string.Empty;

            Console.WriteLine(
                $"{field.FieldName,-28} " +
                $"{field.OccurrenceCount,5}  " +
                $"{kinds}{mixedMarker}");
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");

        Console.WriteLine(
            "  UnifiedToolkit schema pilots <repo-folder>");

        Console.WriteLine(
            "  UnifiedToolkit schema ships <repo-folder>");

        Console.WriteLine(
            "  UnifiedToolkit schema upgrades <repo-folder>");
    }

    private sealed record DatabaseDescriptor(
        string DisplayName,
        string TableName,
        string FilePath,
        string ReportName);

    private static List<LuaEntityClassification>
        ClassifyEntities(
            string databaseKey,
            IEnumerable<LuaEntity> entities)
    {
        return databaseKey switch
        {
            "pilots" or "pilot" =>
                PilotEntityClassifier.Classify(entities),

            _ => entities
                .Select(entity =>
                    new LuaEntityClassification
                    {
                        Entity = entity,
                        Classification =
                            "SemanticCandidate",
                        Reason = ""
                    })
                .ToList()
        };
    }
}