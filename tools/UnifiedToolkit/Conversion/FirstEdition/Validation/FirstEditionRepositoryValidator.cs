using UnifiedToolkit.Conversion.Issues;
using UnifiedToolkit.Conversion.FirstEdition.Pilots;
using UnifiedToolkit.Conversion.FirstEdition.Upgrades;

namespace UnifiedToolkit.Conversion.FirstEdition.Validation;

public static class FirstEditionRepositoryValidator
{
    private static readonly HashSet<string> SupportedSizes=new(StringComparer.OrdinalIgnoreCase){"small","large","huge"};
    public static IReadOnlyList<ConversionIssue> Validate(FirstEditionRepository repository)
    {
        var issues=new List<ConversionIssue>();
        foreach(var ship in repository.Ships){if(string.IsNullOrWhiteSpace(ship.Id))issues.Add(Ship(ship,"BlankTargetShipId","The target ship ID is blank."));if(string.IsNullOrWhiteSpace(ship.Name))issues.Add(Ship(ship,"BlankTargetShipName","The target ship name is blank."));if(!SupportedSizes.Contains(ship.Size))issues.Add(Ship(ship,"UnknownFirstEditionShipSize",$"Unknown First Edition ship size '{ship.Size}'."));if(ship.Attack<0||ship.Agility<0||ship.Hull<=0||ship.Shields<0)issues.Add(Ship(ship,"InvalidFirstEditionShipStats","Target ship statistics are invalid."));}
        foreach(var pilot in repository.Pilots){if(string.IsNullOrWhiteSpace(pilot.Id))issues.Add(Pilot(pilot,"BlankTargetPilotId","The target pilot ID is blank."));if(repository.FindShip(pilot.ShipId) is null)issues.Add(Pilot(pilot,"UnknownTargetPilotShip",$"Target ship '{pilot.ShipId}' does not exist."));if(pilot.PilotSkill<0||pilot.PilotSkill>12)issues.Add(Pilot(pilot,"InvalidPilotSkill",$"Pilot skill {pilot.PilotSkill} is invalid."));if(pilot.SquadPointCost<0)issues.Add(Pilot(pilot,"InvalidPilotCost",$"Squad point cost {pilot.SquadPointCost} is invalid."));}
        foreach(var upgrade in repository.Upgrades){if(string.IsNullOrWhiteSpace(upgrade.Id))issues.Add(Upgrade(upgrade,"BlankTargetUpgradeId","The target upgrade ID is blank."));if(string.IsNullOrWhiteSpace(upgrade.Slot))issues.Add(Upgrade(upgrade,"BlankTargetUpgradeSlot","The target upgrade slot is blank."));if(upgrade.SquadPointCost<0)issues.Add(Upgrade(upgrade,"InvalidUpgradeCost",$"Squad point cost {upgrade.SquadPointCost} is invalid."));}
        return issues;
    }
    private static ConversionIssue Ship(FirstEditionShip s,string code,string msg)=>new(){Severity="Error",Category="TargetValidation",Code=code,SourceType="Ship",SourceId=s.Provenance.SourceId,SourceName=s.Name,TargetId=s.Id,Message=msg};
    private static ConversionIssue Upgrade(FirstEditionUpgrade u,string code,string msg)=>new(){Severity="Error",Category="TargetValidation",Code=code,SourceType="Upgrade",SourceId=u.Provenance.SourceId,SourceName=u.Name,TargetId=u.Id,Message=msg};
    private static ConversionIssue Pilot(FirstEditionPilot p,string code,string msg)=>new(){Severity="Error",Category="TargetValidation",Code=code,SourceType="Pilot",SourceId=p.Provenance.SourceId,SourceName=p.Name,TargetId=p.Id,Message=msg};
}
