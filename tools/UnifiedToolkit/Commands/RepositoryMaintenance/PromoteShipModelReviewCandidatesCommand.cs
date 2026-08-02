using System.Text;
using System.Text.Json;
using UnifiedToolkit.RepositoryMaintenance;

namespace UnifiedToolkit.Commands.RepositoryMaintenance;

public static class PromoteShipModelReviewCandidatesCommand
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
            Console.Error.WriteLine(
                "Usage: promote-ship-model-review-candidates " +
                "<first-edition-repo-folder> [--inventory <file>] [--audit <file>]");
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var inventoryPath = ResolvePath(
                repositoryRoot,
                args,
                "--inventory",
                "_unifiedtoolkit_reports/model-inventory/ship-model-inventory.json");
            var auditPath = ResolvePath(
                repositoryRoot,
                args,
                "--audit",
                "_unifiedtoolkit_reports/model-selection/ship-model-selection-audit.json");

            ValidateFile(inventoryPath, "Ship-model inventory report");

            var inventory = JsonSerializer.Deserialize<ShipModelInventoryManifest>(
                    File.ReadAllText(inventoryPath),
                    JsonOptions)
                ?? throw new InvalidDataException(
                    "Could not parse the ship-model inventory report.");

            var existing = File.Exists(auditPath)
                ? JsonSerializer.Deserialize<List<ShipModelSelectionAuditEntry>>(
                      File.ReadAllText(auditPath),
                      JsonOptions)
                  ?? new List<ShipModelSelectionAuditEntry>()
                : new List<ShipModelSelectionAuditEntry>();

            var usedByFolder = inventory.Entries
                .Where(entry => !entry.UsageStatus.Equals(
                    "ReviewCandidate",
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(
                    entry => ParentPath(entry.RepositoryPath),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var promoted = 0;
            var updated = 0;
            var unresolved = new List<string>();

            foreach (var candidate in inventory.Entries.Where(entry =>
                         entry.UsageStatus.Equals(
                             "ReviewCandidate",
                             StringComparison.OrdinalIgnoreCase)))
            {
                var folder = ParentPath(candidate.RepositoryPath);
                if (!usedByFolder.TryGetValue(folder, out var activeEntries))
                {
                    unresolved.Add(
                        $"{candidate.RepositoryPath}: no active replacement in the same folder.");
                    continue;
                }

                var replacement = ResolveReplacement(candidate, activeEntries);
                if (replacement is null)
                {
                    unresolved.Add(
                        $"{candidate.RepositoryPath}: replacement could not be resolved unambiguously.");
                    continue;
                }

                var selectedFullPath = Path.Combine(
                    repositoryRoot,
                    replacement.RepositoryPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
                ValidateFile(
                    selectedFullPath,
                    $"Replacement model for {candidate.RepositoryPath}");

                var identity = ResolveShipIdentity(replacement, folder);
                var newEntry = new ShipModelSelectionAuditEntry
                {
                    Faction = identity.Faction,
                    ShipId = identity.ShipId,
                    ShipName = identity.ShipName,
                    RejectedModelPath = Normalise(candidate.RepositoryPath),
                    SelectedModelPath = Normalise(replacement.RepositoryPath),
                    Evidence =
                        "Full ships-v2 OBJ inventory plus a fresh Unified 2.5 spawned-ship save " +
                        "confirmed the selected production mesh. The rejected OBJ is not used by " +
                        "any current generated validation save or protected multipart configuration.",
                    CleanupStatus = "Obsolete",
                    LastConfirmedUtc = DateTimeOffset.UtcNow
                };

                var existingIndex = existing.FindIndex(entry =>
                    entry.RejectedModelPath.Equals(
                        newEntry.RejectedModelPath,
                        StringComparison.OrdinalIgnoreCase)
                    && entry.SelectedModelPath.Equals(
                        newEntry.SelectedModelPath,
                        StringComparison.OrdinalIgnoreCase));

                if (existingIndex >= 0)
                {
                    existing[existingIndex] = newEntry;
                    updated++;
                }
                else
                {
                    existing.Add(newEntry);
                    promoted++;
                }
            }

            existing = existing
                .OrderBy(entry => entry.Faction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.ShipName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.RejectedModelPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var auditFolder = Path.GetDirectoryName(auditPath)
                ?? throw new InvalidDataException(
                    "The model-selection audit path has no parent folder.");
            Directory.CreateDirectory(auditFolder);

            File.WriteAllText(
                auditPath,
                JsonSerializer.Serialize(existing, JsonOptions),
                new UTF8Encoding(false));

            WriteCsv(
                Path.Combine(auditFolder, "ship-model-selection-audit.csv"),
                existing);
            WriteMarkdown(
                Path.Combine(auditFolder, "SHIP-MODEL-SELECTION-AUDIT.md"),
                existing);

            var promotionReportPath = Path.Combine(
                auditFolder,
                "ship-model-review-candidate-promotion.json");
            File.WriteAllText(
                promotionReportPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = "1.0.0",
                        generatedUtc = DateTimeOffset.UtcNow,
                        repositoryRoot = Normalise(repositoryRoot),
                        inventoryPath = Normalise(inventoryPath),
                        auditPath = Normalise(auditPath),
                        reviewCandidates = inventory.Entries.Count(entry =>
                            entry.UsageStatus.Equals(
                                "ReviewCandidate",
                                StringComparison.OrdinalIgnoreCase)),
                        promoted,
                        updated,
                        unresolved
                    },
                    JsonOptions),
                new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit Ship Model Review Candidate Promotion");
            Console.WriteLine("====================================================");
            Console.WriteLine($"Repository:          {repositoryRoot}");
            Console.WriteLine($"Inventory:           {inventoryPath}");
            Console.WriteLine($"Audit:               {auditPath}");
            Console.WriteLine();
            Console.WriteLine($"Review candidates:   {inventory.Entries.Count(entry => entry.UsageStatus.Equals("ReviewCandidate", StringComparison.OrdinalIgnoreCase))}");
            Console.WriteLine($"Promoted:            {promoted}");
            Console.WriteLine($"Updated:             {updated}");
            Console.WriteLine($"Unresolved:          {unresolved.Count}");
            Console.WriteLine();
            Console.WriteLine("No OBJ files were moved or deleted.");

            if (unresolved.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Unresolved candidates:");
                foreach (var item in unresolved)
                    Console.WriteLine($"  {item}");
            }

            return unresolved.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Ship-model review candidate promotion failed: {ex.Message}");
            return 1;
        }
    }

    private static ShipModelInventoryEntry? ResolveReplacement(
        ShipModelInventoryEntry candidate,
        IReadOnlyList<ShipModelInventoryEntry> activeEntries)
    {
        var candidateName = candidate.FileName.ToLowerInvariant();
        var role = candidateName.Contains("open", StringComparison.Ordinal)
            ? "open"
            : candidateName.Contains("clos", StringComparison.Ordinal)
                ? "closed"
                : "primary";

        var roleMatches = activeEntries
            .Where(entry => MatchesRole(entry.FileName, role))
            .ToList();

        if (roleMatches.Count == 1)
            return roleMatches[0];

        if (roleMatches.Count > 1)
        {
            return roleMatches
                .OrderByDescending(entry => entry.UsageStatus.Equals(
                    "UsedPrimary",
                    StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(entry => entry.UsageTypes.Count)
                .ThenBy(entry => entry.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        if (activeEntries.Count == 1)
            return activeEntries[0];

        return null;
    }

    private static bool MatchesRole(string fileName, string role)
    {
        var lower = fileName.ToLowerInvariant();
        return role switch
        {
            "open" => lower.Contains("open", StringComparison.Ordinal),
            "closed" => lower.Contains("clos", StringComparison.Ordinal),
            _ => !lower.Contains("open", StringComparison.Ordinal)
                 && !lower.Contains("clos", StringComparison.Ordinal)
        };
    }

    private static (string Faction, string ShipId, string ShipName) ResolveShipIdentity(
        ShipModelInventoryEntry replacement,
        string folder)
    {
        var semanticGroup = replacement.ShipGroups
            .FirstOrDefault(group => group.Contains("__", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(semanticGroup))
        {
            var parts = semanticGroup.Split(
                new[] { "__" },
                2,
                StringSplitOptions.None);
            return (
                parts[0],
                parts[1],
                FriendlyShipName(parts[1], folder));
        }

        var folderName = folder[(folder.LastIndexOf('/') + 1)..];
        return ("multiple", folderName, FriendlyShipName(folderName, folder));
    }

    private static string FriendlyShipName(string shipId, string folder) =>
        shipId.ToLowerInvariant() switch
        {
            "lambdaclassshuttle" => "Lambda-class T-4a Shuttle",
            "firespray31" => "Firespray-31",
            "uwing" or "ut60duwing" => "UT-60D U-Wing",
            "xwing" or "t65xwing" => "T-65 X-Wing",
            "tiefofighter" => "TIE/fo Fighter",
            _ => folder[(folder.LastIndexOf('/') + 1)..]
        };

    private static string ResolvePath(
        string repositoryRoot,
        IReadOnlyList<string> args,
        string option,
        string defaultRelativePath)
    {
        for (var index = 1; index < args.Count; index++)
        {
            if (!args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= args.Count)
                throw new ArgumentException($"{option} requires a file path.");

            return Path.GetFullPath(args[index + 1]);
        }

        return Path.Combine(
            repositoryRoot,
            defaultRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void ValidateFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} was not found.", path);
    }

    private static string ParentPath(string path)
    {
        var normalised = Normalise(path);
        var separator = normalised.LastIndexOf('/');
        return separator < 0 ? string.Empty : normalised[..separator];
    }

    private static string Normalise(string value) =>
        value.Replace('\\', '/');

    private static string Csv(string value) =>
        '"' + (value ?? string.Empty).Replace("\"", "\"\"") + '"';

    private static void WriteCsv(
        string path,
        IReadOnlyList<ShipModelSelectionAuditEntry> entries)
    {
        using var writer = new StreamWriter(
            path,
            append: false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "Faction,ShipId,ShipName,RejectedModelPath,SelectedModelPath," +
            "Evidence,CleanupStatus,LastConfirmedUtc");

        foreach (var entry in entries)
        {
            writer.WriteLine(string.Join(
                ",",
                Csv(entry.Faction),
                Csv(entry.ShipId),
                Csv(entry.ShipName),
                Csv(entry.RejectedModelPath),
                Csv(entry.SelectedModelPath),
                Csv(entry.Evidence),
                Csv(entry.CleanupStatus),
                Csv(entry.LastConfirmedUtc.ToString("O"))));
        }
    }

    private static void WriteMarkdown(
        string path,
        IReadOnlyList<ShipModelSelectionAuditEntry> entries)
    {
        using var writer = new StreamWriter(
            path,
            append: false,
            new UTF8Encoding(false));

        writer.WriteLine("# Ship Model Selection Audit");
        writer.WriteLine();
        writer.WriteLine(
            "This report records rejected ship model links and their confirmed replacements.");
        writer.WriteLine();
        writer.WriteLine(
            "> Files are never deleted automatically. Verify and quarantine them through the repository-maintenance commands.");
        writer.WriteLine();
        writer.WriteLine("| Faction | Ship | Rejected OBJ | Confirmed OBJ | Status |");
        writer.WriteLine("|---|---|---|---|---|");

        foreach (var entry in entries)
        {
            writer.WriteLine(
                $"| {EscapeMarkdown(entry.Faction)} " +
                $"| {EscapeMarkdown(entry.ShipName)} " +
                $"| `{entry.RejectedModelPath}` " +
                $"| `{entry.SelectedModelPath}` " +
                $"| {EscapeMarkdown(entry.CleanupStatus)} |");
        }
    }

    private static string EscapeMarkdown(string value) =>
        (value ?? string.Empty).Replace("|", "\\|");
}
