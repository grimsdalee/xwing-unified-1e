using UnifiedToolkit.Conversion.Issues;

namespace UnifiedToolkit.Conversion.Mapping.Dispositions;

public static class ShipDispositionValidator
{
    public static IReadOnlyList<ConversionIssue> Validate(IEnumerable<ShipDisposition> dispositions)
    {
        var items = dispositions.ToList();
        var issues = new List<ConversionIssue>();

        foreach (var duplicate in items.Where(x => !string.IsNullOrWhiteSpace(x.SourceId))
                     .GroupBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
        {
            issues.Add(Error(duplicate.Key, "DuplicateShipDisposition", $"Duplicate disposition for source ship '{duplicate.Key}'."));
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.SourceId))
                issues.Add(Error("", "MissingDispositionSourceId", "Disposition SourceId is required."));
            if (item.Kind == ShipDispositionKind.Unreviewed)
                issues.Add(Error(item.SourceId, "UnreviewedDisposition", "Unreviewed entries cannot be applied."));
            if (string.IsNullOrWhiteSpace(item.Reason))
                issues.Add(Error(item.SourceId, "MissingDispositionReason", "A reviewed disposition must include a reason."));
            if (item.Kind == ShipDispositionKind.Alias && string.IsNullOrWhiteSpace(item.ProposedTargetId))
                issues.Add(Error(item.SourceId, "MissingAliasTargetId", "Alias dispositions must include ProposedTargetId."));
        }

        return issues;
    }

    private static ConversionIssue Error(string sourceId, string code, string message) => new()
    {
        Severity = "Error",
        Category = "Disposition",
        Code = code,
        SourceType = "Ship",
        SourceId = sourceId,
        Message = message
    };
}
