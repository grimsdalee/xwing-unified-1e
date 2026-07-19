using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class LegacyPilotSourceInspectionService
{
    private static readonly Regex UrlRegex = new(@"^https?://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public LegacyPilotSourceInspectionResult Inspect(
        string repositoryRoot,
        string pilotName,
        string? legacySavePath = null,
        string? outputFolder = null)
    {
        if (string.IsNullOrWhiteSpace(pilotName))
            throw new ArgumentException("Pilot name must not be empty.", nameof(pilotName));

        var root = Path.GetFullPath(repositoryRoot);
        var savePath = FindLegacySave(root, legacySavePath);
        var safeName = Regex.Replace(pilotName.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var output = Path.GetFullPath(outputFolder ?? Path.Combine(root, "ukb", "reports", "legacy-pilot-source-inspection", safeName));
        Directory.CreateDirectory(output);

        using var stream = File.OpenRead(savePath);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var matches = new List<LegacyPilotObjectMatch>();
        Traverse(document.RootElement, "$", pilotName.Trim(), matches);

        var urls = new List<LegacyPilotUrlReference>();
        foreach (var match in matches)
        {
            using var objectDocument = JsonDocument.Parse(match.ObjectJson);
            CollectUrls(objectDocument.RootElement, match.JsonPath, match, urls);
        }

        var objectsJsonPath = Path.Combine(output, $"{safeName}-legacy-objects.json");
        var urlsCsvPath = Path.Combine(output, $"{safeName}-legacy-urls.csv");
        var summaryPath = Path.Combine(output, "summary.txt");

        var serialisableMatches = matches.Select(match => new
        {
            match.JsonPath,
            match.Guid,
            match.Name,
            match.Nickname,
            match.Description,
            match.ObjectType,
            Object = JsonSerializer.Deserialize<JsonElement>(match.ObjectJson)
        }).ToList();

        File.WriteAllText(
            objectsJsonPath,
            JsonSerializer.Serialize(serialisableMatches, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));

        WriteUrlCsv(urlsCsvPath, urls);
        File.WriteAllLines(summaryPath, new[]
        {
            $"Pilot: {pilotName}",
            $"Legacy save: {savePath}",
            $"Matching objects: {matches.Count}",
            $"Unique URLs: {urls.Select(x => x.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count()}",
            $"URL references: {urls.Count}",
            $"Objects report: {objectsJsonPath}",
            $"URLs report: {urlsCsvPath}"
        }, new UTF8Encoding(false));

        return new LegacyPilotSourceInspectionResult
        {
            PilotName = pilotName,
            LegacySavePath = savePath,
            MatchingObjects = matches.Count,
            UrlReferences = urls.Count,
            UniqueUrls = urls.Select(x => x.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            OutputFolder = output,
            ObjectsJsonPath = objectsJsonPath,
            UrlsCsvPath = urlsCsvPath,
            SummaryPath = summaryPath
        };
    }

    private static string FindLegacySave(string root, string? suppliedPath)
    {
        if (!string.IsNullOrWhiteSpace(suppliedPath))
        {
            var explicitPath = Path.GetFullPath(suppliedPath);
            if (!File.Exists(explicitPath))
                throw new FileNotFoundException("Legacy save JSON was not found.", explicitPath);
            return explicitPath;
        }

        var preferred = Path.Combine(root, "source", "legacy-1e", "3302209318.json");
        if (File.Exists(preferred)) return preferred;

        var legacyFolder = Path.Combine(root, "source", "legacy-1e");
        if (Directory.Exists(legacyFolder))
        {
            var candidate = Directory.EnumerateFiles(legacyFolder, "*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
            if (candidate is not null) return candidate;
        }

        throw new FileNotFoundException($"Could not find a legacy First Edition save under '{legacyFolder}'.");
    }

    private static void Traverse(JsonElement element, string path, string pilotName, ICollection<LegacyPilotObjectMatch> matches)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (IsPilotObject(element, pilotName))
                {
                    matches.Add(new LegacyPilotObjectMatch
                    {
                        JsonPath = path,
                        Guid = ReadString(element, "GUID"),
                        Name = ReadString(element, "Name"),
                        Nickname = ReadString(element, "Nickname"),
                        Description = ReadString(element, "Description"),
                        ObjectType = DetermineObjectType(element),
                        ObjectJson = element.GetRawText()
                    });
                }

                foreach (var property in element.EnumerateObject())
                    Traverse(property.Value, path + "." + property.Name, pilotName, matches);
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Traverse(item, $"{path}[{index}]", pilotName, matches);
                    index++;
                }
                break;
        }
    }

    private static bool IsPilotObject(JsonElement element, string pilotName)
    {
        foreach (var propertyName in new[] { "Nickname", "Name" })
        {
            var value = ReadString(element, propertyName);
            if (value.Equals(pilotName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string DetermineObjectType(JsonElement element)
    {
        if (element.TryGetProperty("CustomDeck", out _)) return "CustomDeckObject";
        if (element.TryGetProperty("CustomMesh", out _)) return "CustomMeshObject";
        if (element.TryGetProperty("CustomImage", out _)) return "CustomImageObject";
        if (element.TryGetProperty("ContainedObjects", out _)) return "ContainerObject";
        return ReadString(element, "Name");
    }

    private static void CollectUrls(
        JsonElement element,
        string path,
        LegacyPilotObjectMatch match,
        ICollection<LegacyPilotUrlReference> urls)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectUrls(property.Value, path + "." + property.Name, match, urls);
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CollectUrls(item, $"{path}[{index}]", match, urls);
                    index++;
                }
                break;

            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                if (UrlRegex.IsMatch(value))
                {
                    urls.Add(new LegacyPilotUrlReference
                    {
                        ObjectJsonPath = match.JsonPath,
                        ObjectGuid = match.Guid,
                        ObjectName = match.Name,
                        ObjectNickname = match.Nickname,
                        PropertyJsonPath = path,
                        Url = value
                    });
                }
                break;
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return string.Empty;
        if (element.TryGetProperty(propertyName, out var exact) && exact.ValueKind == JsonValueKind.String)
            return exact.GetString() ?? string.Empty;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string Csv(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

    private static void WriteUrlCsv(string path, IEnumerable<LegacyPilotUrlReference> rows)
    {
        var lines = new List<string>
        {
            "ObjectJsonPath,ObjectGuid,ObjectName,ObjectNickname,PropertyJsonPath,Url"
        };
        lines.AddRange(rows.Select(row => string.Join(',',
            Csv(row.ObjectJsonPath), Csv(row.ObjectGuid), Csv(row.ObjectName), Csv(row.ObjectNickname),
            Csv(row.PropertyJsonPath), Csv(row.Url))));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private sealed class LegacyPilotObjectMatch
    {
        public string JsonPath { get; init; } = string.Empty;
        public string Guid { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Nickname { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string ObjectType { get; init; } = string.Empty;
        public string ObjectJson { get; init; } = string.Empty;
    }

    private sealed class LegacyPilotUrlReference
    {
        public string ObjectJsonPath { get; init; } = string.Empty;
        public string ObjectGuid { get; init; } = string.Empty;
        public string ObjectName { get; init; } = string.Empty;
        public string ObjectNickname { get; init; } = string.Empty;
        public string PropertyJsonPath { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
    }
}

public sealed class LegacyPilotSourceInspectionResult
{
    public string PilotName { get; init; } = string.Empty;
    public string LegacySavePath { get; init; } = string.Empty;
    public int MatchingObjects { get; init; }
    public int UrlReferences { get; init; }
    public int UniqueUrls { get; init; }
    public string OutputFolder { get; init; } = string.Empty;
    public string ObjectsJsonPath { get; init; } = string.Empty;
    public string UrlsCsvPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
}
