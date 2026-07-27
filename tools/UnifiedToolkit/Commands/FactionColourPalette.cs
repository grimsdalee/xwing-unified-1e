using SkiaSharp;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Canonical First Edition faction accent colours used by generated assets.
/// </summary>
internal static class FactionColourPalette
{
    public static SKColor GetPrimary(string factionId)
    {
        return Normalise(factionId) switch
        {
            "galacticempire" => new SKColor(38, 67, 91, 255),
            "firstorder" => new SKColor(62, 62, 68, 255),
            "scumandvillainy" => new SKColor(245, 153, 27, 255),
            "resistance" => new SKColor(163, 62, 22, 255),
            _ => new SKColor(105, 24, 30, 255)
        };
    }

    public static SKColor GetDarkerRim(string factionId, byte reduction = 18)
    {
        var colour = GetPrimary(factionId);

        return new SKColor(
            Subtract(colour.Red, reduction),
            Subtract(colour.Green, reduction),
            Subtract(colour.Blue, reduction),
            255);
    }

    private static byte Subtract(byte value, byte amount)
    {
        return value > amount
            ? (byte)(value - amount)
            : (byte)0;
    }

    private static string Normalise(string value)
    {
        return new string(
            (value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }
}
