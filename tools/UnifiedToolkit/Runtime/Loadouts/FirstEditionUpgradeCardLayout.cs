namespace UnifiedToolkit.Runtime.Loadouts;

public static class FirstEditionUpgradeCardLayout
{
    private const double SideOffset = 2.75;
    private const double FirstCardDownOffset = 0.75;
    private const double StaggerOffset = 1.40;
    private const double BaseHeight = 1.20;
    private const double StackHeightStep = 0.12;

    public static FirstEditionUpgradeCardPlacement Place(
        double pilotX, double pilotZ, double pilotRotationY, int cardIndex)
    {
        if (cardIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(cardIndex));

        var radians = pilotRotationY * Math.PI / 180.0;
        var rightX = Math.Cos(radians);
        var rightZ = -Math.Sin(radians);
        var downX = Math.Sin(radians);
        var downZ = Math.Cos(radians);
        var downOffset = FirstCardDownOffset + cardIndex * StaggerOffset;

        return new FirstEditionUpgradeCardPlacement
        {
            X = pilotX - rightX * SideOffset + downX * downOffset,
            Y = BaseHeight + cardIndex * StackHeightStep,
            Z = pilotZ - rightZ * SideOffset + downZ * downOffset,
            RotationY = NormalizeRotation(pilotRotationY)
        };
    }

    private static double NormalizeRotation(double rotation)
    {
        rotation %= 360.0;
        return rotation < 0 ? rotation + 360.0 : rotation;
    }
}

public sealed class FirstEditionUpgradeCardPlacement
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double RotationY { get; init; }
}
