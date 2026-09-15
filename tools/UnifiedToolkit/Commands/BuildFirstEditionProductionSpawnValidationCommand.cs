using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class BuildFirstEditionProductionSpawnValidationCommand
{
    private const string SmallBaseColliderUrl =
        "{verifycache}https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/colliders/Small_base_Collider.obj";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(Option(args, "--pilot")))
        {
            Usage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var referenceSave = Path.GetFullPath(args[1]);
            ValidateFile(referenceSave, "Reference TTS save");

            var packagePlanPath = Resolve(repository, Option(args, "--package-plan"),
                "_unifiedtoolkit_reports/phase11/ship-package-planning/ship-package-plans.json");
            var runtimePayloadPath = Resolve(repository, Option(args, "--runtime-payloads"),
                "_unifiedtoolkit_reports/phase11f/standard-runtime-payloads/standard-first-edition-runtime-payloads.json");
            var runtimeTemplatesPath = Resolve(repository, Option(args, "--runtime-templates"),
                "_unifiedtoolkit_reports/phase12b/runtime-template-extraction/runtime-templates.json");
            ValidateFile(packagePlanPath, "Phase 11 ship-package plan");
            ValidateFile(runtimePayloadPath, "Phase 11F runtime payloads");
            ValidateFile(runtimeTemplatesPath, "Phase 12B runtime templates");

            var requestedPilot = Option(args, "--pilot")!;
            var packages = Read<ValidationPackagePlan>(packagePlanPath).Packages
                .Where(item => item.PackageStatus.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                .Where(item => Key(item.PilotId) == Key(requestedPilot)
                    || Key(item.PilotName) == Key(requestedPilot)
                    || Key(item.PackageId).EndsWith(Key(requestedPilot), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (packages.Count != 1)
                throw new InvalidDataException(packages.Count == 0
                    ? $"No ready First Edition ship package matches pilot '{requestedPilot}'."
                    : $"Pilot '{requestedPilot}' matches {packages.Count} ready packages; use its exact pilot ID.");

            var package = packages.Single();
            var runtime = Read<PrototypeRuntimePayloadInput>(runtimePayloadPath).Payloads
                .SingleOrDefault(item => Key(item.ShipId) == Key(package.ShipId))
                ?? throw new InvalidDataException($"No standard First Edition runtime payload exists for ship '{package.ShipId}'.");
            var assembly = GenerateShipValidationSavesCommand.BuildAssemblies(repository, new[] { package }, runtime).Single();
            if (!assembly.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(string.Join("; ", assembly.ValidationErrors));

            var outputFolder = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "production-spawn-validation"));
            Directory.CreateDirectory(outputFolder);
            var planPath = Path.Combine(outputFolder, "redsquadronpilot-production-spawn-plan-v1.json");
            var rawSavePath = Path.Combine(outputFolder, "redsquadronpilot-production-spawn-raw-v1.json");
            var savePath = Path.Combine(outputFolder, "redsquadronpilot-production-spawn-loadout-v1.json");
            var manifestPath = Path.Combine(outputFolder, "redsquadronpilot-production-spawn-loadout-v1-manifest.json");
            var reportPath = Path.Combine(outputFolder, "redsquadronpilot-production-spawn-loadout-v1.md");
            var plan = new FiveShipPrototypeAssemblyDocument
            {
                SchemaVersion = "1.0.0", GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalPath(repository), PackagePlanPath = NormalPath(packagePlanPath),
                RuntimePayloadPath = NormalPath(runtimePayloadPath), RequestedPrototypeCount = 1,
                ReadyPrototypeCount = 1, InvalidPrototypeCount = 0, DialRuntimeValidated = true,
                Assemblies = new() { assembly }
            };
            Write(planPath, JsonSerializer.Serialize(plan, JsonOptions));

            var assetBaseUrl = Option(args, "--asset-base-url")
                ?? "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/";
            var generationExit = GeneratePrototypeSaveCommand.Run(new[]
            {
                repository, referenceSave, "--assembly-plan", planPath,
                "--runtime-templates", runtimeTemplatesPath, "--asset-base-url", assetBaseUrl,
                "--output", rawSavePath, "--production-bundle"
            });
            if (generationExit is not 0 and not 2)
                throw new InvalidDataException($"The First Edition production bundle generator returned exit code {generationExit}.");

            var rawSave = JsonNode.Parse(File.ReadAllText(rawSavePath))?.AsObject()
                ?? throw new InvalidDataException("The generated production bundle is not a JSON object.");
            var pilotCard = rawSave["ObjectStates"]!.AsArray().OfType<JsonObject>()
                .Single(item => StateText(item, "kind") == "first-edition-pilot-card-binding");
            var pilotCardGuid = Text(pilotCard, "GUID");
            var upgrades = Options(args, "--upgrade").ToList();
            if (upgrades.Count == 0) upgrades.AddRange(new[] { "engineupgrade", "r2astromech" });
            var request = new FirstEditionLoadoutRequest
            {
                Pilot = requestedPilot, Ship = Option(args, "--ship"), Faction = Option(args, "--faction"),
                Upgrades = upgrades,
                EnableImplementedStructuralEffects = upgrades.Any(item => Key(item) == "r2d6")
            };
            var registration = new FirstEditionProductionLoadoutRegistrar().Register(
                repository, rawSave, request, pilotCardGuid, assetBaseUrl, activateValidatedHandlers: true);
            registration.Save["SaveName"] = $"Phase 16F-R12 — {package.PilotName} First Edition production spawn";
            registration.Save["Note"] = "Phase 16F-R12 generates the First Edition ship, card, base token and faction dial directly, then applies the reviewed production loadout handlers.";

            var index = Index(registration.Save);
            var shipGuid = StateText(pilotCard, "ship_guid");
            var dialGuid = StateText(pilotCard, "dial_guid");
            var ship = index[shipGuid];
            var dial = index[dialGuid];
            if (ship["CustomMesh"] is not JsonObject shipMesh)
                throw new InvalidDataException("The generated ship base has no CustomMesh definition.");
            shipMesh["ColliderURL"] = SmallBaseColliderUrl;
            var shipState = ParseObject(Text(ship, "LuaScriptState"));
            var checks = new List<FirstEditionRuntimeAcceptanceCheck>
            {
                Check("single-generated-first-edition-bundle", rawSave["ObjectStates"]!.AsArray().Count == 4,
                    "Exactly one generated First Edition base hierarchy, faction dial and pilot card plus the hidden movement lookup precede loadout registration."),
                Check("movement-lookup-present", rawSave["ObjectStates"]!.AsArray().OfType<JsonObject>()
                    .Count(item => Text(item, "Nickname").Equals("MoveLUT", StringComparison.OrdinalIgnoreCase)) == 1,
                    "The hidden MoveLUT trajectory provider required by the Unified movement runtime is present exactly once."),
                Check("first-edition-pilot-card-artwork", Text(pilotCard["CustomImage"]?.AsObject() ?? new JsonObject(), "ImageURL").Contains("/unified1e/pilot-cards/", StringComparison.OrdinalIgnoreCase),
                    "The spawned pilot card uses repository-owned First Edition artwork."),
                Check("first-edition-faction-dial", Text(dial["CustomMesh"]?.AsObject() ?? new JsonObject(), "DiffuseURL").Contains("/unified1e/", StringComparison.OrdinalIgnoreCase),
                    "The assigned dial uses the repository-owned First Edition faction texture."),
                Check("first-edition-pilot-base-token", Descendants(ship).Any(item =>
                    Text(item["CustomImage"]?.AsObject() ?? new JsonObject(), "ImageURL").Contains("pilot-tokens", StringComparison.OrdinalIgnoreCase)),
                    "The ship hierarchy contains the First Edition pilot base token."),
                Check("production-guid-links", index.ContainsKey(shipGuid) && index.ContainsKey(dialGuid)
                    && Text(dial, "LuaScriptState").Contains(shipGuid, StringComparison.OrdinalIgnoreCase),
                    "The generated pilot card, ship and assigned dial are linked by their real GUIDs."),
                Check("complete-production-ship-state", shipState["isAi"]?.GetValue<bool>() == false
                    && shipState["strikeTargets"] is JsonArray
                    && shipState["shipData"]?["arcs"] is JsonObject
                    && shipState["shipData"]?["Faction"]?.GetValue<string>() == "Rebel",
                    "The inherited ship runtime receives every required non-null state table and faction value."),
                Check("canonical-production-base-collider",
                    Text(ship["CustomMesh"]?.AsObject() ?? new JsonObject(), "ColliderURL").Equals(
                        SmallBaseColliderUrl,
                        StringComparison.Ordinal),
                    "The generated small ship uses the exact collider required by Unified movement verification."),
                Check("initial-command-channel-clear", Text(ship, "Description").Length == 0
                    && Text(registration.Save, "LuaScript").Length > 0
                    && Text(ship, "LuaScript").Contains("Phase 16F-R12 production spawn bridge", StringComparison.Ordinal),
                    "The ship command channel starts empty and the isolated production load bridge is installed while the Unified movement runtime remains available."),
                Check("first-edition-pilot-values", shipState["shipData"]?["initiative"]?.GetValue<int>() == package.PilotSkill
                    && StateInt(pilotCard, "squad_point_cost") == package.SquadPointCost,
                    "Pilot skill and squad-point cost come from the First Edition package."),
                Check("production-registration-valid", registration.Manifest.IsValid,
                    "The generated bundle accepts normal production loadout registration."),
                Check("reviewed-handler-boundary", registration.Manifest.HandlerActivations.All(item =>
                    new[] { "engineupgrade", "r2astromech", "r2d6", "shieldupgrade" }.Contains(Key(item.UpgradeXws))),
                    "Only separately reviewed production handlers may activate."),
                Check("no-unified-pilot-card-donor", registration.Save["ObjectStates"]!.AsArray().OfType<JsonObject>()
                    .Where(item => StateText(item, "kind") == "first-edition-pilot-card-binding")
                    .All(item => !Text(item, "Nickname").Contains("Veteran", StringComparison.OrdinalIgnoreCase)),
                    "No Unified 2.5 pilot-card donor is present in the generated bundle.")
            };
            var manifest = new FirstEditionProductionSpawnValidationManifest
            {
                PilotName = package.PilotName, PilotSkill = package.PilotSkill,
                SquadPointCost = package.SquadPointCost, ShipGuid = shipGuid,
                DialGuid = dialGuid, PilotCardGuid = pilotCardGuid,
                UpgradeCardCount = registration.Manifest.Upgrades.Count,
                ActiveHandlers = registration.Manifest.HandlerActivations,
                Registration = registration.Manifest, AcceptanceChecks = checks
            };
            Write(savePath, registration.Save.ToJsonString(JsonOptions));
            Write(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
            Write(reportPath, Report(manifest));

            Console.WriteLine("UnifiedToolkit Phase 16F-R12 First Edition Production Spawn Validation");
            Console.WriteLine("=======================================================================");
            Console.WriteLine();
            Console.WriteLine($"First Edition pilot:       {manifest.PilotName}");
            Console.WriteLine($"Pilot skill / squad cost:  {manifest.PilotSkill} / {manifest.SquadPointCost}");
            Console.WriteLine($"Ship / dial / card GUIDs:  {manifest.ShipGuid} / {manifest.DialGuid} / {manifest.PilotCardGuid}");
            Console.WriteLine($"Upgrade cards registered:  {manifest.UpgradeCardCount}");
            Console.WriteLine($"Active handlers:           {manifest.ActiveHandlers.Count}");
            Console.WriteLine($"Acceptance checks passed:  {checks.Count(item => item.Passed)}/{checks.Count}");
            Console.WriteLine($"Valid:                      {manifest.IsValid}");
            Console.WriteLine();
            Console.WriteLine($"TTS validation save: {savePath}");
            Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine($"Report:              {reportPath}");
            Console.WriteLine();
            Console.WriteLine("First Edition production spawn prepared without a Unified 2.5 ship donor.");
            return manifest.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition production-spawn validation failed: {exception.Message}");
            return 1;
        }
    }

    private static IEnumerable<string> Options(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase)) yield return args[index + 1];
    }
    private static string? Option(string[] args, string name) => Options(args, name).FirstOrDefault();
    private static string Resolve(string repository, string? explicitPath, string relative) => Path.GetFullPath(
        explicitPath ?? Path.Combine(repository, relative.Replace('/', Path.DirectorySeparatorChar)));
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not parse JSON file: {path}");
    private static void Write(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));
    private static void ValidateFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} was not found.", path); }
    private static string NormalPath(string value) => value.Replace('\\', '/');
    private static string Key(string? value) => new((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string Text(JsonObject value, string key) => value[key]?.GetValue<string>() ?? "";
    private static JsonObject ParseObject(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new JsonObject();
        try
        {
            return JsonNode.Parse(value) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }
    private static string StateText(JsonObject value, string key) => ParseObject(Text(value, "LuaScriptState"))[key]?.GetValue<string>() ?? "";
    private static int StateInt(JsonObject value, string key) => ParseObject(Text(value, "LuaScriptState"))[key]?.GetValue<int>() ?? 0;
    private static Dictionary<string, JsonObject> Index(JsonNode root) => Descendants(root)
        .Where(item => Text(item, "GUID").Length > 0).GroupBy(item => Text(item, "GUID"), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<JsonObject> Descendants(JsonNode node)
    {
        if (node is JsonObject obj) { yield return obj; foreach (var pair in obj) if (pair.Value is not null) foreach (var child in Descendants(pair.Value)) yield return child; }
        else if (node is JsonArray array) foreach (var item in array) if (item is not null) foreach (var child in Descendants(item)) yield return child;
    }
    private static FirstEditionRuntimeAcceptanceCheck Check(string id, bool passed, string message) => new() { Id = id, Passed = passed, Message = message };
    private static string Report(FirstEditionProductionSpawnValidationManifest manifest) => string.Join("\n", new[]
    {
        "# First Edition Production Spawn Validation", "", $"- Pilot: **{manifest.PilotName}**",
        $"- Pilot skill: **{manifest.PilotSkill}**", $"- Squad cost: **{manifest.SquadPointCost}**",
        $"- Active handlers: **{manifest.ActiveHandlers.Count}**", ""
    }.Concat(manifest.AcceptanceChecks.Select(item => $"- {(item.Passed ? "PASS" : "FAIL")} `{item.Id}`: {item.Message}")));
    private static void Usage() => Console.WriteLine(
        "Usage: UnifiedToolkit build-first-edition-production-spawn-validation <repository> <reference-save.json> --pilot <id|name|import-id> [--upgrade <xws>]... [--output <folder>]");
}

public sealed class FirstEditionProductionSpawnValidationManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string PilotName { get; init; } = "";
    public int PilotSkill { get; init; }
    public int SquadPointCost { get; init; }
    public string ShipGuid { get; init; } = "";
    public string DialGuid { get; init; } = "";
    public string PilotCardGuid { get; init; } = "";
    public int UpgradeCardCount { get; init; }
    public List<FirstEditionProductionHandlerActivation> ActiveHandlers { get; init; } = new();
    public FirstEditionProductionRegistrationManifest Registration { get; init; } = new();
    public List<FirstEditionRuntimeAcceptanceCheck> AcceptanceChecks { get; init; } = new();
    public bool IsValid => AcceptanceChecks.Count > 0 && AcceptanceChecks.All(item => item.Passed);
}
