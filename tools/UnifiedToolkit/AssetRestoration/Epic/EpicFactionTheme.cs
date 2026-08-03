namespace UnifiedToolkit.AssetRestoration.Epic;

public sealed class EpicFactionTheme
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public EpicThemeColour PrimaryArcColour { get; set; } = new();
    public EpicThemeColour ArcFillColour { get; set; } = new();
    public EpicThemeColour RearGuideColour { get; set; } = new();
    public EpicThemeColour AccentColour { get; set; } = new();
    public EpicThemeColour DashboardAccentColour { get; set; } = new();
    public string CalibrationStatus { get; set; } = "ReferenceCalibrated";
    public List<string> ReferenceNotes { get; set; } = new();
}

public sealed class EpicThemeColour
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte A { get; set; } = 255;
}

public static class EpicFactionThemeCatalogue
{
    public static IReadOnlyDictionary<string, EpicFactionTheme> All { get; } =
        new Dictionary<string, EpicFactionTheme>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["rebelalliance"] = new()
            {
                Id = "rebelalliance",
                DisplayName = "Rebel Alliance",
                PrimaryArcColour = Colour(221, 19, 48),
                ArcFillColour = Colour(91, 8, 23, 78),
                RearGuideColour = Colour(221, 19, 48),
                AccentColour = Colour(188, 190, 196),
                DashboardAccentColour = Colour(142, 48, 52),
                ReferenceNotes = new List<string>
                {
                    "Calibrated from the supplied Rebel small-base example.",
                    "Uses crimson arc lines and a dark burgundy firing sector."
                }
            },
            ["galacticempire"] = new()
            {
                Id = "galacticempire",
                DisplayName = "Galactic Empire",
                PrimaryArcColour = Colour(139, 224, 10),
                ArcFillColour = Colour(40, 90, 13, 78),
                RearGuideColour = Colour(139, 224, 10),
                AccentColour = Colour(188, 190, 196),
                DashboardAccentColour = Colour(77, 110, 56),
                ReferenceNotes = new List<string>
                {
                    "Calibrated from the supplied Imperial small-base example.",
                    "Uses bright lime-green arc lines and a dark green firing sector."
                }
            },
            ["scumandvillainy"] = new()
            {
                Id = "scumandvillainy",
                DisplayName = "Scum and Villainy",
                PrimaryArcColour = Colour(255, 224, 103),
                ArcFillColour = Colour(101, 85, 22, 82),
                RearGuideColour = Colour(255, 224, 103),
                AccentColour = Colour(188, 190, 196),
                DashboardAccentColour = Colour(166, 126, 47),
                ReferenceNotes = new List<string>
                {
                    "Calibrated from the supplied Scum small-base example.",
                    "Uses pale yellow arc lines and an olive/mustard firing sector."
                }
            }
        };

    public static EpicFactionTheme Get(string factionId)
    {
        if (!All.TryGetValue(factionId, out var theme))
        {
            throw new KeyNotFoundException(
                $"No Epic faction theme exists for '{factionId}'.");
        }

        return theme;
    }

    private static EpicThemeColour Colour(
        byte r,
        byte g,
        byte b,
        byte a = 255) =>
        new()
        {
            R = r,
            G = g,
            B = b,
            A = a
        };
}
