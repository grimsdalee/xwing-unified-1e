using System.Text;
using System.Text.Json;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicShipTargetingLayoutBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Build(
        string repositoryRoot,
        string? outputPath = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);

        var catalogue = new EpicShipTargetingLayoutCatalogue
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Ships = BuildLayouts()
        };

        outputPath ??= Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-ship-targeting-layouts.json");
        outputPath = Path.GetFullPath(outputPath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(outputPath)!);

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(catalogue, JsonOptions),
            new UTF8Encoding(false));

        return outputPath;
    }

    public static EpicShipTargetingLayoutCatalogue Load(
        string repositoryRoot,
        string? path = null)
    {
        path ??= Path.Combine(
            Path.GetFullPath(repositoryRoot),
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-ship-targeting-layouts.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Epic ship targeting-layout catalogue was not found.",
                path);
        }

        return JsonSerializer.Deserialize<EpicShipTargetingLayoutCatalogue>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidDataException(
                "Could not deserialize the Epic targeting-layout catalogue.");
    }

    private static List<EpicShipTargetingLayout> BuildLayouts() =>
        new()
        {
            BuildCr90(),
            BuildRaider(),
            BuildGozanti(),
            BuildGr75(),
            BuildCroc()
        };

    private static EpicShipTargetingLayout BuildCr90() =>
        new()
        {
            ShipId = "cr90corvette",
            ShipName = "CR90 Corvette",
            FactionId = "rebelalliance",
            Divider = new EpicDividerLayout
            {
                Visible = true
            },
            TargetingGeometry = new List<EpicTargetingGeometry>
            {
                Triangle(
                    "fore-port-arc",
                    "Fore",
                    "ForeMount",
                    "ForePortSectionCorners"),
                Triangle(
                    "fore-starboard-arc",
                    "Fore",
                    "ForeMount",
                    "ForeStarboardSectionCorners"),
                Triangle(
                    "aft-port-arc",
                    "Aft",
                    "AftMount",
                    "AftPortSectionCorners"),
                Triangle(
                    "aft-starboard-arc",
                    "Aft",
                    "AftMount",
                    "AftStarboardSectionCorners")
            },
            TurretIndicators = new List<EpicTurretIndicator>
            {
                new()
                {
                    Id = "fore-dual-rotation-arrows",
                    Centre = "ForeMount",
                    Style = "Cr90ClockwiseAnnularArrowOutline"
                }
            },
            ReferenceImage =
                "assets/source/unified1e/reference/epic/" +
                "cr90corvette/CR90_full.jpg",
            Notes = new List<string>
            {
                "Blue Fore/Aft divider.",
                "Four independent red triangular targeting arcs: Fore port, Fore starboard, Aft port and Aft starboard.",
                "Each triangle originates at the relevant mount marker and terminates at the two corners on one side of that large-base section.",
                "The Fore mount carries one clockwise annular-arrow outline with its arrowhead on the right-hand side."
            }
        };

    private static EpicShipTargetingLayout BuildRaider() =>
        new()
        {
            ShipId = "raiderclasscorvette",
            ShipName = "Raider-class Corvette",
            FactionId = "galacticempire",
            Divider = new EpicDividerLayout
            {
                Visible = true
            },
            TargetingGeometry = new List<EpicTargetingGeometry>
            {
                Sector(
                    "raider-fore-sector",
                    "Fore",
                    "DividerCentre",
                    "RaiderForeShoulderCorners"),
                Triangle(
                    "aft-port-sector",
                    "Aft",
                    "AftMount",
                    "RaiderAftPortCorners"),
                Triangle(
                    "aft-starboard-sector",
                    "Aft",
                    "AftMount",
                    "RaiderAftStarboardCorners")
            },
            ReferenceImage =
                "assets/source/unified1e/reference/epic/" +
                "raiderclasscorvette/scans/" +
                "Raider-physical-token-600dpi-20260808.png",
            Notes = new List<string>
            {
                "Blue Fore/Aft divider.",
                "Green Fore V has its apex at the exact calibrated divider centre and terminates at the two Fore/centre shoulder transitions.",
                "The green Fore firing-zone fill continues from the V boundaries to the full Fore edge rather than closing across the two shoulder endpoints.",
                "The Fore shoulder position is the long-base mesh transition at z=-1.783 rather than the extreme Fore edge.",
                "Two green Aft side triangles originate at the Aft mount marker.",
                "The Aft triangles together form an X through the Aft mount: two lines to the divider-side corners and two to the outer Aft corners.",
                "Geometry is derived from the supplied 600 DPI First Edition Raider scan; CR90 targeting geometry is not reused."
            }
        };

    private static EpicShipTargetingLayout BuildGozanti() =>
        new()
        {
            ShipId = "gozanticlasscruiser",
            ShipName = "Gozanti-class Cruiser",
            FactionId = "galacticempire",
            Divider = new EpicDividerLayout
            {
                Visible = true
            },
            TargetingGeometry = new List<EpicTargetingGeometry>
            {
                Sector(
                    "gozanti-fore-sector",
                    "Fore",
                    "DividerCentre",
                    "GozantiForeShoulderCorners")
            },
            ReferenceImage =
                "assets/source/unified1e/reference/epic/" +
                "gozanticlasscruiser/scans/" +
                "Gozanti-physical-token-600dpi-20260808.png",
            Notes = new List<string>
            {
                "Blue Fore/Aft divider.",
                "Green Fore V has its apex at the calibrated divider centre and terminates at the two Fore/centre shoulder transitions.",
                "The green strokes touch but do not cover the blue divider.",
                "The green Fore firing-zone fill continues from the V boundaries to the full Fore edge.",
                "The short Epic mesh preserves the end-section shoulder UV while reducing the physical centre span.",
                "No Aft targeting geometry."
            }
        };

    private static EpicShipTargetingLayout BuildGr75() =>
        new()
        {
            ShipId = "gr75mediumtransport",
            ShipName = "GR-75 Medium Transport",
            FactionId = "rebelalliance",
            Divider = new EpicDividerLayout
            {
                Visible = true
            },
            ReferenceImage =
                "assets/source/unified1e/reference/epic/" +
                "gr75mediumtransport/gr75_base.png",
            Notes = new List<string>
            {
                "Blue Fore/Aft divider.",
                "No targeting or firing arcs."
            }
        };

    private static EpicShipTargetingLayout BuildCroc() =>
        new()
        {
            ShipId = "croccruiser",
            ShipName = "C-ROC Cruiser",
            FactionId = "scumandvillainy",
            Divider = new EpicDividerLayout
            {
                Visible = false
            },
            TargetingGeometry = new List<EpicTargetingGeometry>
            {
                Sector(
                    "fore-sector",
                    "Fore",
                    "BaseCentre",
                    "ForeOuterCorners")
            },
            ReferenceImage =
                "assets/source/unified1e/reference/epic/" +
                "croccruiser/croc_base.png",
            Notes = new List<string>
            {
                "No Fore/Aft divider.",
                "Yellow Fore sector originates at exact full-base centre and reaches the two outer Fore corners.",
                "No Aft targeting geometry."
            }
        };

    private static EpicTargetingGeometry Triangle(
        string id,
        string section,
        string origin,
        string destination) =>
        new()
        {
            Id = id,
            GeometryType = "Triangle",
            Section = section,
            Origin = origin,
            Destination = destination,
            FillEnabled = true
        };

    private static EpicTargetingGeometry Sector(
        string id,
        string section,
        string origin,
        string destination,
        bool fillEnabled = true) =>
        new()
        {
            Id = id,
            GeometryType = "Sector",
            Section = section,
            Origin = origin,
            Destination = destination,
            FillEnabled = fillEnabled
        };
}
