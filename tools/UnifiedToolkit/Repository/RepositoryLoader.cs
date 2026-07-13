using UnifiedToolkit.XWing;

namespace UnifiedToolkit.Repository;

public static class RepositoryLoader
{
    public static Repository Load(string repoFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoFolder);

        var ships = ShipParser.ParseFromRepo(repoFolder);
        var pilots = PilotParser.ParseFromRepo(repoFolder);
        var upgrades = UpgradeParser.ParseFromRepo(repoFolder);

        PilotShipLinker.Link(pilots, ships);

        return new Repository(
            ships,
            pilots,
            upgrades);
    }
}