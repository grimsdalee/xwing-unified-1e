using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class VerifyFirstEditionLoadoutContractCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1) { ShowUsage(); return 1; }
        try
        {
            var repository = Path.GetFullPath(args[0]);
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "loadout-contract"));
            var result = new FirstEditionLoadoutPlanner().Verify(repository);
            Directory.CreateDirectory(output);
            var path = Path.Combine(output, "first-edition-loadout-contract-verification.json");
            File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Loadout Contract Verification");
            Console.WriteLine("==========================================================");
            Console.WriteLine();
            Console.WriteLine($"Pilots:                {result.PilotCount}");
            Console.WriteLine($"Ships:                 {result.ShipCount}");
            Console.WriteLine($"Upgrade cards:         {result.UpgradeCount}");
            Console.WriteLine($"Mechanics entries:     {result.MechanicsUpgradeCount}");
            Console.WriteLine($"Condition assignments: {result.ConditionAssignmentCount}");
            Console.WriteLine($"Printed slot instances:{result.PrintedSlotCount,5}");
            Console.WriteLine($"Printed slot types:    {result.DistinctSlotTypeCount}");
            Console.WriteLine($"Acceptance scenarios:  {result.AcceptanceScenarioCount}");
            Console.WriteLine($"Scenario failures:     {result.AcceptanceScenarioFailureCount}");
            Console.WriteLine($"Errors:                {result.Issues.Count(issue => issue.Severity == FirstEditionLoadoutIssueSeverity.Error)}");
            Console.WriteLine($"Warnings:              {result.Issues.Count(issue => issue.Severity == FirstEditionLoadoutIssueSeverity.Warning)}");
            Console.WriteLine($"Valid:                 {result.IsValid}");
            Console.WriteLine();
            Console.WriteLine($"Verification: {path}");
            Console.WriteLine();
            Console.WriteLine("Verification completed. No source files or gameplay state were modified.");
            return result.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition loadout contract verification failed: {exception.Message}");
            return 1;
        }
    }

    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(index => args[index + 1]).FirstOrDefault();
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit verify-first-edition-loadout-contract <repository> [--output <folder>]");
}
