namespace UnifiedToolkit.KnowledgeBase.AssetExtraction;

public sealed class AssetExtractionPlan
{
    public string SchemaVersion { get; init; } = "1.1.0";
    public DateTimeOffset GeneratedUtc { get; init; }
    public string AssetRole { get; init; } = string.Empty;
    public List<AssetExtractionLayout> Layouts { get; init; } = new();
    public List<AssetExtractionSheet> Sheets { get; init; } = new();
}

public sealed class AssetExtractionLayout
{
    public string LayoutId { get; init; } = string.Empty;
    public int Rows { get; init; } = 1;
    public int Columns { get; init; } = 1;
    public double MarginLeft { get; init; }
    public double MarginTop { get; init; }
    public double MarginRight { get; init; }
    public double MarginBottom { get; init; }
    public double HorizontalGap { get; init; }
    public double VerticalGap { get; init; }
}

public sealed class AssetExtractionSheet
{
    public string SheetId { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string LayoutId { get; init; } = string.Empty;
    public List<AssetExtractionEntry> Entries { get; init; } = new();
}

public sealed class AssetExtractionEntry
{
    public string EntityId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public int? Row { get; init; }
    public int? Column { get; init; }
    public double? CropX { get; init; }
    public double? CropY { get; init; }
    public double? CropWidth { get; init; }
    public double? CropHeight { get; init; }
    public string? SuggestionSource { get; init; }
    public double? SuggestionConfidence { get; init; }
    public double? SuggestedCropX { get; init; }
    public double? SuggestedCropY { get; init; }
    public double? SuggestedCropWidth { get; init; }
    public double? SuggestedCropHeight { get; init; }
}

public sealed class AssetExtractionPreparationResult
{
    public int Sheets { get; init; }
    public int Entries { get; init; }
    public int UnresolvedSheets { get; init; }
    public int UnassignedMappings { get; init; }
    public string PlanFile { get; init; } = string.Empty;
    public string HtmlFile { get; init; } = string.Empty;
    public string OutputFolder { get; init; } = string.Empty;
}
