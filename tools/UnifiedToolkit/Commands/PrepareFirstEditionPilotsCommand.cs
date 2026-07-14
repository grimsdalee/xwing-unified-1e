using System.Text.Json;
using UnifiedToolkit.Conversion.FirstEdition.DataImport;
using UnifiedToolkit.Conversion.Mapping;
using UnifiedToolkit.Conversion.Mapping.Pilots;
using UnifiedToolkit.Reports;
using UnifiedToolkit.Repository;
using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Commands;

public static class PrepareFirstEditionPilotsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { ShowUsage(); return 1; }
        var repoFolder = Path.GetFullPath(args[0]);
        var dataFolder = Path.GetFullPath(args[1]);
        var mappingFolder = args.Length >= 3 ? Path.GetFullPath(args[2]) : Path.Combine(AppContext.BaseDirectory, "ConversionData", "first-edition");
        try
        {
            var repository = RepositoryLoader.Load(repoFolder);
            var mappings = ConversionMappingLoader.Load(mappingFolder);
            var officialPilots = FirstEditionDataLoader.LoadPilots(dataFolder);
            var shipMap = mappings.Ships.ToDictionary(x => x.SourceId, x => x.TargetId, StringComparer.OrdinalIgnoreCase);
            var candidates = new List<PilotMappingCandidate>();
            var rawProposals = new List<PilotMapping>();

            foreach (var source in repository.Pilots.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
            {
                var sourceShipId = source.Ship?.Id ?? source.ShipType;
                if (!shipMap.TryGetValue(sourceShipId, out var targetShipId))
                {
                    candidates.Add(Base(source, sourceShipId, "SourceShipDeferred", "No confirmed First Edition ship mapping exists for this source pilot.", 0m));
                    continue;
                }

                var nameKey = Normalise(source.Name);
                var matches = officialPilots.Where(x => x.ShipId.Equals(targetShipId, StringComparison.OrdinalIgnoreCase) && Normalise(x.Name) == nameKey).ToList();
                var factionMatches = matches.Where(x => FactionCompatible(source.Faction, x.Faction)).ToList();
                if (factionMatches.Count == 1) matches = factionMatches;

                if (matches.Count == 1)
                {
                    var target = matches[0];
                    candidates.Add(From(source, sourceShipId, target, "ProposedDirect", factionMatches.Count == 1 ? "ExactNameShipFaction" : "ExactNameAndShip", factionMatches.Count == 1 ? 1.00m : 0.95m, "Matched to an official First Edition pilot identity."));
                    rawProposals.Add(new PilotMapping
                    {
                        MappingId = "pending-canonicalization", SourceId = source.Id, TargetId = target.Id,
                        Name = target.Name, ShipId = target.ShipId, Faction = target.Faction,
                        PilotSkill = target.PilotSkill, SquadPointCost = target.SquadPointCost,
                        Unique = target.Unique, UpgradeSlots = target.UpgradeSlots
                    });
                }
                else if (matches.Count > 1)
                {
                    candidates.Add(Base(source, sourceShipId, "Ambiguous", $"{matches.Count} official pilots match the same normalised name and ship.", 0.50m));
                }
                else
                {
                    candidates.Add(Base(source, sourceShipId, "NotInOfficialDataset", "No official First Edition pilot with the same normalised name was found on the confirmed target ship.", 0m));
                }
            }

            var proposalSet = PilotProposalCanonicalizer.Canonicalize(rawProposals);
            var validation = PilotMappingValidator.Validate(proposalSet.CanonicalMappings, proposalSet.Alternates);
            if (validation.Count > 0)
                throw new InvalidDataException("Canonical pilot proposal validation failed: " + string.Join(" | ", validation));

            var reports = Path.Combine(repoFolder, "_unifiedtoolkit_reports", "conversion");
            var csv = PilotMappingCandidatesReport.Write(reports, candidates);
            var alternatesCsv = PilotSourceAlternatesReport.Write(reports, proposalSet.Alternates);
            var canonicalJson = Path.Combine(reports, "pilots.canonical.proposed.json");
            var alternatesJson = Path.Combine(reports, "pilot-source-alternates.proposed.json");
            Directory.CreateDirectory(reports);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(canonicalJson, JsonSerializer.Serialize(proposalSet.CanonicalMappings, jsonOptions));
            File.WriteAllText(alternatesJson, JsonSerializer.Serialize(proposalSet.Alternates, jsonOptions));

            Console.WriteLine("UnifiedToolkit First Edition Pilot Preparation");
            Console.WriteLine("==============================================");
            Console.WriteLine();
            Console.WriteLine($"Mapping version:          {mappings.Version}");
            Console.WriteLine($"Source pilots:            {repository.Pilots.Count}");
            Console.WriteLine($"Official pilots loaded:   {officialPilots.Count}");
            Console.WriteLine($"Matched source records:   {rawProposals.Count}");
            Console.WriteLine($"Canonical pilots:         {proposalSet.CanonicalMappings.Count}");
            Console.WriteLine($"Alternate printings:      {proposalSet.Alternates.Count}");
            Console.WriteLine($"Ambiguous:                {candidates.Count(x => x.Status == "Ambiguous")}");
            Console.WriteLine($"Source ship deferred:     {candidates.Count(x => x.Status == "SourceShipDeferred")}");
            Console.WriteLine($"Not in official data:     {candidates.Count(x => x.Status == "NotInOfficialDataset")}");
            Console.WriteLine($"Candidates report:        {csv}");
            Console.WriteLine($"Alternates report:        {alternatesCsv}");
            Console.WriteLine($"Canonical proposals:      {canonicalJson}");
            Console.WriteLine($"Alternate proposals:      {alternatesJson}");
            Console.WriteLine();
            Console.WriteLine("Proposal files are review artifacts and do not replace live conversion data.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Pilot preparation failed: {ex.Message}"); return 1; }
    }

    private static PilotMappingCandidate Base(PilotDefinition p, string ship, string status, string notes, decimal confidence) => new()
    { SourceId = p.Id, SourceName = p.Name, SourceShipId = ship, SourceFaction = p.Faction, SourceInitiative = p.Initiative, Status = status, MatchMethod = "None", Confidence = confidence, Notes = notes };

    private static PilotMappingCandidate From(PilotDefinition p, string ship, FirstEditionDataPilot t, string status, string method, decimal confidence, string notes) => new()
    { SourceId = p.Id, SourceName = p.Name, SourceShipId = ship, SourceFaction = p.Faction, SourceInitiative = p.Initiative, TargetId = t.Id, TargetName = t.Name, TargetShipId = t.ShipId, TargetFaction = t.Faction, TargetPilotSkill = t.PilotSkill, TargetSquadPointCost = t.SquadPointCost, TargetUnique = t.Unique, TargetUpgradeSlots = string.Join(';', t.UpgradeSlots), Status = status, MatchMethod = method, Confidence = confidence, Notes = notes };

    private static bool FactionCompatible(string source, string target) => Normalise(source) == Normalise(target) || (Normalise(source) == "rebelalliance" && Normalise(target) == "rebel") || (Normalise(source) == "galacticempire" && Normalise(target) == "imperial") || (Normalise(source) == "scumandvillainy" && Normalise(target) == "scum");
    private static string Normalise(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit prepare-first-edition-pilots <repo-folder> <xwing-data-folder> [mapping-folder]");
}
