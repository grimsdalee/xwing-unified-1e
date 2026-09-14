using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.Runtime.Loadouts;

public sealed class FirstEditionShieldUpgradeHandler
{
    public const string UpgradeXws = "shieldupgrade";
    public const string MechanicId = "stat-change";
    public const double TokenSpacing = 0.9;
    private const double OwnerSearchRadius = 4.0;
    private const string ShieldMeshPath = "assets/source/unified1e/gameplay-tokens/meshes/shield.obj";
    private const string ShieldFacePath = "assets/source/unified1e/gameplay-tokens/faces/shield.png";

    public FirstEditionShieldTokenResult Apply(JsonObject save, FirstEditionRuntimeOwnerBinding owner,
        string pilotName, string? assetBaseUrl = null)
    {
        var objects = save["ObjectStates"]?.AsArray()
            ?? throw new InvalidDataException("The TTS save has no ObjectStates array.");
        var index = Index(save);
        if (!index.TryGetValue(owner.PilotCardGuid, out var pilotCard))
            throw new InvalidDataException($"Pilot card '{owner.PilotCardGuid}' was not found.");

        var pilotX = Number(pilotCard, "Transform", "posX");
        var pilotZ = Number(pilotCard, "Transform", "posZ");
        var rotationY = Number(pilotCard, "Transform", "rotY");
        var radians = rotationY * Math.PI / 180.0;
        var rowX = Math.Cos(radians);
        var rowZ = Math.Sin(radians);

        var nearby = objects.OfType<JsonObject>()
            .Where(item => Text(item, "Nickname").Equals("Shield", StringComparison.OrdinalIgnoreCase))
            .Select(item => new ShieldCandidate(item,
                Number(item, "Transform", "posX"), Number(item, "Transform", "posZ")))
            .Where(item => Distance(item.X, item.Z, pilotX, pilotZ) <= OwnerSearchRadius)
            .OrderBy(item => Distance(item.X, item.Z, pilotX, pilotZ))
            .ToList();
        if (nearby.Count == 0)
            throw new InvalidDataException("No existing shield token was found beside the selected pilot card.");

        var template = nearby[0].Object;
        var end = nearby.MaxBy(item => (item.X - pilotX) * rowX + (item.Z - pilotZ) * rowZ)!;
        var tokenX = end.X + rowX * TokenSpacing;
        var tokenZ = end.Z + rowZ * TokenSpacing;
        var used = index.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tokenGuid = GuidFor($"{owner.ShipGuid}:{UpgradeXws}:additional-shield-token", used);
        var baseUrl = (assetBaseUrl ?? "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/").TrimEnd('/') + "/";
        var lua = Text(template, "LuaScript");
        if (lua.Length == 0 || !lua.Contains("__XW_TokenType = 'Shield'", StringComparison.Ordinal))
            throw new InvalidDataException("The nearby Shield object does not contain the validated Unified Shield flip handler.");
        var bundleReturnIndex = lua.LastIndexOf("return __bundle_require(", StringComparison.Ordinal);
        if (bundleReturnIndex < 0)
            throw new InvalidDataException("The nearby Shield object's bundled Lua return statement was not found.");
        var ownerInitialization =
            $"function onLoad() self.setVar(\"shield_owner\", {LuaString(pilotName)}) end\n";
        lua = lua.Insert(bundleReturnIndex, ownerInitialization);

        var token = template.DeepClone().AsObject();
        token["GUID"] = tokenGuid;
        token["Transform"] = Transform(tokenX, Math.Max(1.05, Number(template, "Transform", "posY")), tokenZ,
            0, rotationY, 0, 0.375, 0.375, 0.375);
        token["Description"] = $"Shield Upgrade — owned by {pilotName}";
        token["GMNotes"] = new JsonObject
        {
            ["kind"] = "first-edition-upgrade-resource-token",
            ["tokenType"] = "shield",
            ["sourceUpgradeXws"] = UpgradeXws,
            ["pilotCardGuid"] = owner.PilotCardGuid,
            ["shipGuid"] = owner.ShipGuid,
            ["controllerGuid"] = owner.ControllerGuid,
            ["ownerPilotName"] = pilotName,
            ["activationStatus"] = "active"
        }.ToJsonString();
        token["CustomMesh"] = new JsonObject
        {
            ["MeshURL"] = AssetUrl(baseUrl, ShieldMeshPath),
            ["DiffuseURL"] = AssetUrl(baseUrl, ShieldFacePath),
            ["NormalURL"] = "",
            ["ColliderURL"] = AssetUrl(baseUrl, ShieldMeshPath),
            ["Convex"] = true,
            ["MaterialIndex"] = 1,
            ["TypeIndex"] = 0,
            ["CastShadows"] = true
        };
        token["LuaScript"] = lua;
        token["LuaScriptState"] = "";
        token.Remove("States");
        objects.Add(token);

        return new FirstEditionShieldTokenResult
        {
            TokenGuid = tokenGuid,
            TemplateGuid = Text(template, "GUID"),
            ExistingShieldTokenGuids = nearby.Select(item => Text(item.Object, "GUID")).ToList(),
            X = tokenX,
            Y = Number(token, "Transform", "posY"),
            Z = tokenZ,
            RotationY = rotationY,
            MeshRepositoryPath = ShieldMeshPath,
            FaceRepositoryPath = ShieldFacePath,
            PreservesUnifiedFlipHandler = true
        };
    }

    private static Dictionary<string, JsonObject> Index(JsonNode root) => Descendants(root)
        .Where(item => Text(item, "GUID").Length > 0)
        .GroupBy(item => Text(item, "GUID"), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<JsonObject> Descendants(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["GUID"] is not null) yield return obj;
            foreach (var pair in obj) if (pair.Value is not null)
                foreach (var child in Descendants(pair.Value)) yield return child;
        }
        else if (node is JsonArray array)
            foreach (var item in array) if (item is not null)
                foreach (var child in Descendants(item)) yield return child;
    }
    private static double Number(JsonObject obj, string parent, string key) =>
        obj[parent]?[key]?.GetValue<double>() ?? 0;
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static double Distance(double x1, double z1, double x2, double z2) =>
        Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(z1 - z2, 2));
    private static JsonObject Transform(double x, double y, double z, double rx, double ry, double rz,
        double sx, double sy, double sz) => new()
    {
        ["posX"] = x, ["posY"] = y, ["posZ"] = z,
        ["rotX"] = rx, ["rotY"] = ry, ["rotZ"] = rz,
        ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = sz
    };
    private static string AssetUrl(string baseUrl, string path) => baseUrl + path.Replace('\\', '/').TrimStart('/');
    private static string LuaString(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    private static string GuidFor(string seed, HashSet<string> used)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        for (var offset = 0; offset <= bytes.Length - 3; offset += 3)
        {
            var guid = Convert.ToHexString(bytes.AsSpan(offset, 3)).ToLowerInvariant();
            if (used.Add(guid)) return guid;
        }
        throw new InvalidOperationException("Could not allocate a unique deterministic TTS GUID.");
    }
    private sealed record ShieldCandidate(JsonObject Object, double X, double Z);
}

public sealed class FirstEditionShieldTokenResult
{
    public string TokenGuid { get; init; } = "";
    public string TemplateGuid { get; init; } = "";
    public List<string> ExistingShieldTokenGuids { get; init; } = new();
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double RotationY { get; init; }
    public string MeshRepositoryPath { get; init; } = "";
    public string FaceRepositoryPath { get; init; } = "";
    public bool PreservesUnifiedFlipHandler { get; init; }
}
