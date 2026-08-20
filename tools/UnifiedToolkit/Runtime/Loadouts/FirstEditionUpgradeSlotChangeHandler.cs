namespace UnifiedToolkit.Runtime.Loadouts;

public static class FirstEditionUpgradeSlotChangeHandler
{
    public const string R2D6Xws = "r2d6";

    public static bool Supports(string upgradeXws) =>
        Key(upgradeXws) == R2D6Xws;

    public static FirstEditionSlotChangeResult Apply(string upgradeXws, FirstEditionLoadoutPilot pilot,
        List<FirstEditionLoadoutSlot> slots)
    {
        if (!Supports(upgradeXws))
            return new FirstEditionSlotChangeResult
            {
                UpgradeXws = upgradeXws,
                ErrorCode = "slot-change-handler-not-implemented",
                Message = $"No reviewed structural slot handler exists for '{upgradeXws}'."
            };

        if (pilot.PrintedUpgradeSlots.Any(slot =>
                FirstEditionLoadoutPlanner.NormalizeSlot(slot) == "Elite"))
            return new FirstEditionSlotChangeResult
            {
                UpgradeXws = R2D6Xws,
                ErrorCode = "r2d6-existing-elite-slot",
                Message = "R2-D6 cannot be equipped when the pilot already has a printed Elite slot."
            };

        if (pilot.PilotSkill <= 2)
            return new FirstEditionSlotChangeResult
            {
                UpgradeXws = R2D6Xws,
                ErrorCode = "r2d6-pilot-skill",
                Message = "R2-D6 requires pilot skill 3 or higher."
            };

        if (!slots.Any(slot => slot.Type == "Astromech" && slot.AssignedUpgradeXws is null))
            return new FirstEditionSlotChangeResult
            {
                UpgradeXws = R2D6Xws,
                ErrorCode = "r2d6-astromech-slot",
                Message = "R2-D6 requires an available Astromech slot."
            };

        if (slots.Any(slot => slot.Source.Equals("upgrade:r2d6", StringComparison.OrdinalIgnoreCase)))
            return new FirstEditionSlotChangeResult
            {
                UpgradeXws = R2D6Xws,
                ErrorCode = "r2d6-duplicate-slot-change",
                Message = "R2-D6's Elite-slot change has already been applied to this loadout."
            };

        var ordinal = slots.Count(slot => slot.Type == "Elite") + 1;
        var generated = new FirstEditionLoadoutSlot
        {
            Type = "Elite",
            Ordinal = ordinal,
            SlotId = $"elite:{ordinal}",
            Source = $"upgrade:{R2D6Xws}"
        };
        slots.Add(generated);
        return new FirstEditionSlotChangeResult
        {
            UpgradeXws = R2D6Xws,
            Applied = true,
            AddedSlots = new() { generated },
            Message = "R2-D6 generated one Elite slot."
        };
    }

    private static string Key(string? value) =>
        new((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

public sealed class FirstEditionSlotChangeResult
{
    public string UpgradeXws { get; init; } = "";
    public bool Applied { get; init; }
    public string ErrorCode { get; init; } = "";
    public string Message { get; init; } = "";
    public List<FirstEditionLoadoutSlot> AddedSlots { get; init; } = new();
}
