using UnifiedToolkit.Conversion.Issues;

namespace UnifiedToolkit.Conversion.Mapping;

public static class ConversionMappingValidator
{
    public static List<ConversionIssue> Validate(ConversionMappingSet mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var issues = new List<ConversionIssue>();

        if (string.IsNullOrWhiteSpace(mappings.Version))
            issues.Add(MappingError("MissingMappingVersion", "The mapping set version is blank."));

        ValidateDuplicateValues(mappings.Ships, x => x.MappingId, "DuplicateMappingId", "mapping ID", issues);
        ValidateDuplicateValues(mappings.Ships, x => x.SourceId, "DuplicateSourceShipMapping", "source ship ID", issues);
        ValidateDuplicateTargetIds(mappings.Ships, issues);

        foreach (var mapping in mappings.Ships)
        {
            if (string.IsNullOrWhiteSpace(mapping.MappingId))
                issues.Add(ShipError(mapping, "MissingMappingId", "MappingId is required."));
            if (string.IsNullOrWhiteSpace(mapping.SourceId))
                issues.Add(ShipError(mapping, "MissingSourceId", "SourceId is required."));

            if (mapping.Kind == ConversionKind.Excluded)
            {
                if (string.IsNullOrWhiteSpace(mapping.ExclusionReason))
                {
                    issues.Add(ShipError(
                        mapping,
                        "MissingExclusionReason",
                        "Excluded mappings must include an exclusion reason."));
                }

                if (!string.IsNullOrWhiteSpace(mapping.TargetId))
                {
                    issues.Add(ShipError(
                        mapping,
                        "ExcludedMappingHasTargetId",
                        "Excluded mappings must not define a target ID."));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(mapping.TargetId))
                issues.Add(ShipError(mapping, "MissingTargetId", "TargetId is required for non-excluded mappings."));
            if (string.IsNullOrWhiteSpace(mapping.Name))
                issues.Add(ShipError(mapping, "MissingTargetName", "Name is required for non-excluded mappings."));
            if (string.IsNullOrWhiteSpace(mapping.Size))
                issues.Add(ShipError(mapping, "MissingTargetSize", "Size is required for non-excluded mappings."));
            if (mapping.Attack < 0 || mapping.Agility < 0 || mapping.Hull < 0 || mapping.Shields < 0)
                issues.Add(ShipError(mapping, "NegativeShipStat", "Ship statistics cannot be negative."));
        }

        return issues;
    }

    private static void ValidateDuplicateTargetIds(
        IEnumerable<ShipMapping> mappings,
        ICollection<ConversionIssue> issues)
    {
        foreach (var group in mappings
                     .Where(x => x.Kind != ConversionKind.Excluded && !string.IsNullOrWhiteSpace(x.TargetId))
                     .GroupBy(x => x.TargetId, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
        {
            var sources = string.Join(", ", group.Select(x => x.SourceId).OrderBy(x => x));
            issues.Add(MappingError(
                "DuplicateTargetShipId",
                $"Target ship ID '{group.Key}' is used by multiple source mappings: {sources}."));
        }
    }

    private static void ValidateDuplicateValues(
        IEnumerable<ShipMapping> mappings,
        Func<ShipMapping, string> selector,
        string code,
        string label,
        ICollection<ConversionIssue> issues)
    {
        foreach (var group in mappings
                     .Where(x => !string.IsNullOrWhiteSpace(selector(x)))
                     .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
        {
            issues.Add(MappingError(code, $"Duplicate {label} '{group.Key}'."));
        }
    }

    private static ConversionIssue MappingError(string code, string message) => new()
    {
        Severity = "Error",
        Category = "Mapping",
        Code = code,
        SourceType = "MappingSet",
        Message = message
    };

    private static ConversionIssue ShipError(ShipMapping mapping, string code, string message) => new()
    {
        Severity = "Error",
        Category = "Mapping",
        Code = code,
        SourceType = "Ship",
        SourceId = mapping.SourceId,
        TargetId = mapping.TargetId,
        Message = message
    };
}
