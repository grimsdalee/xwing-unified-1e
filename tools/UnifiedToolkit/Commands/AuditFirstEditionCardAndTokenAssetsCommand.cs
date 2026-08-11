using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.Commands;

/// <summary>Phase 16 read-only card, damage-deck and legacy accessory audit.</summary>
public static class AuditFirstEditionCardAndTokenAssetsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var dataRoot = Path.GetFullPath(Option(args, "--xwing-data")
                ?? Path.Combine(repository, "source", "xwing-data"));
            var artworkRoot = Path.GetFullPath(Option(args, "--artwork")
                ?? Path.Combine(repository, "assets", "source", "xwing-data", "images"));
            var contextsPath = Path.GetFullPath(Option(args, "--legacy-contexts")
                ?? Path.Combine(repository, "ukb", "reports", "legacy-asset-contexts.csv"));
            var importPath = Path.GetFullPath(Option(args, "--legacy-import")
                ?? Path.Combine(repository, "assets", "manifests", "legacy1e-import.json"));
            var output = Path.GetFullPath(Option(args, "--output")
                ?? Path.Combine(repository, "_unifiedtoolkit_reports", "phase16", "card-token-audit"));

            RequireDirectory(repository, "Repository");
            RequireDirectory(dataRoot, "xwing-data");
            RequireDirectory(artworkRoot, "xwing-data artwork");
            RequireFile(contextsPath, "Legacy context catalogue");
            RequireFile(importPath, "Legacy import manifest");

            var conditions = LoadCards(Path.Combine(dataRoot, "data", "conditions.js"), artworkRoot, repository);
            var upgrades = LoadCards(Path.Combine(dataRoot, "data", "upgrades.js"), artworkRoot, repository);
            var upgradeCardBacks = AuditUpgradeCardBacks(repository, upgrades);
            var candidates = LoadLegacyCandidates(repository, contextsPath, importPath);

            foreach (var condition in conditions)
            {
                condition.LegacyTokenCandidates = candidates
                    .Where(candidate => candidate.Category == "condition-token"
                        && Normalise(candidate.ObjectName).Contains(Normalise(condition.Name), StringComparison.Ordinal)
                        && candidate.PropertyName.Equals("DiffuseURL", StringComparison.OrdinalIgnoreCase))
                    .Select(candidate => candidate.RepositoryPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var decks = new[]
            {
                Deck(dataRoot, artworkRoot, "core", "Core Set", "damage-deck-core.js"),
                Deck(dataRoot, artworkRoot, "core-tfa", "The Force Awakens Core Set", "damage-deck-core-tfa.js"),
                Deck(dataRoot, artworkRoot, "rebel-transport", "GR-75 Rebel Transport", "damage-deck-rebel-transport.js")
            };

            var epicDecks = ExpectedEpicDamageDecks(repository);

            var manifest = new CardTokenAssetAudit
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                Conditions = conditions,
                Upgrades = upgrades,
                UpgradeCardBacks = upgradeCardBacks,
                DamageDecks = decks.ToList(),
                EpicDamageDecks = epicDecks,
                MissingEpicDamageDeckArtwork = epicDecks
                    .Where(deck => !deck.ArtworkComplete)
                    .Select(deck => deck.ShipName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                LegacyCandidates = candidates
            };

            Directory.CreateDirectory(output);
            var jsonPath = Path.Combine(output, "first-edition-card-token-assets.json");
            var csvPath = Path.Combine(output, "legacy-accessory-asset-candidates.csv");
            var scanCsvPath = Path.Combine(output, "epic-damage-deck-scan-checklist.csv");
            var upgradeBackCsvPath = Path.Combine(output, "upgrade-card-backs.csv");
            var reportPath = Path.Combine(output, "FIRST-EDITION-CARD-TOKEN-ASSET-AUDIT.md");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            WriteCsv(csvPath, candidates);
            WriteEpicDamageDeckScanChecklist(scanCsvPath, epicDecks);
            WriteUpgradeCardBacks(upgradeBackCsvPath, upgradeCardBacks);
            WriteReport(reportPath, manifest);

            Console.WriteLine("UnifiedToolkit Phase 16 First Edition Card and Token Asset Audit");
            Console.WriteLine("================================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:                       {repository}");
            Console.WriteLine($"Condition cards:                  {conditions.Count}");
            Console.WriteLine($"Condition artwork available:      {conditions.Count(row => row.ArtworkAvailable)}");
            Console.WriteLine($"Conditions with token candidates: {conditions.Count(row => row.LegacyTokenCandidates.Count > 0)}");
            Console.WriteLine($"Upgrade cards:                    {upgrades.Count}");
            Console.WriteLine($"Upgrade artwork available:        {upgrades.Count(row => row.ArtworkAvailable)}");
            Console.WriteLine($"Upgrade-card back types expected: {upgradeCardBacks.Count}");
            Console.WriteLine($"Upgrade-card backs available:     {upgradeCardBacks.Count(row => row.ArtworkAvailable)}");
            Console.WriteLine($"Damage-deck sets present:          {decks.Length}");
            Console.WriteLine($"Epic ship damage sets expected:   {epicDecks.Select(deck => deck.ShipId).Distinct().Count()}");
            Console.WriteLine($"Epic section decks expected:      {epicDecks.Count}");
            Console.WriteLine($"Epic physical cards expected:     {epicDecks.Sum(deck => deck.PhysicalCardCount)}");
            Console.WriteLine($"Epic ship damage sets incomplete: {manifest.MissingEpicDamageDeckArtwork.Count}");
            Console.WriteLine($"Legacy candidate files:           {candidates.Select(row => row.RepositoryPath).Distinct().Count()}");
            Console.WriteLine();
            Console.WriteLine($"Manifest:                         {jsonPath}");
            Console.WriteLine($"Candidates:                       {csvPath}");
            Console.WriteLine($"Epic scan checklist:              {scanCsvPath}");
            Console.WriteLine($"Upgrade-card backs:               {upgradeBackCsvPath}");
            Console.WriteLine($"Report:                           {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Audit completed. No source assets or mappings were modified.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Card and token asset audit failed: {ex.Message}");
            return 1;
        }
    }

    private static List<CardAssetAuditRow> LoadCards(string path, string artworkRoot, string repository)
    {
        RequireFile(path, "xwing-data card data");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray().Select(item =>
        {
            var image = Text(item, "image");
            var fullPath = Path.Combine(artworkRoot, image.Replace('/', Path.DirectorySeparatorChar));
            return new CardAssetAuditRow
            {
                Name = Text(item, "name"),
                Xws = Text(item, "xws"),
                Slot = Text(item, "slot"),
                Image = image,
                RepositoryPath = Relative(repository, fullPath),
                ArtworkAvailable = File.Exists(fullPath)
            };
        }).OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static DamageDeckAuditRow Deck(
        string dataRoot, string artworkRoot, string id, string name, string dataFile)
    {
        var cards = LoadCards(Path.Combine(dataRoot, "data", dataFile), artworkRoot, dataRoot);
        var folder = Path.Combine(artworkRoot, "damage-decks", id);
        return new DamageDeckAuditRow
        {
            Id = id,
            Name = name,
            DataRecords = cards.Count,
            ArtworkFiles = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Count() : 0
        };
    }

    private static List<UpgradeCardBackAuditRow> AuditUpgradeCardBacks(
        string repository, IEnumerable<CardAssetAuditRow> upgrades)
    {
        var folder = Path.Combine(repository, "assets", "source", "unified1e", "upgrade-card-backs");
        return upgrades.Select(upgrade => upgrade.Slot)
            .Where(slot => !string.IsNullOrWhiteSpace(slot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(slot => slot, StringComparer.OrdinalIgnoreCase)
            .Select(slot =>
            {
                var fileName = $"{slot.ToLowerInvariant().Replace(' ', '_')}.png";
                var path = Path.Combine(folder, fileName);
                return new UpgradeCardBackAuditRow
                {
                    UpgradeType = slot,
                    ExpectedFileName = fileName,
                    RepositoryPath = Relative(repository, path),
                    ArtworkAvailable = File.Exists(path)
                };
            }).ToList();
    }

    private static List<EpicDamageDeckSectionAuditRow> ExpectedEpicDamageDecks(string repository)
    {
        var sections = new[]
        {
            EpicSection("cr90corvette", "CR90 Corvette", "fore",
                Card("Comms Failure"), Card("Deck Breach"), Card("Direct Hit", 2),
                Card("Life Support Failure"), Card("Scrambled Scopes"),
                Card("Secondary Drive Failure"), Card("Tracking Misalignment"), Card("Weapon Damaged", 2)),
            EpicSection("cr90corvette", "CR90 Corvette", "aft",
                Card("Deck Breach"), Card("Grid Overload", 2), Card("Life Support Failure"),
                Card("Power Plant Failure"), Card("Projector Power Failure"),
                Card("Reactor Leak", 2), Card("Structural Collapse", 2)),

            EpicSection("raiderclasscorvette", "Raider-class Corvette", "fore",
                Card("Deck Breach"), Card("Direct Hit", 2), Card("Power Plant Failure"),
                Card("Projector Power Failure"), Card("Reactor Leak", 2),
                Card("Secondary Drive Failure"), Card("Tracking Misalignment"), Card("Weapon Damaged")),
            EpicSection("raiderclasscorvette", "Raider-class Corvette", "aft",
                Card("Command Deck Breach"), Card("Comms Failure"), Card("Deck Breach"),
                Card("Life Support Failure"), Card("Misfiring Thrusters", 2), Card("Scrambled Scopes"),
                Card("Structural Collapse", 2), Card("Weapon Damaged")),

            EpicSection("gozanticlasscruiser", "Gozanti-class Cruiser", "fore",
                Card("Damaged Docking Clamp", 3), Card("Hull Breach", 2), Card("Comms Failure"),
                Card("Secondary Drive Failure"), Card("Scrambled Scopes", 2), Card("Viewport Rupture")),
            EpicSection("gozanticlasscruiser", "Gozanti-class Cruiser", "aft",
                Card("Damaged Stabilizers", 2), Card("Weapons Offline"), Card("Reactor Leak", 2),
                Card("Reactor Core Rupture"), Card("Projector Power Failure"), Card("Damaged Docking Clamp", 3)),

            EpicSection("croccruiser", "C-ROC Cruiser", "fore",
                Card("Hull Breach", 2), Card("Secondary Drive Failure", 2),
                Card("Scrambled Scopes", 2), Card("Viewport Rupture", 2), Card("Spilled Cargo", 2)),
            EpicSection("croccruiser", "C-ROC Cruiser", "aft",
                Card("Damaged Stabilizers", 2), Card("Weapons Offline"), Card("Reactor Leak", 2),
                Card("Projector Power Failure"), Card("Reactor Cowl Rupture"), Card("Damaged Dish"),
                Card("Spilled Cargo", 2))
        };

        foreach (var section in sections)
        {
            var folder = Path.Combine(repository, "assets", "source", "unified1e", "damage-decks",
                section.ShipId, section.Section);
            section.ArtworkFolder = Relative(repository, folder);
            var files = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.png").ToList()
                : new List<string>();
            var available = files.Select(path => Normalise(Path.GetFileNameWithoutExtension(path)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            section.BackArtworkAvailable = available.Contains("back");
            foreach (var card in section.Cards)
            {
                card.ExpectedFileName = $"{Slug(card.Name)}.png";
                card.ArtworkAvailable = available.Contains(Normalise(Path.GetFileNameWithoutExtension(card.ExpectedFileName)));
            }
        }

        return sections.ToList();
    }

    private static EpicDamageDeckSectionAuditRow EpicSection(
        string shipId, string shipName, string section, params EpicDamageCardAuditRow[] cards) => new()
        {
            ShipId = shipId,
            ShipName = shipName,
            Section = section,
            Cards = cards.ToList()
        };

    private static EpicDamageCardAuditRow Card(string name, int quantity = 1) => new()
    {
        Name = name,
        Quantity = quantity
    };

    private static List<LegacyAccessoryCandidate> LoadLegacyCandidates(
        string repository, string contextsPath, string importPath)
    {
        using var importDocument = JsonDocument.Parse(File.ReadAllText(importPath));
        var imports = importDocument.RootElement.GetProperty("entries").EnumerateArray()
            .Select(item => new
            {
                Url = Url(Text(item, "sourceUrl")),
                Destination = Text(item, "destinationRepositoryPath"),
                Status = Text(item, "status")
            })
            .Where(item => item.Status is "downloaded" or "unchanged")
            .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(Path.Combine(repository, "assets", "source"), "*", SearchOption.AllDirectories)
            .Where(path => path.Contains("legacy1e", StringComparison.OrdinalIgnoreCase))
            .GroupBy(path => Path.GetFileName(path)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var results = new List<LegacyAccessoryCandidate>();
        foreach (var row in Csv.Read(contextsPath))
        {
            var sourceUrl = Url(row.GetValueOrDefault("SourceUrl") ?? string.Empty);
            if (!imports.TryGetValue(sourceUrl, out var import) || string.IsNullOrWhiteSpace(import.Destination)) continue;
            var objectName = row.GetValueOrDefault("ObjectNickname") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(objectName)) objectName = row.GetValueOrDefault("ObjectName") ?? string.Empty;
            var container = row.GetValueOrDefault("ContainerText") ?? string.Empty;
            var category = Category($"{objectName} {container}");
            if (category is null || !files.TryGetValue(Path.GetFileName(import.Destination), out var matches)) continue;
            foreach (var match in matches)
            {
                results.Add(new LegacyAccessoryCandidate
                {
                    Category = category,
                    PropertyName = row.GetValueOrDefault("PropertyName") ?? string.Empty,
                    ObjectName = objectName,
                    Container = container,
                    RepositoryPath = Relative(repository, match),
                    SourceUrl = sourceUrl
                });
            }
        }
        return results.GroupBy(row => $"{row.Category}|{row.PropertyName}|{row.ObjectName}|{row.RepositoryPath}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).OrderBy(row => row.Category).ThenBy(row => row.ObjectName).ToList();
    }

    private static string? Category(string text)
    {
        var value = text.ToLowerInvariant();
        if (value.Contains("condition token")) return "condition-token";
        if (value.Contains("epic playmat")) return "epic-playmat";
        if (value.Contains("critical hit")) return "critical-hit";
        if (value.Contains("damage deck")) return "damage-deck";
        if (value.Contains("range") && value.Contains("ruler")) return "range-ruler";
        if (value.Contains("asteroid")) return "asteroid";
        if (value.Contains("debris")) return "debris";
        if (value.Contains("mine") || value.Contains("conner net") || value.Contains("connor net")) return "mine";
        if (value.Contains("bomb") || value.Contains("seismic charge") || value.Contains("thermal detonator")) return "bomb";
        return null;
    }

    private static void WriteCsv(string path, IEnumerable<LegacyAccessoryCandidate> rows)
    {
        var lines = new List<string> { "Category,PropertyName,ObjectName,Container,RepositoryPath,SourceUrl" };
        lines.AddRange(rows.Select(row => string.Join(',', new[]
        {
            Quote(row.Category), Quote(row.PropertyName), Quote(row.ObjectName),
            Quote(row.Container), Quote(row.RepositoryPath), Quote(row.SourceUrl)
        })));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteEpicDamageDeckScanChecklist(
        string path, IEnumerable<EpicDamageDeckSectionAuditRow> sections)
    {
        var lines = new List<string>
        {
            "ShipId,ShipName,Section,CardName,Quantity,ExpectedFileName,ArtworkAvailable,ArtworkFolder"
        };
        foreach (var section in sections)
        {
            lines.AddRange(section.Cards.Select(card => string.Join(',', new[]
            {
                Quote(section.ShipId), Quote(section.ShipName), Quote(section.Section),
                Quote(card.Name), card.Quantity.ToString(), Quote(card.ExpectedFileName),
                card.ArtworkAvailable.ToString(), Quote(section.ArtworkFolder)
            })));
            lines.Add(string.Join(',', new[]
            {
                Quote(section.ShipId), Quote(section.ShipName), Quote(section.Section),
                Quote("Card back"), "1", Quote("back.png"),
                section.BackArtworkAvailable.ToString(), Quote(section.ArtworkFolder)
            }));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteUpgradeCardBacks(string path, IEnumerable<UpgradeCardBackAuditRow> rows)
    {
        var lines = new List<string>
        {
            "UpgradeType,ExpectedFileName,ArtworkAvailable,RepositoryPath"
        };
        lines.AddRange(rows.Select(row => string.Join(',', new[]
        {
            Quote(row.UpgradeType), Quote(row.ExpectedFileName),
            row.ArtworkAvailable.ToString(), Quote(row.RepositoryPath)
        })));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteReport(string path, CardTokenAssetAudit audit)
    {
        var lines = new List<string>
        {
            "# Phase 16 First Edition Card and Token Asset Audit", "",
            $"- Condition cards: **{audit.Conditions.Count}**",
            $"- Upgrade cards: **{audit.Upgrades.Count}**",
            $"- Upgrade-card backs: **{audit.UpgradeCardBacks.Count(row => row.ArtworkAvailable)}/{audit.UpgradeCardBacks.Count} available**",
            $"- Damage-deck sets present: **{audit.DamageDecks.Count}**",
            $"- Legacy accessory candidates: **{audit.LegacyCandidates.Count}**", "",
            "## Damage decks present", ""
        };
        lines.AddRange(audit.DamageDecks.Select(deck => $"- {deck.Name}: {deck.DataRecords} records, {deck.ArtworkFiles} artwork files"));
        lines.AddRange(new[] { "", "## Upgrade-card backs", "" });
        lines.AddRange(audit.UpgradeCardBacks.Select(back =>
            $"- {back.UpgradeType}: {(back.ArtworkAvailable ? $"available (`{back.RepositoryPath}`)" : $"missing (`{back.ExpectedFileName}`)")}"));
        lines.AddRange(new[] { "", "## Epic damage-deck scan checklist", "" });
        foreach (var section in audit.EpicDamageDecks)
        {
            lines.Add($"### {section.ShipName} — {section.Section}");
            lines.Add("");
            lines.Add($"- Physical cards: **{section.PhysicalCardCount}**");
            lines.Add($"- Unique card faces: **{section.Cards.Count}**");
            lines.Add($"- Artwork complete: **{section.ArtworkComplete}**");
            lines.Add($"- Card back: {(section.BackArtworkAvailable ? "available" : "missing (`back.png`)")}");
            lines.Add("");
            lines.AddRange(section.Cards.Select(card =>
                $"- {card.Name}{(card.Quantity > 1 ? $" ×{card.Quantity}" : "")}: " +
                (card.ArtworkAvailable ? "available" : $"missing (`{card.ExpectedFileName}`)")));
            lines.Add("");
        }
        lines.AddRange(new[] { "", "## Condition token evidence", "" });
        lines.AddRange(audit.Conditions.Select(row => $"- {row.Name}: {(row.LegacyTokenCandidates.Count == 0 ? "none identified" : string.Join(", ", row.LegacyTokenCandidates))}"));
        lines.AddRange(new[] { "", "Candidates require visual and runtime validation before canonical import." });
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string? Option(string[] args, string name) =>
        Enumerable.Range(0, args.Length - 1).Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]).FirstOrDefault();
    private static void RequireDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} not found: {path}"); }
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found.", path); }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Url(string value) => value.Trim().Replace("http://", "https://", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
    private static string Normalise(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant()
        .Split(new[] { ' ', '-', '_', '!', '\'', '.', ',', ':', '/', '\\', '(', ')' }, StringSplitOptions.RemoveEmptyEntries));
    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit audit-first-edition-card-token-assets <repository> [--output <folder>]");
}

internal static class Csv
{
    public static List<Dictionary<string, string>> Read(string path)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        using var reader = new StreamReader(path, Encoding.UTF8, true);
        while (reader.Read() is var code && code >= 0)
        {
            var ch = (char)code;
            if (quoted)
            {
                if (ch == '"' && reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { record.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && reader.Peek() == '\n') reader.Read();
                record.Add(field.ToString()); field.Clear();
                if (record.Any(value => value.Length > 0)) records.Add(record);
                record = new();
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); records.Add(record); }
        if (records.Count == 0) return new();
        var headers = records[0];
        return records.Skip(1).Select(values => headers.Select((header, index) => new
        {
            header,
            value = index < values.Count ? values[index] : string.Empty
        }).ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase)).ToList();
    }
}

public sealed class CardTokenAssetAudit
{
    public DateTimeOffset GeneratedUtc { get; init; }
    public List<CardAssetAuditRow> Conditions { get; init; } = new();
    public List<CardAssetAuditRow> Upgrades { get; init; } = new();
    public List<UpgradeCardBackAuditRow> UpgradeCardBacks { get; init; } = new();
    public List<DamageDeckAuditRow> DamageDecks { get; init; } = new();
    public List<EpicDamageDeckSectionAuditRow> EpicDamageDecks { get; init; } = new();
    public List<string> MissingEpicDamageDeckArtwork { get; init; } = new();
    public List<LegacyAccessoryCandidate> LegacyCandidates { get; init; } = new();
}

public sealed class UpgradeCardBackAuditRow
{
    public string UpgradeType { get; init; } = "";
    public string ExpectedFileName { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public bool ArtworkAvailable { get; init; }
}

public sealed class EpicDamageDeckSectionAuditRow
{
    public string ShipId { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string Section { get; init; } = "";
    public string ArtworkFolder { get; set; } = "";
    public bool BackArtworkAvailable { get; set; }
    public List<EpicDamageCardAuditRow> Cards { get; init; } = new();
    public int PhysicalCardCount => Cards.Sum(card => card.Quantity);
    public bool ArtworkComplete => BackArtworkAvailable && Cards.All(card => card.ArtworkAvailable);
}

public sealed class EpicDamageCardAuditRow
{
    public string Name { get; init; } = "";
    public int Quantity { get; init; }
    public string ExpectedFileName { get; set; } = "";
    public bool ArtworkAvailable { get; set; }
}

public sealed class CardAssetAuditRow
{
    public string Name { get; init; } = "";
    public string Xws { get; init; } = "";
    public string Slot { get; init; } = "";
    public string Image { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public bool ArtworkAvailable { get; init; }
    public List<string> LegacyTokenCandidates { get; set; } = new();
}

public sealed class DamageDeckAuditRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int DataRecords { get; init; }
    public int ArtworkFiles { get; init; }
}

public sealed class LegacyAccessoryCandidate
{
    public string Category { get; init; } = "";
    public string PropertyName { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public string Container { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public string SourceUrl { get; init; } = "";
}
