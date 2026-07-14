using System.Text.Json;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping.Upgrades;
using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Commands;

public static class PrepareFirstEditionUpgradesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { ShowUsage(); return 1; }
        var repoFolder = Path.GetFullPath(args[0]);
        var dataFolder = Path.GetFullPath(args[1]);
        try
        {
            var repository = RepositoryLoader.Load(repoFolder);
            var official = FirstEditionDataLoader.LoadUpgrades(dataFolder);
            var candidates = new List<UpgradeMappingCandidate>();
            var raw = new List<UpgradeMapping>();

            foreach (var source in repository.Upgrades.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
            {
                var name = Token(source.Name);
                var sourceSlot = NormaliseSlot(source.Slot);
                var matches = official.Where(x => Token(x.Name) == name).ToList();
                var slotMatches = matches.Where(x => SlotsCompatible(sourceSlot, x.Slot)).ToList();
                if (slotMatches.Count == 1) matches = slotMatches;

                if (matches.Count == 1)
                {
                    var target = matches[0];
                    candidates.Add(new UpgradeMappingCandidate
                    {
                        SourceId = source.Id, SourceName = source.Name, SourceSlot = sourceSlot,
                        TargetId = target.Id, TargetName = target.Name, TargetSlot = target.Slot,
                        TargetCost = target.SquadPointCost, TargetUnique = target.Unique,
                        Status = "ProposedDirect", MatchMethod = slotMatches.Count == 1 ? "ExactNameAndSlot" : "ExactName",
                        Confidence = slotMatches.Count == 1 ? 1.00m : 0.90m,
                        Notes = "Matched to an official First Edition upgrade identity. Restrictions require semantic review before live conversion."
                    });
                    raw.Add(new UpgradeMapping
                    {
                        SourceId = source.Id, TargetId = target.Id, Name = target.Name, Slot = target.Slot,
                        SquadPointCost = target.SquadPointCost, Unique = target.Unique,
                        Factions = target.Factions, ShipRestrictions = target.ShipRestrictions,
                        SizeRestrictions = target.SizeRestrictions, Text = target.Text
                    });
                }
                else if (matches.Count > 1)
                {
                    candidates.Add(Base(source, sourceSlot, "Ambiguous", $"{matches.Count} First Edition records share the same normalised name; slot or card identity requires review."));
                }
                else
                {
                    candidates.Add(Base(source, sourceSlot, "NotInOfficialDataset", "No official First Edition upgrade with the same normalised name was found."));
                }
            }

            var proposal = UpgradeProposalCanonicalizer.Canonicalize(raw);
            var reportFolder = Path.Combine(repoFolder, "_unifiedtoolkit_reports", "conversion");
            var matchesCsv = UpgradeMappingCandidatesReport.Write(reportFolder, candidates);
            var alternatesCsv = UpgradeSourceAlternatesReport.Write(reportFolder, proposal.Alternates);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var canonicalJson = Path.Combine(reportFolder, "upgrades.canonical.proposed.json");
            var alternatesJson = Path.Combine(reportFolder, "upgrade-source-alternates.proposed.json");
            File.WriteAllText(canonicalJson, JsonSerializer.Serialize(proposal.CanonicalMappings, options));
            File.WriteAllText(alternatesJson, JsonSerializer.Serialize(proposal.Alternates, options));

            Console.WriteLine("UnifiedToolkit First Edition Upgrade Preparation");
            Console.WriteLine("================================================");
            Console.WriteLine();
            Console.WriteLine($"Source upgrades:          {repository.Upgrades.Count}");
            Console.WriteLine($"Official upgrades loaded: {official.Count}");
            Console.WriteLine($"Matched source records:   {raw.Count}");
            Console.WriteLine($"Canonical upgrades:       {proposal.CanonicalMappings.Count}");
            Console.WriteLine($"Alternate printings:      {proposal.Alternates.Count}");
            Console.WriteLine($"Ambiguous:                {candidates.Count(x => x.Status == "Ambiguous")}");
            Console.WriteLine($"Not in official data:     {candidates.Count(x => x.Status == "NotInOfficialDataset")}");
            Console.WriteLine($"Candidates report:        {matchesCsv}");
            Console.WriteLine($"Alternates report:        {alternatesCsv}");
            Console.WriteLine($"Canonical proposals:      {canonicalJson}");
            Console.WriteLine($"Alternate proposals:      {alternatesJson}");
            Console.WriteLine();
            Console.WriteLine("Proposal files are review artifacts and do not replace live conversion data.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Upgrade preparation failed: {ex.Message}"); return 1; }
    }

    private static UpgradeMappingCandidate Base(UpgradeDefinition source, string slot, string status, string notes) => new()
    { SourceId = source.Id, SourceName = source.Name, SourceSlot = slot, Status = status, MatchMethod = "None", Notes = notes };

    private static bool SlotsCompatible(string source, string target) => source == NormaliseSlot(target) || SlotAliases.TryGetValue(source, out var aliases) && aliases.Contains(NormaliseSlot(target));
    private static string NormaliseSlot(string value) => Token(value);
    private static string Token(string value) => new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static readonly Dictionary<string, HashSet<string>> SlotAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["talent"] = new(StringComparer.OrdinalIgnoreCase) { "elite" },
        ["configuration"] = new(StringComparer.OrdinalIgnoreCase) { "title", "modification" },
        ["gunner"] = new(StringComparer.OrdinalIgnoreCase) { "crew" },
        ["sensor"] = new(StringComparer.OrdinalIgnoreCase) { "system" },
        ["device"] = new(StringComparer.OrdinalIgnoreCase) { "bomb" },
        ["forcepower"] = new(StringComparer.OrdinalIgnoreCase) { "elite" },
        ["astromech"] = new(StringComparer.OrdinalIgnoreCase) { "astromech", "salvagedastromech" }
    };
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit prepare-first-edition-upgrades <repo-folder> <xwing-data-folder>");
}
