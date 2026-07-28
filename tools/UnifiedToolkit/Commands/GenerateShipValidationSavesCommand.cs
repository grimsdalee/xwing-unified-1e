using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Generates one Tabletop Simulator validation save per faction/ship type.
/// Every ready First Edition pilot package for that ship is placed into the
/// same save for systematic visual inspection.
/// </summary>
public static class GenerateShipValidationSavesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] RequiredRoles =
    {
        "ShipModel",
        "ShipTexture",
        "DialTexture",
        "PilotCard",
        "PilotBaseToken"
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
            var referenceSave = Path.GetFullPath(args[1]);
            ValidateFile(referenceSave, "Reference TTS save");

            var packagePlanPath = ResolvePath(
                repositoryRoot,
                args,
                "--package-plan",
                "_unifiedtoolkit_reports/phase11/ship-package-planning/ship-package-plans.json");
            var runtimePayloadPath = ResolvePath(
                repositoryRoot,
                args,
                "--runtime-payloads",
                "_unifiedtoolkit_reports/phase11f/standard-runtime-payloads/standard-first-edition-runtime-payloads.json");
            var runtimeTemplatesPath = ResolvePath(
                repositoryRoot,
                args,
                "--runtime-templates",
                "_unifiedtoolkit_reports/phase12b/runtime-template-extraction/runtime-templates.json");
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);
            var assetBaseUrl = ReadOption(args, "--asset-base-url")
                ?? "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main/";

            ValidateFile(packagePlanPath, "Phase 11 ship-package plan");
            ValidateFile(runtimePayloadPath, "Phase 11F runtime payloads");
            ValidateFile(runtimeTemplatesPath, "Phase 12B runtime templates");

            var packagePlan = Read<ValidationPackagePlan>(packagePlanPath);
            var runtimePayloads = Read<PrototypeRuntimePayloadInput>(runtimePayloadPath);
            var runtimeIndex = runtimePayloads.Payloads
                .GroupBy(item => Normalise(item.ShipId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var readyPackages = packagePlan.Packages
                .Where(package => package.PackageStatus.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                .Where(package => package.BaseSize.Equals("small", StringComparison.OrdinalIgnoreCase)
                    || package.BaseSize.Equals("large", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var groups = readyPackages
                .GroupBy(
                    package => new ValidationShipKey(
                        Normalise(package.Faction),
                        Normalise(package.ShipId)),
                    ValidationShipKeyComparer.Instance)
                .OrderBy(group => group.Key.Faction)
                .ThenBy(group => group.First().ShipName)
                .ToList();

            var plansFolder = Path.Combine(outputFolder, "plans");
            var savesFolder = Path.Combine(outputFolder, "saves");
            Directory.CreateDirectory(plansFolder);
            Directory.CreateDirectory(savesFolder);

            var results = new List<ShipValidationSaveResult>();

            foreach (var group in groups)
            {
                var sample = group.First();
                var runtimeKey = Normalise(sample.ShipId);
                runtimeIndex.TryGetValue(runtimeKey, out var runtime);

                var assemblies = BuildAssemblies(
                    repositoryRoot,
                    group.OrderBy(package => package.PilotSkill)
                        .ThenBy(package => package.PilotName)
                        .ToList(),
                    runtime);

                var errors = assemblies
                    .SelectMany(assembly => assembly.ValidationErrors)
                    .ToList();

                var plan = new FiveShipPrototypeAssemblyDocument
                {
                    SchemaVersion = "1.0.0",
                    GeneratedUtc = DateTimeOffset.UtcNow,
                    RepositoryRoot = NormalisePath(repositoryRoot),
                    PackagePlanPath = NormalisePath(packagePlanPath),
                    RuntimePayloadPath = NormalisePath(runtimePayloadPath),
                    DialRuntimePath = string.Empty,
                    RequestedPrototypeCount = assemblies.Count,
                    ReadyPrototypeCount = assemblies.Count(item => item.Status == "Ready"),
                    InvalidPrototypeCount = assemblies.Count(item => item.Status != "Ready"),
                    DialRuntimeValidated = true,
                    ValidationErrors = errors,
                    Assemblies = assemblies
                };

                var stem = $"{SafeFileName(group.Key.Faction)}__{SafeFileName(sample.ShipId)}";
                var planPath = Path.Combine(plansFolder, stem + "__assembly-plan.json");
                var savePath = Path.Combine(savesFolder, stem + "__all-pilots.json");

                File.WriteAllText(
                    planPath,
                    JsonSerializer.Serialize(plan, JsonOptions),
                    new UTF8Encoding(false));

                var status = "Skipped";
                var exitCode = 2;
                if (plan.InvalidPrototypeCount == 0 && plan.ValidationErrors.Count == 0)
                {
                    exitCode = GeneratePrototypeSaveCommand.Run(new[]
                    {
                        repositoryRoot,
                        referenceSave,
                        "--assembly-plan", planPath,
                        "--runtime-templates", runtimeTemplatesPath,
                        "--asset-base-url", assetBaseUrl,
                        "--output", savePath
                    });
                    status = exitCode is 0 or 2 ? "Generated" : "Failed";
                }

                results.Add(new ShipValidationSaveResult
                {
                    Faction = sample.Faction,
                    ShipId = sample.ShipId,
                    ShipName = sample.ShipName,
                    PilotCount = assemblies.Count,
                    ReadyCount = plan.ReadyPrototypeCount,
                    InvalidCount = plan.InvalidPrototypeCount,
                    Status = status,
                    GeneratorExitCode = exitCode,
                    PlanPath = NormalisePath(planPath),
                    SavePath = NormalisePath(savePath),
                    Errors = errors
                });
            }

            var manifest = new ShipValidationSaveManifest
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                ReferenceSave = NormalisePath(referenceSave),
                AssetBaseUrl = assetBaseUrl,
                ShipGroups = results.Count,
                SavesGenerated = results.Count(result => result.Status == "Generated"),
                SavesFailed = results.Count(result => result.Status == "Failed"),
                SavesSkipped = results.Count(result => result.Status == "Skipped"),
                Results = results
            };

            var manifestPath = Path.Combine(outputFolder, "ship-validation-saves.json");
            var csvPath = Path.Combine(outputFolder, "ship-validation-saves.csv");
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));
            WriteCsv(csvPath, results);

            Console.WriteLine("UnifiedToolkit Ship Validation Save Generation");
            Console.WriteLine("==============================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:          {repositoryRoot}");
            Console.WriteLine($"Reference save:      {referenceSave}");
            Console.WriteLine($"Asset base URL:      {assetBaseUrl}");
            Console.WriteLine();
            Console.WriteLine($"Ship groups:         {manifest.ShipGroups}");
            Console.WriteLine($"Saves generated:     {manifest.SavesGenerated}");
            Console.WriteLine($"Saves failed:        {manifest.SavesFailed}");
            Console.WriteLine($"Saves skipped:       {manifest.SavesSkipped}");
            Console.WriteLine();
            Console.WriteLine($"Saves folder:        {savesFolder}");
            Console.WriteLine($"Manifest:            {manifestPath}");
            Console.WriteLine($"CSV:                 {csvPath}");

            return manifest.SavesFailed == 0 && manifest.SavesSkipped == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ship validation save generation failed: {ex.Message}");
            return 1;
        }
    }

    private static List<FiveShipPrototypeAssembly> BuildAssemblies(
        string repositoryRoot,
        IReadOnlyList<ValidationPackage> packages,
        PrototypeRuntimeShipInput? runtime)
    {
        const int columns = 5;
        const float spacingX = 8.0f;
        const float spacingZ = 10.0f;

        var rows = (int)Math.Ceiling(packages.Count / (double)columns);
        var startX = -((Math.Min(columns, packages.Count) - 1) * spacingX) / 2.0f;
        var startZ = -((rows - 1) * spacingZ) / 2.0f;
        var assemblies = new List<FiveShipPrototypeAssembly>();

        for (var index = 0; index < packages.Count; index++)
        {
            var package = packages[index];
            var errors = new List<string>();
            var assets = new List<FiveShipPrototypeAsset>();

            foreach (var role in RequiredRoles)
            {
                var requirement = package.Requirements.FirstOrDefault(item =>
                    item.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
                var chosen = ChooseAsset(repositoryRoot, role, requirement);

                if (chosen is null)
                {
                    errors.Add($"{package.PilotName}: no usable asset for role {role}.");
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(
                    repositoryRoot,
                    chosen.RepositoryPath.Replace('/', Path.DirectorySeparatorChar)));

                if (!File.Exists(fullPath))
                    errors.Add($"{package.PilotName}: missing {role}: {chosen.RepositoryPath}");

                assets.Add(new FiveShipPrototypeAsset
                {
                    Role = role,
                    AssetId = chosen.AssetId,
                    RepositoryPath = NormalisePath(chosen.RepositoryPath),
                    FullPath = NormalisePath(fullPath),
                    Exists = File.Exists(fullPath),
                    ResolutionSource = requirement?.ResolutionSource ?? string.Empty,
                    ResolutionMethod = role is "ShipModel" or "ShipTexture"
                        ? "Validation ships-v2 repository preference"
                        : requirement?.ResolutionMethod ?? string.Empty
                });
            }

            if (runtime is null)
                errors.Add($"{package.PilotName}: no runtime payload for ship {package.ShipId}.");

            var baseSize = package.BaseSize.ToLowerInvariant();
            var shipId = Normalise(package.ShipId);
            var baseKey = baseSize == "large"
                ? "FirstEditionLargeShipBase"
                : "FirstEditionSmallShipBase";
            var pegKey = shipId is "asf01bwing" or "bwing"
                ? "FirstEditionBwingShipPeg"
                : baseSize == "large"
                    ? "FirstEditionLargeShipPeg"
                    : "FirstEditionSmallShipPeg";

            assemblies.Add(new FiveShipPrototypeAssembly
            {
                RequestedShipId = package.ShipId,
                PackageId = package.PackageId,
                ShipId = package.ShipId,
                ShipName = package.ShipName,
                PilotId = package.PilotId,
                PilotName = package.PilotName,
                Faction = package.Faction,
                BaseSize = baseSize,
                BaseTemplateKey = baseKey,
                PegTemplateKey = pegKey,
                PositionX = startX + (index % columns) * spacingX,
                PositionZ = startZ + (index / columns) * spacingZ,
                PackageStatus = package.PackageStatus,
                RuntimeType = runtime?.RuntimeType ?? string.Empty,
                Status = errors.Count == 0 ? "Ready" : "Invalid",
                MoveSet = runtime?.MoveSet ?? new List<string>(),
                ActSet = runtime?.ActSet ?? new List<string>(),
                FirstEditionActions = runtime?.FirstEditionActions ?? new List<string>(),
                RuntimeControls = runtime?.RuntimeControls ?? new PrototypeRuntimeControlsInput(),
                Assets = assets,
                ValidationErrors = errors
            });
        }

        return assemblies;
    }

    private static ValidationAsset? ChooseAsset(
        string repositoryRoot,
        string role,
        ValidationRequirement? requirement)
    {
        if (requirement is null)
            return null;

        var candidates = new List<ValidationAsset>();
        if (requirement.SelectedAsset is not null)
            candidates.Add(requirement.SelectedAsset);
        candidates.AddRange(requirement.Candidates);

        if (role is "ShipModel" or "ShipTexture")
        {
            const string requiredRoot =
                "assets/source/unified25/assets/ships-v2/";

            return candidates
                .Where(asset => asset.RepositoryPath.Replace('\\', '/')
                    .StartsWith(requiredRoot, StringComparison.OrdinalIgnoreCase))
                .Where(asset => !asset.RepositoryPath.Contains(
                    "PrototypeShipTexture",
                    StringComparison.OrdinalIgnoreCase))
                .Where(asset => File.Exists(Path.Combine(
                    repositoryRoot,
                    asset.RepositoryPath.Replace('/', Path.DirectorySeparatorChar))))
                .OrderByDescending(asset => asset.ResolverScore)
                .ThenByDescending(asset => asset.Score)
                .ThenBy(asset => asset.RepositoryPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        return candidates.FirstOrDefault(asset => File.Exists(Path.Combine(
            repositoryRoot,
            asset.RepositoryPath.Replace('/', Path.DirectorySeparatorChar))))
            ?? candidates.FirstOrDefault();
    }

    private static void WriteCsv(string path, IEnumerable<ShipValidationSaveResult> results)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("Faction,ShipId,ShipName,PilotCount,ReadyCount,InvalidCount,Status,SavePath,PlanPath,Errors");
        foreach (var result in results)
        {
            writer.WriteLine(string.Join(',',
                Csv(result.Faction),
                Csv(result.ShipId),
                Csv(result.ShipName),
                result.PilotCount,
                result.ReadyCount,
                result.InvalidCount,
                Csv(result.Status),
                Csv(result.SavePath),
                Csv(result.PlanPath),
                Csv(string.Join(" | ", result.Errors))));
        }
    }

    private static string Csv(string value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not parse JSON file: {path}");

    private static string ResolvePath(
        string repositoryRoot,
        string[] args,
        string option,
        string defaultRelativePath)
    {
        var explicitPath = ReadOption(args, option);
        return string.IsNullOrWhiteSpace(explicitPath)
            ? Path.GetFullPath(Path.Combine(
                repositoryRoot,
                defaultRelativePath.Replace('/', Path.DirectorySeparatorChar)))
            : Path.GetFullPath(explicitPath);
    }

    private static string ResolveOutputFolder(string repositoryRoot, string[] args)
    {
        var explicitPath = ReadOption(args, "--output");
        return string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(repositoryRoot, "assets", "generated", "validation")
            : Path.GetFullPath(explicitPath);
    }

    private static string? ReadOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }
        return null;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string((value ?? string.Empty)
            .ToLowerInvariant()
            .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character)
                ? '-'
                : character)
            .ToArray());
        return result.Trim('-');
    }

    private static string Normalise(string value) =>
        new((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string NormalisePath(string path) => path.Replace('\\', '/');

    private static void ValidateFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} was not found.", path);
    }

    private static void ShowUsage()
    {
        Console.WriteLine(
            "Usage: UnifiedToolkit generate-ship-validation-saves " +
            "<first-edition-repo-folder> <reference-save.json> " +
            "[--package-plan <file>] [--runtime-payloads <file>] " +
            "[--runtime-templates <file>] [--asset-base-url <url>] " +
            "[--output <folder>]");
    }

    private sealed record ValidationShipKey(string Faction, string ShipId);

    private sealed class ValidationShipKeyComparer : IEqualityComparer<ValidationShipKey>
    {
        public static ValidationShipKeyComparer Instance { get; } = new();
        public bool Equals(ValidationShipKey? x, ValidationShipKey? y) =>
            x is not null && y is not null
            && x.Faction.Equals(y.Faction, StringComparison.OrdinalIgnoreCase)
            && x.ShipId.Equals(y.ShipId, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(ValidationShipKey obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Faction),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ShipId));
    }
}

public sealed class ValidationPackagePlan
{
    public List<ValidationPackage> Packages { get; init; } = new();
}

public sealed class ValidationPackage
{
    public string PackageId { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string PilotId { get; init; } = string.Empty;
    public string PilotName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string BaseSize { get; init; } = string.Empty;
    public int PilotSkill { get; init; }
    public string PackageStatus { get; init; } = string.Empty;
    public List<ValidationRequirement> Requirements { get; init; } = new();
}

public sealed class ValidationRequirement
{
    public string Role { get; init; } = string.Empty;
    public string ResolutionSource { get; init; } = string.Empty;
    public string ResolutionMethod { get; init; } = string.Empty;
    public ValidationAsset? SelectedAsset { get; init; }
    public List<ValidationAsset> Candidates { get; init; } = new();
}

public sealed class ValidationAsset
{
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public int Score { get; init; }
    public int ResolverScore { get; init; }
}

public sealed class ShipValidationSaveManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string ReferenceSave { get; init; } = string.Empty;
    public string AssetBaseUrl { get; init; } = string.Empty;
    public int ShipGroups { get; init; }
    public int SavesGenerated { get; init; }
    public int SavesFailed { get; init; }
    public int SavesSkipped { get; init; }
    public List<ShipValidationSaveResult> Results { get; init; } = new();
}

public sealed class ShipValidationSaveResult
{
    public string Faction { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public int PilotCount { get; init; }
    public int ReadyCount { get; init; }
    public int InvalidCount { get; init; }
    public string Status { get; init; } = string.Empty;
    public int GeneratorExitCode { get; init; }
    public string PlanPath { get; init; } = string.Empty;
    public string SavePath { get; init; } = string.Empty;
    public List<string> Errors { get; init; } = new();
}
