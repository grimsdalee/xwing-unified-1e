using UnifiedToolkit.Conversion.FirstEdition;

namespace UnifiedToolkit.Assets;

public static class FirstEditionAssetMatcher
{
    public static IReadOnlyList<AssetRoleRequirement> Requirements(FirstEditionRepository repository)
    {
        var result = new List<AssetRoleRequirement>();
        foreach (var ship in repository.Ships)
        {
            var key = new AssetEntityKey { EntityType = "ship", EntityId = ship.Id };
            result.Add(new AssetRoleRequirement { Entity = key, EntityName = ship.Name, Role = AssetRole.ShipModel, ChassisId = ship.Id, ShipSize = ship.Size });
            result.Add(new AssetRoleRequirement { Entity = key, EntityName = ship.Name, Role = AssetRole.ShipTexture, ChassisId = ship.Id, ShipSize = ship.Size });
            result.Add(new AssetRoleRequirement { Entity = key, EntityName = ship.Name, Role = AssetRole.ShipBase, Required = !ship.Size.Equals("huge", StringComparison.OrdinalIgnoreCase), ChassisId = ship.Id, ShipSize = ship.Size });
            result.Add(new AssetRoleRequirement { Entity = key, EntityName = ship.Name, Role = AssetRole.ShipDial, ChassisId = ship.Id, ShipSize = ship.Size });
            result.Add(new AssetRoleRequirement { Entity = key, EntityName = ship.Name, Role = AssetRole.ShipTemplate, ChassisId = ship.Id, ShipSize = ship.Size });
        }

        foreach (var pilot in repository.Pilots)
            result.Add(new AssetRoleRequirement
            {
                Entity = new AssetEntityKey { EntityType = "pilot", EntityId = pilot.Id, ShipId = pilot.ShipId, Faction = pilot.Faction },
                EntityName = pilot.Name,
                Role = AssetRole.PilotCard,
                ChassisId = pilot.ShipId
            });

        foreach (var upgrade in repository.Upgrades)
            result.Add(new AssetRoleRequirement
            {
                Entity = new AssetEntityKey { EntityType = "upgrade", EntityId = upgrade.Id, Slot = upgrade.Slot },
                EntityName = upgrade.Name,
                Role = AssetRole.UpgradeCard
            });

        return result;
    }

    public static IReadOnlyList<AssetMatchCandidate> Match(FirstEditionRepository repository, AssetCatalogue catalogue)
    {
        var all = new List<AssetMatchCandidate>();
        foreach (var requirement in Requirements(repository))
            all.AddRange(MatchRequirement(requirement, catalogue.Assets));

        var recommendedIds = all
            .Where(x => x.ConfidenceBand != AssetConfidenceBand.Rejected)
            .GroupBy(x => (x.SemanticKey, x.Role))
            .Select(g => g.OrderByDescending(x => x.Score).ThenBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase).First())
            .Select(x => (x.SemanticKey, x.Role, x.AssetId))
            .ToHashSet();

        return all.Select(x => new AssetMatchCandidate
        {
            EntityType = x.EntityType,
            EntityId = x.EntityId,
            EntityName = x.EntityName,
            EntityShipId = x.EntityShipId,
            EntityFaction = x.EntityFaction,
            EntitySlot = x.EntitySlot,
            SemanticKey = x.SemanticKey,
            Role = x.Role,
            AssetId = x.AssetId,
            AssetName = x.AssetName,
            AssetKind = x.AssetKind,
            StructuralClass = x.StructuralClass,
            SourceKind = x.SourceKind,
            Location = x.Location,
            MatchMethod = x.MatchMethod,
            Confidence = x.Confidence,
            RoleScore = x.RoleScore,
            ContextScore = x.ContextScore,
            Score = x.Score,
            ConfidenceBand = x.ConfidenceBand,
            Recommended = recommendedIds.Contains((x.SemanticKey, x.Role, x.AssetId)),
            Notes = x.Notes
        })
        .OrderBy(x => x.SemanticKey, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Role)
        .ThenByDescending(x => x.Score)
        .ThenBy(x => x.AssetName, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    public static IReadOnlyList<AssetResolutionReview> BuildReview(
        IEnumerable<AssetRoleRequirement> requirements,
        IEnumerable<AssetMatchCandidate> candidates)
    {
        var lookup = candidates
            .Where(x => x.ConfidenceBand != AssetConfidenceBand.Rejected)
            .GroupBy(x => (x.SemanticKey, x.Role))
            .ToDictionary(x => x.Key, x => x.OrderByDescending(v => v.Score).Take(10).ToArray());

        return requirements.Select(requirement =>
        {
            lookup.TryGetValue((requirement.Entity.SemanticKey, requirement.Role), out var found);
            found ??= Array.Empty<AssetMatchCandidate>();
            var decision = !requirement.Required && found.Length == 0
                ? "NotRequired"
                : found.Length == 0
                    ? "Missing"
                    : found[0].ConfidenceBand == AssetConfidenceBand.AutoApprovable
                        ? "Recommended"
                        : "Unreviewed";

            return new AssetResolutionReview
            {
                Entity = requirement.Entity,
                EntityName = requirement.EntityName,
                Role = requirement.Role,
                Required = requirement.Required,
                Decision = decision,
                Candidates = found.Select(x => new AssetResolutionCandidate
                {
                    AssetId = x.AssetId,
                    AssetName = x.AssetName,
                    AssetKind = x.AssetKind,
                    StructuralClass = x.StructuralClass,
                    SourceKind = x.SourceKind,
                    Location = x.Location,
                    Score = x.Score,
                    ConfidenceBand = x.ConfidenceBand,
                    MatchMethod = x.MatchMethod,
                    Notes = x.Notes
                }).ToArray()
            };
        }).ToArray();
    }

    private static IEnumerable<AssetMatchCandidate> MatchRequirement(AssetRoleRequirement requirement, IEnumerable<AssetRecord> assets)
    {
        var id = AssetText.Normalize(requirement.Entity.EntityId);
        var name = AssetText.Normalize(requirement.EntityName);
        var aliases = AliasTerms(id, name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var asset in assets)
        {
            var compatibility = Compatibility(requirement, asset);
            if (!compatibility.Accepted)
                continue;

            var assetName = AssetText.Normalize(asset.Name);
            var terms = asset.SearchTerms.Select(AssetText.Normalize).Where(x => x.Length > 0).Append(assetName).Distinct().ToArray();

            decimal confidence;
            string method;
            if (requirement.Role == AssetRole.ShipBase)
            {
                confidence = 0.95m;
                method = "SharedBaseBySize";
            }
            else if (aliases.Any(x => assetName == x))
            {
                confidence = 1.00m;
                method = "ExactNormalizedName";
            }
            else if (aliases.Any(a => terms.Any(x => x == a)))
            {
                confidence = 0.95m;
                method = "ExactSearchTerm";
            }
            else if (aliases.Where(x => x.Length >= 4).Any(a => terms.Any(x => x.Contains(a, StringComparison.OrdinalIgnoreCase))))
            {
                confidence = 0.75m;
                method = "ContainedSearchTerm";
            }
            else
            {
                continue;
            }

            var context = ContextScore(requirement, asset);
            var score = Math.Min(1.00m, confidence * 0.55m + compatibility.RoleScore * 0.25m + context * 0.20m);
            var band = Band(score, confidence, context, method);

            yield return new AssetMatchCandidate
            {
                EntityType = requirement.Entity.EntityType,
                EntityId = requirement.Entity.EntityId,
                EntityName = requirement.EntityName,
                EntityShipId = requirement.Entity.ShipId,
                EntityFaction = requirement.Entity.Faction,
                EntitySlot = requirement.Entity.Slot,
                SemanticKey = requirement.Entity.SemanticKey,
                Role = requirement.Role,
                AssetId = asset.AssetId,
                AssetName = asset.Name,
                AssetKind = asset.Kind,
                StructuralClass = asset.StructuralClass,
                SourceKind = asset.SourceKind,
                Location = string.IsNullOrWhiteSpace(asset.Url) ? FirstNonEmpty(asset.RelativePath, asset.SourcePath) : asset.Url,
                MatchMethod = method,
                Confidence = confidence,
                RoleScore = compatibility.RoleScore,
                ContextScore = context,
                Score = score,
                ConfidenceBand = band,
                Notes = BuildNotes(asset, compatibility.Note)
            };
        }
    }

    private static (bool Accepted, decimal RoleScore, string Note) Compatibility(AssetRoleRequirement requirement, AssetRecord asset)
    {
        var expected = requirement.Role switch
        {
            AssetRole.ShipModel => new[] { AssetStructuralClass.ShipModel },
            AssetRole.ShipTexture => new[] { AssetStructuralClass.ShipTexture },
            AssetRole.ShipBase => ExpectedBaseClasses(requirement.ShipSize),
            AssetRole.ShipDial => new[] { AssetStructuralClass.DialBag, AssetStructuralClass.DialObject },
            AssetRole.ShipTemplate => new[] { AssetStructuralClass.ShipObjectTemplate },
            AssetRole.PilotCard => new[] { AssetStructuralClass.PilotCardImage, AssetStructuralClass.CardObjectTemplate },
            AssetRole.UpgradeCard => new[] { AssetStructuralClass.UpgradeCardImage, AssetStructuralClass.CardObjectTemplate },
            _ => Array.Empty<AssetStructuralClass>()
        };

        if (!expected.Contains(asset.StructuralClass))
            return (false, 0m, $"Rejected structural class {asset.StructuralClass} for role {requirement.Role}.");

        if (requirement.Role == AssetRole.ShipTemplate && asset.StructuralClass != AssetStructuralClass.ShipObjectTemplate)
            return (false, 0m, "Dial bags and generic model bags are not ship object templates.");

        if (requirement.Role == AssetRole.ShipTexture && asset.StructuralClass != AssetStructuralClass.ShipTexture)
            return (false, 0m, "Pilot and card images cannot be used as ship textures.");

        return (true, asset.StructuralClass switch
        {
            AssetStructuralClass.ShipModel => 1.00m,
            AssetStructuralClass.ShipTexture => 1.00m,
            AssetStructuralClass.SharedSmallBase or AssetStructuralClass.SharedLargeBase or AssetStructuralClass.SharedHugeBase => 1.00m,
            AssetStructuralClass.DialObject => 1.00m,
            AssetStructuralClass.DialBag => 0.90m,
            AssetStructuralClass.ShipObjectTemplate => 1.00m,
            AssetStructuralClass.PilotCardImage or AssetStructuralClass.UpgradeCardImage => 1.00m,
            AssetStructuralClass.CardObjectTemplate => 0.85m,
            _ => 0.50m
        }, "");
    }

    private static AssetStructuralClass[] ExpectedBaseClasses(string size) => AssetText.Normalize(size) switch
    {
        "large" => new[] { AssetStructuralClass.SharedLargeBase },
        "huge" => new[] { AssetStructuralClass.SharedHugeBase },
        _ => new[] { AssetStructuralClass.SharedSmallBase }
    };

    private static decimal ContextScore(AssetRoleRequirement requirement, AssetRecord asset)
    {
        var text = AssetText.Normalize(string.Join(" ", asset.Name, asset.RelativePath, asset.TtsType, asset.JsonPointer, asset.ChassisContext, asset.FactionContext, asset.SizeContext));
        decimal score = 0.25m;

        var chassis = AssetText.Normalize(requirement.ChassisId);
        if (!string.IsNullOrWhiteSpace(chassis) && (text.Contains(chassis) || ChassisEquivalent(chassis, text))) score += 0.45m;
        if (!string.IsNullOrWhiteSpace(requirement.Entity.Faction) && text.Contains(AssetText.Normalize(requirement.Entity.Faction))) score += 0.15m;
        if (!string.IsNullOrWhiteSpace(requirement.Entity.Slot) && text.Contains(AssetText.Normalize(requirement.Entity.Slot))) score += 0.10m;
        if (!string.IsNullOrWhiteSpace(requirement.ShipSize) && text.Contains(AssetText.Normalize(requirement.ShipSize))) score += 0.15m;

        if (IsKnownCollision(requirement, text)) return 0m;
        return Math.Min(1m, score);
    }

    private static bool IsKnownCollision(AssetRoleRequirement requirement, string text)
    {
        var id = AssetText.Normalize(requirement.Entity.EntityId);
        if (id == "aggressor" && text.Contains("tieaggressor")) return true;
        if (id == "tieaggressor" && text.Contains("aggressorassaultfighter")) return true;
        return false;
    }

    private static bool ChassisEquivalent(string chassis, string text)
    {
        var aliases = chassis switch
        {
            "aggressor" => new[] { "aggressorassaultfighter", "scuaggressor" },
            "awing" => new[] { "rz1awing", "rebawing" },
            "xwing" => new[] { "t65xwing", "rebxwing" },
            "tieadvanced" => new[] { "tieadvancedx1" },
            "tieadvprototype" => new[] { "tieadvancedv1", "tieadvv1" },
            "protectoratestarfighter" => new[] { "fangfighter" },
            "yt1300" => new[] { "modifiedyt1300lightfreighter" },
            _ => Array.Empty<string>()
        };
        return aliases.Any(text.Contains);
    }

    private static AssetConfidenceBand Band(decimal score, decimal confidence, decimal context, string method)
    {
        if (score >= 0.90m && confidence >= 0.95m && context >= 0.70m && method is not "ContainedSearchTerm")
            return AssetConfidenceBand.AutoApprovable;
        if (score >= 0.60m)
            return AssetConfidenceBand.ReviewRequired;
        return AssetConfidenceBand.Rejected;
    }

    private static IEnumerable<string> AliasTerms(string id, string name)
    {
        yield return id;
        yield return name;
        if (id == "deadmansswitch" || name == "deadmansswitch")
        {
            yield return "deadmansswitch";
            yield return "deadmansswitches";
        }
        if (id == "houndstooth" || name == "houndstooth")
        {
            yield return "houndstooth";
            yield return "houndstooths";
        }
        if (id.EndsWith("s", StringComparison.Ordinal) && id.Length > 4) yield return id[..^1];
        if (name.EndsWith("s", StringComparison.Ordinal) && name.Length > 4) yield return name[..^1];
    }

    private static string BuildNotes(AssetRecord asset, string compatibilityNote)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(compatibilityNote)) notes.Add(compatibilityNote);
        if (asset.SourceKind == AssetSourceKind.LegacySaveObject)
            notes.Add($"TTS template GUID {asset.TtsGuid}; type {asset.TtsType}; full template JSON preserved in catalogue.");
        else if (!string.IsNullOrWhiteSpace(asset.TtsGuid))
            notes.Add($"Referenced by TTS GUID {asset.TtsGuid}.");
        if (!string.IsNullOrWhiteSpace(asset.ChassisContext)) notes.Add($"Chassis context: {asset.ChassisContext}.");
        if (!string.IsNullOrWhiteSpace(asset.SizeContext)) notes.Add($"Size context: {asset.SizeContext}.");
        return string.Join(" ", notes);
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
}
