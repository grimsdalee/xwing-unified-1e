using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedToolkit.Commands;

/// <summary>
/// Phase 12B-1:
/// Builds and validates the deterministic six-ship prototype assembly plan.
///
/// This stage deliberately stops before TTS serialization. It proves that the
/// selected package, runtime payload, First Edition dial runtime and every
/// required repository asset can be joined without guessing.
/// </summary>
public static class PrepareFiveShipPrototypeAssemblyCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly PrototypeSelection[] RequestedPrototypes =
    {
        new("t65xwing", new[] { "xwing", "t65xwing" }, new[] { "lukeskywalker" }, -15f, -5f),
        new("kwing", new[] { "kwing" }, new[] { "wardensquadronpilot", "mirandadoni" }, -9f, -5f),
        new("asf01bwing", new[] { "asf01bwing", "bwing" }, new[] { "bladesquadronveteran", "bluesquadronpilot", "tennumb" }, -3f, -5f),
        new("tiereaper", new[] { "tiereaper" }, new[] { "majorvermeil", "scarifbasepilot" }, 3f, -5f),
        new("lancerclasspursuitcraft", new[] { "lancerclasspursuitcraft" }, new[] { "ketsuonyo", "shadowporthunter" }, 9f, -5f),
        new("sheathipedeclassshuttle", new[] { "sheathipedeclassshuttle" }, new[] { "fennrau", "ezrabridger" }, 15f, -5f)
    };

    // These are repository image/model assets selected by the package planner.
    // Base and peg are not per-pilot files: they are reusable TTS runtime object
    // templates selected later by base size.
    private static readonly string[] RequiredAssetRoles =
    {
        "ShipModel",
        "ShipTexture",
        "DialTexture",
        "PilotCard",
        "PilotBaseToken"
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repositoryRoot = Path.GetFullPath(args[0]);
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
            var dialRuntimePath = ResolvePath(
                repositoryRoot,
                args,
                "--dial-runtime",
                "_unifiedtoolkit_reports/phase12a/dial-runtime-integration/first-edition-dial-runtime.json");
            var outputFolder = ResolveOutputFolder(repositoryRoot, args);

            ValidateFile(packagePlanPath, "Phase 11 ship-package plan");
            ValidateFile(runtimePayloadPath, "Phase 11F runtime payloads");
            ValidateFile(dialRuntimePath, "Phase 12A generated dial runtime manifest");

            var pegCataloguePath = ResolvePath(
                repositoryRoot,
                args,
                "--peg-catalogue",
                "_unifiedtoolkit_reports/phase12b/ship-peg-catalogue/ship-peg-assets.json");
            ValidateFile(pegCataloguePath, "Phase 12B ship-peg catalogue");

            var pegCatalogue = Read<ShipPegCatalogueInput>(pegCataloguePath);
            var requiredPegKeys = new[]
            {
                "FirstEditionSmallShipPeg",
                "FirstEditionBwingShipPeg",
                "FirstEditionLargeShipPeg"
            };
            var availablePegKeys = pegCatalogue.Pegs
                .Where(peg => peg.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase))
                .Select(peg => peg.TemplateKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingPegKeys = requiredPegKeys
                .Where(key => !availablePegKeys.Contains(key))
                .ToList();

            var packagePlan = Read<PrototypePackagePlanInput>(packagePlanPath);
            var runtimePayloads = Read<PrototypeRuntimePayloadInput>(runtimePayloadPath);
            var dialRuntime = Read<PrototypeDialRuntimeInput>(dialRuntimePath);

            var packageCandidates = packagePlan.Packages
                .Where(package => package.PackageStatus.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var runtimeIndex = runtimePayloads.Payloads
                .GroupBy(payload => Normalise(payload.ShipId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var assemblies = new List<FiveShipPrototypeAssembly>();

            foreach (var request in RequestedPrototypes)
            {
                assemblies.Add(BuildAssembly(
                    repositoryRoot,
                    request,
                    packageCandidates,
                    runtimeIndex,
                    dialRuntime));
            }

            var errors = assemblies
                .SelectMany(assembly => assembly.ValidationErrors.Select(error => $"{assembly.RequestedShipId}: {error}"))
                .ToList();
            errors.AddRange(
                missingPegKeys.Select(key =>
                    $"Peg catalogue: required template '{key}' is not resolved."));
            var warnings = assemblies
                .SelectMany(assembly => assembly.ValidationWarnings.Select(warning => $"{assembly.RequestedShipId}: {warning}"))
                .ToList();

            Directory.CreateDirectory(outputFolder);

            var document = new FiveShipPrototypeAssemblyDocument
            {
                SchemaVersion = "1.0.0",
                GeneratedUtc = DateTimeOffset.UtcNow,
                RepositoryRoot = NormalisePath(repositoryRoot),
                PackagePlanPath = NormalisePath(packagePlanPath),
                RuntimePayloadPath = NormalisePath(runtimePayloadPath),
                DialRuntimePath = NormalisePath(dialRuntimePath),
                RequestedPrototypeCount = RequestedPrototypes.Length,
                ReadyPrototypeCount = assemblies.Count(assembly => assembly.Status == "Ready"),
                InvalidPrototypeCount = assemblies.Count(assembly => assembly.Status != "Ready"),
                DialRuntimeValidated = dialRuntime.ValidationErrors.Count == 0,
                ValidationErrors = errors,
                ValidationWarnings = warnings,
                Assemblies = assemblies
            };

            var jsonPath = Path.Combine(outputFolder, "five-ship-prototype-assembly-plan.json");
            var csvPath = Path.Combine(outputFolder, "five-ship-prototype-assembly-assets.csv");
            var reportPath = Path.Combine(outputFolder, "FIVE-SHIP-PROTOTYPE-ASSEMBLY-PLAN.md");
            var checklistPath = Path.Combine(outputFolder, "FIVE-SHIP-PROTOTYPE-TTS-CHECKLIST.md");

            File.WriteAllText(jsonPath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
            WriteCsv(csvPath, assemblies);
            WriteMarkdown(reportPath, document);
            WriteChecklist(checklistPath, assemblies);

            Console.WriteLine("UnifiedToolkit Phase 12B-1 Six-Ship Prototype Assembly Plan");
            Console.WriteLine("=============================================================");
            Console.WriteLine();
            Console.WriteLine($"Repository:               {repositoryRoot}");
            Console.WriteLine($"Package plan:             {packagePlanPath}");
            Console.WriteLine($"Runtime payloads:         {runtimePayloadPath}");
            Console.WriteLine($"Dial runtime:             {dialRuntimePath}");
            Console.WriteLine();
            Console.WriteLine($"Requested prototypes:     {document.RequestedPrototypeCount}");
            Console.WriteLine($"Ready assemblies:         {document.ReadyPrototypeCount}");
            Console.WriteLine($"Invalid assemblies:       {document.InvalidPrototypeCount}");
            Console.WriteLine($"Dial runtime validated:   {document.DialRuntimeValidated}");
            Console.WriteLine($"Validation errors:        {document.ValidationErrors.Count}");
            Console.WriteLine($"Validation warnings:      {document.ValidationWarnings.Count}");
            Console.WriteLine();

            foreach (var assembly in assemblies)
            {
                Console.WriteLine(
                    $"  {assembly.ShipName,-32} {assembly.PilotName,-25} " +
                    $"{assembly.BaseSize,-5} {assembly.Status}");
            }

            Console.WriteLine();
            Console.WriteLine($"Plan:                     {jsonPath}");
            Console.WriteLine($"Asset CSV:                {csvPath}");
            Console.WriteLine($"Report:                   {reportPath}");
            Console.WriteLine($"TTS checklist:             {checklistPath}");
            Console.WriteLine();
            Console.WriteLine(
                "Prototype assembly planned and validated. No TTS save or object was created.");

            return document.InvalidPrototypeCount == 0
                && document.ValidationErrors.Count == 0
                && document.DialRuntimeValidated
                ? 0
                : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Five-ship prototype assembly preparation failed: {ex.Message}");
            return 1;
        }
    }

    private static FiveShipPrototypeAssembly BuildAssembly(
        string repositoryRoot,
        PrototypeSelection request,
        IReadOnlyList<PrototypePackageInput> packages,
        IReadOnlyDictionary<string, PrototypeRuntimeShipInput> runtimeIndex,
        PrototypeDialRuntimeInput dialRuntime)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var matchingPackages = packages
            .Where(package => request.ShipAliases.Contains(
                Normalise(package.ShipId),
                StringComparer.OrdinalIgnoreCase))
            .ToList();

        var selectedPackage = matchingPackages
            .OrderBy(package => PilotPreference(package, request.PreferredPilotIds))
            .ThenBy(package => package.PilotSkill)
            .FirstOrDefault();

        if (selectedPackage is null)
        {
            errors.Add("No ready package was found for the requested ship.");
            return InvalidAssembly(request, errors, warnings);
        }

        var runtime = request.ShipAliases
            .Select(alias => runtimeIndex.TryGetValue(alias, out var value) ? value : null)
            .FirstOrDefault(value => value is not null);

        if (runtime is null)
            errors.Add("No Phase 11F runtime payload was found.");

        if (selectedPackage.BaseSize.Equals("medium", StringComparison.OrdinalIgnoreCase))
            errors.Add("Medium base is invalid in First Edition.");
        else if (!selectedPackage.BaseSize.Equals("small", StringComparison.OrdinalIgnoreCase)
                 && !selectedPackage.BaseSize.Equals("large", StringComparison.OrdinalIgnoreCase))
            errors.Add($"Unsupported standard First Edition base size '{selectedPackage.BaseSize}'.");

        var resolvedAssets = new List<FiveShipPrototypeAsset>();

        foreach (var role in RequiredAssetRoles)
        {
            var requirement = selectedPackage.Requirements.FirstOrDefault(item =>
                item.Role.Equals(role, StringComparison.OrdinalIgnoreCase));

            if (requirement?.SelectedAsset is null)
            {
                errors.Add($"Required role {role} has no selected asset.");
                continue;
            }

            var repositoryPath = requirement.SelectedAsset.RepositoryPath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, repositoryPath));
            var exists = File.Exists(fullPath);

            if (!exists)
                errors.Add($"Required role {role} points to a missing file: {requirement.SelectedAsset.RepositoryPath}");

            resolvedAssets.Add(new FiveShipPrototypeAsset
            {
                Role = role,
                AssetId = requirement.SelectedAsset.AssetId,
                RepositoryPath = NormalisePath(requirement.SelectedAsset.RepositoryPath),
                FullPath = NormalisePath(fullPath),
                Exists = exists,
                ResolutionSource = requirement.ResolutionSource,
                ResolutionMethod = requirement.ResolutionMethod
            });
        }

        if (runtime is not null)
        {
            if (runtime.MoveSet.Count == 0)
                errors.Add("Runtime payload has no moveSet.");
            if (runtime.UnknownActions.Count > 0)
                errors.Add($"Runtime payload has unknown actions: {string.Join(", ", runtime.UnknownActions)}");
            if (runtime.ValidationIssues.Count > 0)
                errors.AddRange(runtime.ValidationIssues.Select(issue => $"Runtime payload: {issue}"));

            if (!runtime.BaseSize.Equals(selectedPackage.BaseSize, StringComparison.OrdinalIgnoreCase))
                errors.Add(
                    $"Package base size '{selectedPackage.BaseSize}' does not match runtime base size '{runtime.BaseSize}'.");
        }

        if (dialRuntime.ValidationErrors.Count > 0)
            errors.Add("Generated First Edition dial runtime contains validation errors.");

        if (matchingPackages.Count > 1
            && !request.PreferredPilotIds.Contains(
                Normalise(selectedPackage.PilotId),
                StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add(
                $"Preferred pilot was unavailable; selected '{selectedPackage.PilotName}' deterministically.");
        }

        var normalisedBaseSize = selectedPackage.BaseSize.ToLowerInvariant();
        var baseTemplateKey = normalisedBaseSize switch
        {
            "small" => "FirstEditionSmallShipBase",
            "large" => "FirstEditionLargeShipBase",
            _ => string.Empty
        };
        var normalisedShipId = Normalise(selectedPackage.ShipId);
        var pegTemplateKey = normalisedShipId is "asf01bwing" or "bwing"
            ? "FirstEditionBwingShipPeg"
            : normalisedBaseSize switch
            {
                "small" => "FirstEditionSmallShipPeg",
                "large" => "FirstEditionLargeShipPeg",
                _ => string.Empty
            };

        if (baseTemplateKey.Length == 0)
            errors.Add($"No reusable base template is defined for '{selectedPackage.BaseSize}'.");
        if (pegTemplateKey.Length == 0)
            errors.Add($"No reusable peg template is defined for '{selectedPackage.BaseSize}'.");

        return new FiveShipPrototypeAssembly
        {
            RequestedShipId = request.CanonicalShipId,
            PackageId = selectedPackage.PackageId,
            ShipId = selectedPackage.ShipId,
            ShipName = selectedPackage.ShipName,
            PilotId = selectedPackage.PilotId,
            PilotName = selectedPackage.PilotName,
            Faction = selectedPackage.Faction,
            BaseSize = normalisedBaseSize,
            BaseTemplateKey = baseTemplateKey,
            PegTemplateKey = pegTemplateKey,
            PositionX = request.PositionX,
            PositionZ = request.PositionZ,
            PackageStatus = selectedPackage.PackageStatus,
            RuntimeType = runtime?.RuntimeType ?? string.Empty,
            MoveSet = runtime?.MoveSet ?? new List<string>(),
            ActSet = runtime?.ActSet ?? new List<string>(),
            FirstEditionActions = runtime?.FirstEditionActions ?? new List<string>(),
            RuntimeControls = runtime?.RuntimeControls ?? new PrototypeRuntimeControlsInput(),
            DialRuntimeRoot = dialRuntime.GeneratedRoot,
            Assets = resolvedAssets,
            ValidationErrors = errors,
            ValidationWarnings = warnings,
            Status = errors.Count == 0 ? "Ready" : "Invalid"
        };
    }

    private static int PilotPreference(
        PrototypePackageInput package,
        IReadOnlyList<string> preferredIds)
    {
        var id = Normalise(package.PilotId);
        for (var index = 0; index < preferredIds.Count; index++)
        {
            if (id.Equals(preferredIds[index], StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return int.MaxValue;
    }

    private static FiveShipPrototypeAssembly InvalidAssembly(
        PrototypeSelection request,
        List<string> errors,
        List<string> warnings) => new()
    {
        RequestedShipId = request.CanonicalShipId,
        PositionX = request.PositionX,
        PositionZ = request.PositionZ,
        Status = "Invalid",
        ValidationErrors = errors,
        ValidationWarnings = warnings
    };

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
            ? Path.Combine(
                repositoryRoot,
                "_unifiedtoolkit_reports",
                "phase12b",
                "five-ship-prototype-assembly")
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

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not parse JSON file: {path}");

    private static string Normalise(string value) =>
        new((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string NormalisePath(string path) =>
        path.Replace('\\', '/');

    private static void ValidateFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} was not found.", path);
    }

    private static void WriteCsv(
        string path,
        IEnumerable<FiveShipPrototypeAssembly> assemblies)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(
            "ShipId,ShipName,PilotId,PilotName,Faction,BaseSize,BaseTemplateKey,PegTemplateKey," +
            "PositionX,PositionZ,Status,Role,AssetId,RepositoryPath,Exists,MoveSet,ActSet,FirstEditionActions");

        foreach (var assembly in assemblies)
        {
            if (assembly.Assets.Count == 0)
            {
                WriteCsvRow(writer, assembly, null);
                continue;
            }

            foreach (var asset in assembly.Assets)
                WriteCsvRow(writer, assembly, asset);
        }
    }

    private static void WriteCsvRow(
        StreamWriter writer,
        FiveShipPrototypeAssembly assembly,
        FiveShipPrototypeAsset? asset)
    {
        writer.WriteLine(string.Join(',',
            Csv(assembly.ShipId),
            Csv(assembly.ShipName),
            Csv(assembly.PilotId),
            Csv(assembly.PilotName),
            Csv(assembly.Faction),
            Csv(assembly.BaseSize),
            Csv(assembly.BaseTemplateKey),
            Csv(assembly.PegTemplateKey),
            assembly.PositionX.ToString(System.Globalization.CultureInfo.InvariantCulture),
            assembly.PositionZ.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Csv(assembly.Status),
            Csv(asset?.Role ?? string.Empty),
            Csv(asset?.AssetId ?? string.Empty),
            Csv(asset?.RepositoryPath ?? string.Empty),
            asset?.Exists ?? false,
            Csv(string.Join('|', assembly.MoveSet)),
            Csv(string.Join('|', assembly.ActSet)),
            Csv(string.Join('|', assembly.FirstEditionActions))));
    }

    private static void WriteMarkdown(
        string path,
        FiveShipPrototypeAssemblyDocument document)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Phase 12B-1 – Five-Ship Prototype Assembly Plan");
        writer.WriteLine();
        writer.WriteLine($"- Requested: **{document.RequestedPrototypeCount}**");
        writer.WriteLine($"- Ready: **{document.ReadyPrototypeCount}**");
        writer.WriteLine($"- Invalid: **{document.InvalidPrototypeCount}**");
        writer.WriteLine($"- Dial runtime validated: **{document.DialRuntimeValidated}**");
        writer.WriteLine();
        writer.WriteLine("| Ship | Pilot | Faction | Base | Maneuvers | Special 1E actions | Status |");
        writer.WriteLine("|---|---|---|---|---:|---|---|");

        foreach (var assembly in document.Assemblies)
        {
            writer.WriteLine(
                $"| {Md(assembly.ShipName)} | {Md(assembly.PilotName)} | " +
                $"{Md(assembly.Faction)} | {Md(assembly.BaseSize)} | " +
                $"{assembly.MoveSet.Count} | " +
                $"{Md(string.Join(", ", assembly.FirstEditionActions))} | " +
                $"{Md(assembly.Status)} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Validation errors");
        writer.WriteLine();
        if (document.ValidationErrors.Count == 0)
            writer.WriteLine("None.");
        else
            foreach (var error in document.ValidationErrors)
                writer.WriteLine($"- {Md(error)}");

        writer.WriteLine();
        writer.WriteLine(
            "Base and peg are reusable TTS runtime templates selected by base size, " +
            "not per-pilot repository assets. This is the serialization contract for " +
            "the next step; it does not yet create a TTS save.");
    }

    private static void WriteChecklist(
        string path,
        IEnumerable<FiveShipPrototypeAssembly> assemblies)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# Five-Ship Prototype TTS Checklist");
        writer.WriteLine();
        writer.WriteLine("Use this after the Phase 12B serializer creates the save.");
        writer.WriteLine();

        foreach (var assembly in assemblies.Where(item => item.Status == "Ready"))
        {
            writer.WriteLine($"## {assembly.ShipName} — {assembly.PilotName}");
            writer.WriteLine();
            writer.WriteLine($"- Expected base: **{assembly.BaseSize}**");
            writer.WriteLine("- [ ] Correct model and colour variant");
            writer.WriteLine(
                $"- [ ] Correct reusable base template: `{assembly.BaseTemplateKey}`");
            writer.WriteLine(
                $"- [ ] Correct reusable peg template: `{assembly.PegTemplateKey}`");
            writer.WriteLine("- [ ] Correct pilot base token");
            writer.WriteLine("- [ ] Correct pilot card");
            writer.WriteLine("- [ ] Correct First Edition dial top and faction reverse");
            writer.WriteLine("- [ ] Green, white and red manoeuvre icons display correctly");
            writer.WriteLine("- [ ] Dial movement list matches official First Edition data");
            writer.WriteLine("- [ ] Ordinary action controls display correctly");
            foreach (var action in assembly.FirstEditionActions)
                writer.WriteLine($"- [ ] {action} control/metadata present");
            writer.WriteLine();
        }
    }

    private static string Csv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static string Md(string value) =>
        (value ?? string.Empty)
            .Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", " ");

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  prepare-five-ship-prototype-assembly <first-edition-repository> " +
            "[--package-plan <file>] [--runtime-payloads <file>] " +
            "[--dial-runtime <file>] [--output <folder>]");
    }

    private sealed record PrototypeSelection(
        string CanonicalShipId,
        IReadOnlyList<string> ShipAliases,
        IReadOnlyList<string> PreferredPilotIds,
        float PositionX,
        float PositionZ);
}

public sealed class FiveShipPrototypeAssemblyDocument
{
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string RepositoryRoot { get; init; } = string.Empty;
    public string PackagePlanPath { get; init; } = string.Empty;
    public string RuntimePayloadPath { get; init; } = string.Empty;
    public string DialRuntimePath { get; init; } = string.Empty;
    public int RequestedPrototypeCount { get; init; }
    public int ReadyPrototypeCount { get; init; }
    public int InvalidPrototypeCount { get; init; }
    public bool DialRuntimeValidated { get; init; }
    public List<string> ValidationErrors { get; init; } = new();
    public List<string> ValidationWarnings { get; init; } = new();
    public List<FiveShipPrototypeAssembly> Assemblies { get; init; } = new();
}

public sealed class FiveShipPrototypeAssembly
{
    public string RequestedShipId { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string PilotId { get; init; } = string.Empty;
    public string PilotName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string BaseSize { get; init; } = string.Empty;
    public string BaseTemplateKey { get; init; } = string.Empty;
    public string PegTemplateKey { get; init; } = string.Empty;
    public float PositionX { get; init; }
    public float PositionZ { get; init; }
    public string PackageStatus { get; init; } = string.Empty;
    public string RuntimeType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string DialRuntimeRoot { get; init; } = string.Empty;
    public List<string> MoveSet { get; init; } = new();
    public List<string> ActSet { get; init; } = new();
    public List<string> FirstEditionActions { get; init; } = new();
    public PrototypeRuntimeControlsInput RuntimeControls { get; init; } = new();
    public List<FiveShipPrototypeAsset> Assets { get; init; } = new();
    public List<string> ValidationErrors { get; init; } = new();
    public List<string> ValidationWarnings { get; init; } = new();
}

public sealed class FiveShipPrototypeAsset
{
    public string Role { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public string ResolutionSource { get; init; } = string.Empty;
    public string ResolutionMethod { get; init; } = string.Empty;
}

public sealed class PrototypePackagePlanInput
{
    public List<PrototypePackageInput> Packages { get; init; } = new();
}

public sealed class PrototypePackageInput
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
    public List<PrototypePackageRequirementInput> Requirements { get; init; } = new();
}

public sealed class PrototypePackageRequirementInput
{
    public string Role { get; init; } = string.Empty;
    public string ResolutionSource { get; init; } = string.Empty;
    public string ResolutionMethod { get; init; } = string.Empty;
    public PrototypePackageAssetInput? SelectedAsset { get; init; }
}

public sealed class PrototypePackageAssetInput
{
    public string AssetId { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
}

public sealed class PrototypeRuntimePayloadInput
{
    public List<PrototypeRuntimeShipInput> Payloads { get; init; } = new();
}

public sealed class PrototypeRuntimeShipInput
{
    public string ShipId { get; init; } = string.Empty;
    public string BaseSize { get; init; } = string.Empty;
    public string RuntimeType { get; init; } = string.Empty;
    public List<string> MoveSet { get; init; } = new();
    public List<string> ActSet { get; init; } = new();
    public List<string> FirstEditionActions { get; init; } = new();
    public PrototypeRuntimeControlsInput RuntimeControls { get; init; } = new();
    public List<string> UnknownActions { get; init; } = new();
    public List<string> ValidationIssues { get; init; } = new();
}

public sealed class PrototypeRuntimeControlsInput
{
    public string? JamControl { get; init; }
    public string? RotateArcMode { get; init; }
    public List<string> RotateArcAdditionalModes { get; init; } = new();
    public string? CoordinateControl { get; init; }
    public string? ReloadControl { get; init; }
    public string? SlamControl { get; init; }
}

public sealed class PrototypeDialRuntimeInput
{
    public string GeneratedRoot { get; init; } = string.Empty;
    public List<string> ValidationErrors { get; init; } = new();
    public List<string> ValidationWarnings { get; init; } = new();
}


public sealed class ShipPegCatalogueInput
{
    public List<ShipPegCatalogueEntryInput> Pegs { get; init; } = new();
}

public sealed class ShipPegCatalogueEntryInput
{
    public string TemplateKey { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
}
