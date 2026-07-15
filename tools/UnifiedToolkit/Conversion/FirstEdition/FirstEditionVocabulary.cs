namespace UnifiedToolkit.Conversion.FirstEdition;

public static class FirstEditionVocabulary
{
    public static readonly IReadOnlySet<string> ShipSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "small",
        "large",
        "huge"
    };

    public static readonly IReadOnlySet<string> Factions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "rebelalliance",
        "galacticempire",
        "scumandvillainy",
        "resistance",
        "firstorder"
    };

    public static readonly IReadOnlySet<string> UpgradeSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "elite",
        "astromech",
        "salvagedastromech",
        "torpedo",
        "missile",
        "cannon",
        "turret",
        "bomb",
        "crew",
        "system",
        "illicit",
        "modification",
        "title",
        "tech",
        "cargo",
        "team",
        "hardpoint"
    };

    // First Edition Epic ships are represented by multiple card sections. They remain
    // valid restriction targets even though Phase 3 currently converts only normal ships.
    public static readonly IReadOnlySet<string> DeferredEpicShipSectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cr90corvettefore",
        "cr90corvetteaft",
        "raiderclasscorvettefore",
        "raiderclasscorvetteaft"
    };
}
