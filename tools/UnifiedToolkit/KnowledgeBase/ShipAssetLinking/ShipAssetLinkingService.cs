using System.Security.Cryptography;
using System.Text;
using UnifiedToolkit.KnowledgeBase.ShipAssetLinking.Reports;

namespace UnifiedToolkit.KnowledgeBase.ShipAssetLinking;

public sealed class ShipAssetLinkingService
{
    private readonly ShipAssetRoleCatalogue roleCatalogue;
    private readonly ShipAliasBuilder aliasBuilder;
    private readonly ShipAssetEligibilityFilter eligibilityFilter;
    private readonly ShipAssetCandidateScorer candidateScorer;
    private readonly ShipAssetCandidateClassifier candidateClassifier;
    private readonly ShipAssetCandidateEvidencePolicy evidencePolicy;
    private readonly ShipAssetReferenceUpdater referenceUpdater;
    private readonly ShipAssetLinkOutputWriter outputWriter;
    private readonly LegacyAssetContextReportWriter contextReportWriter;

    public ShipAssetLinkingService(
        ShipAssetRoleCatalogue roleCatalogue,
        ShipAliasBuilder aliasBuilder,
        ShipAssetEligibilityFilter eligibilityFilter,
        ShipAssetCandidateScorer candidateScorer,
        ShipAssetCandidateClassifier candidateClassifier,
        ShipAssetCandidateEvidencePolicy evidencePolicy,
        ShipAssetReferenceUpdater referenceUpdater,
        ShipAssetLinkOutputWriter outputWriter,
        LegacyAssetContextReportWriter contextReportWriter)
    {
        this.roleCatalogue = roleCatalogue;
        this.aliasBuilder = aliasBuilder;
        this.eligibilityFilter = eligibilityFilter;
        this.candidateScorer = candidateScorer;
        this.candidateClassifier = candidateClassifier;
        this.evidencePolicy = evidencePolicy;
        this.referenceUpdater = referenceUpdater;
        this.outputWriter = outputWriter;
        this.contextReportWriter = contextReportWriter;
    }

    public static ShipAssetLinkingService CreateDefault()
    {
        var contextMatcher = new LegacyAssetContextMatcher();
        return new ShipAssetLinkingService(
            new ShipAssetRoleCatalogue(),
            new ShipAliasBuilder(),
            new ShipAssetEligibilityFilter(contextMatcher),
            new ShipAssetCandidateScorer(contextMatcher),
            new ShipAssetCandidateClassifier(),
            new ShipAssetCandidateEvidencePolicy(),
            new ShipAssetReferenceUpdater(),
            ShipAssetLinkOutputWriter.CreateDefault(),
            new LegacyAssetContextReportWriter());
    }

    public ShipAssetLinkResult Link(ShipAssetLinkingOptions options)
    {
        options.Validate();

        var knowledgeBase = ShipAssetJson.Read<UnifiedKnowledgeBase>(options.KnowledgeBasePath);
        var ships = ShipAssetJson.Read<List<FirstEditionShipRecord>>(options.ShipsFile);
        var legacyContexts = LegacyAssetContextIndex.Load(options.LegacySavePath);
        var linkedShips = BuildLinks(
            knowledgeBase,
            ships,
            legacyContexts,
            options.CandidatesPerRole);

        referenceUpdater.ReplaceShipReferences(knowledgeBase, linkedShips);
        outputWriter.Write(options.OutputRoot, knowledgeBase, linkedShips);
        contextReportWriter.Write(options.OutputRoot, legacyContexts);

        return CreateResult(options.OutputRoot, linkedShips);
    }

    private List<KnowledgeBaseShip> BuildLinks(
        UnifiedKnowledgeBase knowledgeBase,
        IEnumerable<FirstEditionShipRecord> ships,
        LegacyAssetContextIndex legacyContexts,
        int candidatesPerRole)
    {
        var linkedShips = new List<KnowledgeBaseShip>();

        foreach (var ship in ships.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var identity = aliasBuilder.Build(ship);
            var baseSize = NormalizeBaseSize(ship.Size);
            var roleLinks = roleCatalogue.GetAll()
                .Select(role => BuildRoleLink(
                    knowledgeBase.Domains.Assets,
                    role,
                    identity,
                    legacyContexts,
                    candidatesPerRole,
                    baseSize))
                .ToList();

            linkedShips.Add(new KnowledgeBaseShip
            {
                ShipId = $"SHIP-{StableId(ship.TargetId)}",
                SourceId = ship.SourceId,
                TargetId = ship.TargetId,
                Name = ship.Name,
                BaseSize = baseSize,
                Factions = ship.Factions,
                AssetRoles = roleLinks
            });
        }

        return linkedShips;
    }

    private KnowledgeBaseShipAssetRole BuildRoleLink(
        IEnumerable<KnowledgeBaseAsset> assets,
        ShipAssetRoleDefinition role,
        ShipIdentityProfile identity,
        LegacyAssetContextIndex legacyContexts,
        int candidatesPerRole,
        string baseSize)
    {
        var candidates = assets
            .Select(asset => new
            {
                Asset = asset,
                Contexts = legacyContexts.Find(asset)
            })
            .Where(item => eligibilityFilter.IsEligible(item.Asset, role.Name, item.Contexts))
            .Select(item => candidateScorer.Score(
                item.Asset,
                role.Name,
                identity,
                item.Contexts,
                baseSize))
            .Where(candidate => candidate.Score >= 35)
            .Where(candidate => evidencePolicy.HasRequiredEvidence(role.Name, candidate))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .Take(candidatesPerRole)
            .ToList();

        return new KnowledgeBaseShipAssetRole
        {
            Role = role.Name,
            Required = role.Required,
            Status = candidateClassifier.Classify(candidates),
            Candidates = candidates
        };
    }

    private static ShipAssetLinkResult CreateResult(
        string outputRoot,
        IReadOnlyCollection<KnowledgeBaseShip> linkedShips) => new()
    {
        Ships = linkedShips.Count,
        CandidateLinks = linkedShips.Sum(ship => ship.AssetRoles.Sum(role => role.Candidates.Count)),
        ClearRoles = linkedShips.Sum(ship => ship.AssetRoles.Count(role => role.Status == "clear")),
        ReviewRoles = linkedShips.Sum(ship => ship.AssetRoles.Count(role => role.Status == "review")),
        MissingRequiredRoles = linkedShips.Sum(ship =>
            ship.AssetRoles.Count(role => role.Required && role.Candidates.Count == 0)),
        OutputRoot = outputRoot
    };

    private static string NormalizeBaseSize(string value) =>
        value.Equals("medium", StringComparison.OrdinalIgnoreCase)
            ? "large"
            : value.Equals("huge", StringComparison.OrdinalIgnoreCase)
                ? "epic"
                : value.ToLowerInvariant();

    private static string StableId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
}
