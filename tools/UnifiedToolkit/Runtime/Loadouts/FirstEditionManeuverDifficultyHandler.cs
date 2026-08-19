namespace UnifiedToolkit.Runtime.Loadouts;

public static class FirstEditionManeuverDifficultyHandler
{
    public const char UnifiedEasyDifficultyCode = 'b';

    public static List<string> TreatSpeedsAsEasy(IEnumerable<string> moveSet, params int[] speeds)
    {
        var selectedSpeeds = speeds.ToHashSet();
        return moveSet.Select(move => Transform(move, selectedSpeeds)).ToList();
    }

    public static int Speed(string move)
    {
        if (string.IsNullOrWhiteSpace(move)) return -1;
        for (var index = move.Length - 1; index >= 1; index--)
            if (char.IsDigit(move[index])) return move[index] - '0';
        return -1;
    }

    private static string Transform(string move, HashSet<int> selectedSpeeds)
    {
        if (string.IsNullOrWhiteSpace(move) || move.Length < 2 || !selectedSpeeds.Contains(Speed(move)))
            return move;
        return UnifiedEasyDifficultyCode + move[1..];
    }
}
