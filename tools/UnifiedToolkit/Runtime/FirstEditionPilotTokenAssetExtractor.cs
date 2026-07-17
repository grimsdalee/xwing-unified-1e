using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedToolkit.Runtime;

public sealed class FirstEditionPilotTokenCatalogue
{
    public string SourceSave { get; set; } = "";
    public string AssetsRoot { get; set; } = "";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public List<FirstEditionPilotTokenCandidate> Candidates { get; set; } = [];
    public List<FirstEditionPilotTokenSheet> Sheets { get; set; } = [];
    public FirstEditionPilotTokenSummary Summary { get; set; } = new();
}

public sealed class FirstEditionPilotTokenCandidate
{
    public int Index { get; set; }
    public string ObjectGuid { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Description { get; set; } = "";
    public string PropertyPath { get; set; } = "";
    public string Url { get; set; } = "";
    public string AssetKind { get; set; } = "";
    public bool IsLikelyPilotBaseToken { get; set; }
    public bool IsLikelySheet { get; set; }
    public int SheetColumns { get; set; } = 1;
    public int SheetRows { get; set; } = 1;
    public string PilotClue { get; set; } = "";
    public string ShipClue { get; set; } = "";
    public string FactionClue { get; set; } = "";
    public string BaseSizeClue { get; set; } = "";
    public string Confidence { get; set; } = "Low";
    public string SuggestedAssetPath { get; set; } = "";
    public string Evidence { get; set; } = "";
}

public sealed class FirstEditionPilotTokenSheet
{
    public string Url { get; set; } = "";
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int EstimatedCells => Columns * Rows;
    public List<string> ObjectGuids { get; set; } = [];
    public List<string> ContextNames { get; set; } = [];
}

public sealed class FirstEditionPilotTokenSummary
{
    public int ObjectsVisited { get; set; }
    public int ImageUrlsFound { get; set; }
    public int UniqueImageUrls { get; set; }
    public int LikelyTokenCandidates { get; set; }
    public int SheetCandidates { get; set; }
    public int T65Candidates { get; set; }
    public int IndividualCandidates { get; set; }
    public bool ReadyForReview { get; set; }
}

public static class FirstEditionPilotTokenAssetExtractor
{
    private static readonly Regex UrlRegex = new(@"^https?://", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] ImagePropertyHints = ["image", "diffuse", "faceurl", "backurl", "url"];
    private static readonly string[] TokenWords = ["pilot", "ship token", "base token", "base insert", "ship base", "token", "x-wing", "xwing"];
    private static readonly string[] T65Words = ["t-65", "t65", "x-wing", "xwing", "x wing"];

    public static FirstEditionPilotTokenCatalogue Extract(string savePath, string assetsRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(savePath));
        var result = new FirstEditionPilotTokenCatalogue
        {
            SourceSave = Path.GetFullPath(savePath),
            AssetsRoot = Path.GetFullPath(assetsRoot)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(document.RootElement, "$", new ObjectContext(), result, seen);

        result.Candidates = result.Candidates
            .OrderByDescending(x => x.IsLikelyPilotBaseToken)
            .ThenByDescending(x => x.Confidence == "High")
            .ThenBy(x => x.Nickname)
            .ThenBy(x => x.Url)
            .Select((x, i) => { x.Index = i + 1; return x; })
            .ToList();

        result.Sheets = result.Candidates
            .Where(x => x.IsLikelySheet)
            .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FirstEditionPilotTokenSheet
            {
                Url = g.Key,
                Columns = g.Max(x => x.SheetColumns),
                Rows = g.Max(x => x.SheetRows),
                ObjectGuids = g.Select(x => x.ObjectGuid).Where(x => x.Length > 0).Distinct().OrderBy(x => x).ToList(),
                ContextNames = g.Select(x => x.Nickname).Where(x => x.Length > 0).Distinct().OrderBy(x => x).ToList()
            })
            .OrderByDescending(x => x.EstimatedCells)
            .ToList();

        result.Summary.UniqueImageUrls = result.Candidates.Select(x => x.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        result.Summary.LikelyTokenCandidates = result.Candidates.Count(x => x.IsLikelyPilotBaseToken);
        result.Summary.SheetCandidates = result.Sheets.Count;
        result.Summary.T65Candidates = result.Candidates.Count(x => ContainsAny(Combined(x), T65Words));
        result.Summary.IndividualCandidates = result.Candidates.Count(x => x.IsLikelyPilotBaseToken && !x.IsLikelySheet);
        result.Summary.ReadyForReview = result.Summary.LikelyTokenCandidates > 0;
        return result;
    }

    private static void Visit(JsonElement element, string path, ObjectContext inherited, FirstEditionPilotTokenCatalogue result, HashSet<string> seen)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            result.Summary.ObjectsVisited++;
            var context = inherited.Clone();
            context.Guid = String(element, "GUID") ?? context.Guid;
            context.Name = String(element, "Nickname") ?? context.Name;
            context.Description = String(element, "Description") ?? context.Description;
            context.Type = String(element, "Name") ?? context.Type;
            context.Lua = String(element, "LuaScript") ?? context.Lua;

            var columns = Int(element, "NumWidth") ?? Int(element, "Number") ?? 1;
            var rows = Int(element, "NumHeight") ?? 1;

            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = path + "." + property.Name;
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString() ?? "";
                    if (IsImageUrl(property.Name, value))
                    {
                        result.Summary.ImageUrlsFound++;
                        var key = context.Guid + "|" + propertyPath + "|" + value;
                        if (seen.Add(key)) AddCandidate(result, context, propertyPath, property.Name, value, columns, rows);
                    }
                }
                else
                {
                    Visit(property.Value, propertyPath, context, result, seen);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray()) Visit(item, $"{path}[{index++}]", inherited, result, seen);
        }
    }

    private static void AddCandidate(FirstEditionPilotTokenCatalogue result, ObjectContext context, string path, string property, string url, int columns, int rows)
    {
        var evidenceText = $"{context.Name} {context.Description} {context.Type} {context.Lua} {path}";
        var likelyToken = ContainsAny(evidenceText, TokenWords);
        var sheet = columns > 1 || rows > 1 || path.Contains("CustomDeck", StringComparison.OrdinalIgnoreCase) || property.Equals("FaceURL", StringComparison.OrdinalIgnoreCase);
        var confidence = likelyToken && (ContainsAny(evidenceText, T65Words) || evidenceText.Contains("pilot", StringComparison.OrdinalIgnoreCase)) ? "High" : likelyToken ? "Medium" : "Low";
        var pilot = InferPilot(context.Name, context.Description);
        var ship = ContainsAny(evidenceText, T65Words) ? "T-65 X-Wing" : InferShip(evidenceText);
        var faction = InferFaction(evidenceText);
        var size = InferBaseSize(evidenceText);
        var extension = GuessExtension(url);
        var slug = Slug(string.IsNullOrWhiteSpace(pilot) ? (string.IsNullOrWhiteSpace(context.Name) ? $"candidate-{result.Candidates.Count + 1}" : context.Name) : pilot);

        result.Candidates.Add(new FirstEditionPilotTokenCandidate
        {
            ObjectGuid = context.Guid,
            ObjectType = context.Type,
            Nickname = context.Name,
            Description = context.Description,
            PropertyPath = path,
            Url = url,
            AssetKind = sheet ? "SheetOrDeck" : property.Contains("Back", StringComparison.OrdinalIgnoreCase) ? "BackImage" : "IndividualImage",
            IsLikelyPilotBaseToken = likelyToken,
            IsLikelySheet = sheet,
            SheetColumns = Math.Max(1, columns),
            SheetRows = Math.Max(1, rows),
            PilotClue = pilot,
            ShipClue = ship,
            FactionClue = faction,
            BaseSizeClue = size,
            Confidence = confidence,
            SuggestedAssetPath = Path.Combine("pilot-base-tokens", string.IsNullOrWhiteSpace(ship) ? "unmatched" : Slug(ship), slug + extension).Replace('\\', '/'),
            Evidence = BuildEvidence(context, property, columns, rows)
        });
    }

    private static bool IsImageUrl(string property, string value)
    {
        if (!UrlRegex.IsMatch(value)) return false;
        var p = property.ToLowerInvariant();
        if (ImagePropertyHints.Any(p.Contains)) return true;
        return Regex.IsMatch(value, @"\.(png|jpe?g|webp|gif)(\?|$)", RegexOptions.IgnoreCase) || value.Contains("steamusercontent", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildEvidence(ObjectContext c, string property, int columns, int rows)
        => $"Object={c.Guid}; Type={c.Type}; Property={property}; Grid={columns}x{rows}; Name={c.Name}";

    private static string Combined(FirstEditionPilotTokenCandidate x) => $"{x.Nickname} {x.Description} {x.PilotClue} {x.ShipClue} {x.PropertyPath}";
    private static bool ContainsAny(string text, IEnumerable<string> words) => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    private static string InferPilot(string name, string description) => name.Contains("token", StringComparison.OrdinalIgnoreCase) ? Regex.Replace(name, "(?i)pilot|ship|base|token|insert", "").Trim(' ', '-', '_') : "";
    private static string InferShip(string text)
    {
        var ships = new[] { "Y-Wing", "A-Wing", "B-Wing", "TIE Fighter", "TIE Interceptor", "Firespray", "Millennium Falcon", "Lambda Shuttle" };
        return ships.FirstOrDefault(x => text.Contains(x, StringComparison.OrdinalIgnoreCase)) ?? "";
    }
    private static string InferFaction(string text)
    {
        if (text.Contains("rebel", StringComparison.OrdinalIgnoreCase)) return "Rebel";
        if (text.Contains("imperial", StringComparison.OrdinalIgnoreCase) || text.Contains("empire", StringComparison.OrdinalIgnoreCase)) return "Imperial";
        if (text.Contains("scum", StringComparison.OrdinalIgnoreCase)) return "Scum";
        return "";
    }
    private static string InferBaseSize(string text)
    {
        if (text.Contains("epic", StringComparison.OrdinalIgnoreCase) || text.Contains("huge", StringComparison.OrdinalIgnoreCase)) return "Epic";
        if (text.Contains("large", StringComparison.OrdinalIgnoreCase)) return "Large";
        if (text.Contains("small", StringComparison.OrdinalIgnoreCase)) return "Small";
        return "";
    }
    private static string GuessExtension(string url)
    {
        var clean = url.Split('?', '#')[0];
        var ext = Path.GetExtension(clean);
        return Regex.IsMatch(ext, @"^\.(png|jpe?g|webp)$", RegexOptions.IgnoreCase) ? ext.ToLowerInvariant() : ".png";
    }
    private static string Slug(string value)
    {
        var s = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(s) ? "unmatched" : s;
    }
    private static string? String(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        return null;
    }

    private sealed class ObjectContext
    {
        public string Guid { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
        public string Lua { get; set; } = "";
        public ObjectContext Clone() => new() { Guid = Guid, Name = Name, Description = Description, Type = Type, Lua = Lua };
    }
}
