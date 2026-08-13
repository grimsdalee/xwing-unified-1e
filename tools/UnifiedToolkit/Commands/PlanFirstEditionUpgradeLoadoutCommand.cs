using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedToolkit.Runtime.Loadouts;

namespace UnifiedToolkit.Commands;

public static class PlanFirstEditionUpgradeLoadoutCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(Option(args, "--pilot")))
        {
            ShowUsage();
            return 1;
        }

        try
        {
            var repository = Path.GetFullPath(args[0]);
            var request = new FirstEditionLoadoutRequest
            {
                Pilot = Option(args, "--pilot")!,
                Ship = Option(args, "--ship"),
                Faction = Option(args, "--faction"),
                Upgrades = Options(args, "--upgrade").Concat(Options(args, "--upgrades")).ToList()
            };
            var plan = new FirstEditionLoadoutPlanner().Plan(repository, request);
            var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(repository,
                "_unifiedtoolkit_reports", "phase16", "loadout-plans"));
            Directory.CreateDirectory(output);
            var fileStem = Slug(string.IsNullOrWhiteSpace(plan.Pilot.ImportId) ? request.Pilot : plan.Pilot.ImportId);
            var jsonPath = Path.Combine(output, fileStem + ".json");
            var reportPath = Path.Combine(output, fileStem + ".txt");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(plan, JsonOptions), new UTF8Encoding(false));
            File.WriteAllLines(reportPath, Report(plan), new UTF8Encoding(false));

            Console.WriteLine("UnifiedToolkit First Edition Upgrade Loadout Plan");
            Console.WriteLine("=================================================");
            Console.WriteLine();
            Console.WriteLine($"Pilot:              {plan.Pilot.Name}");
            Console.WriteLine($"Ship:               {plan.Ship.Name}");
            Console.WriteLine($"Faction:            {plan.Pilot.Faction}");
            Console.WriteLine($"Base size:          {plan.Ship.Size}");
            Console.WriteLine($"Printed slots:      {plan.Pilot.PrintedUpgradeSlots.Count}");
            Console.WriteLine($"Total slot objects: {plan.Slots.Count}");
            Console.WriteLine($"Upgrades requested: {request.Upgrades.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries)).Count()}");
            Console.WriteLine($"Upgrades assigned:  {plan.Assignments.Count(assignment => assignment.IsAssigned)}");
            Console.WriteLine($"Pilot cost:         {plan.PilotCost}");
            Console.WriteLine($"Upgrade cost:       {plan.UpgradeCost}");
            Console.WriteLine($"Total cost:         {plan.TotalCost}");
            Console.WriteLine($"Errors:             {plan.Issues.Count(issue => issue.Severity == FirstEditionLoadoutIssueSeverity.Error)}");
            Console.WriteLine($"Warnings:           {plan.Issues.Count(issue => issue.Severity == FirstEditionLoadoutIssueSeverity.Warning)}");
            Console.WriteLine($"Valid:              {plan.IsValid}");
            Console.WriteLine();
            Console.WriteLine($"Plan:   {jsonPath}");
            Console.WriteLine($"Report: {reportPath}");
            Console.WriteLine();
            Console.WriteLine("Planning completed. No TTS save, Lua script, asset or gameplay state was modified.");
            return plan.IsValid ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"First Edition upgrade loadout planning failed: {exception.Message}");
            return 1;
        }
    }

    private static IEnumerable<string> Report(FirstEditionLoadoutPlan plan)
    {
        yield return "FIRST EDITION UPGRADE LOADOUT PLAN";
        yield return "==================================";
        yield return $"Pilot: {plan.Pilot.Name} ({plan.Pilot.Id})";
        yield return $"Ship: {plan.Ship.Name} ({plan.Ship.Id})";
        yield return $"Faction: {plan.Pilot.Faction}";
        yield return $"Base size: {plan.Ship.Size}";
        yield return $"Cost: {plan.PilotCost} + {plan.UpgradeCost} = {plan.TotalCost}";
        yield return $"Valid: {plan.IsValid}";
        yield return "";
        yield return "SLOTS";
        foreach (var slot in plan.Slots)
            yield return $"{slot.SlotId} [{slot.Source}] -> {slot.AssignedUpgradeXws ?? "unoccupied"}";
        yield return "";
        yield return "ASSIGNMENTS";
        foreach (var assignment in plan.Assignments)
            yield return $"{assignment.RequestIndex}. {assignment.Name} ({assignment.Xws}), {assignment.Points} points -> {assignment.AssignedSlotId ?? "not assigned"}";
        yield return "";
        yield return "ISSUES";
        if (plan.Issues.Count == 0) yield return "None";
        foreach (var issue in plan.Issues)
            yield return $"{issue.Severity.ToString().ToUpperInvariant()} {issue.Code} [{issue.Subject}]: {issue.Message}";
    }

    private static string? Option(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(index => args[index + 1]).FirstOrDefault();
    private static IEnumerable<string> Options(string[] args, string name) => Enumerable.Range(0, Math.Max(0, args.Length - 1))
        .Where(index => args[index].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(index => args[index + 1]);
    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant().Split(
        new[] { ' ', ':', '/', '\\', '"', '\'', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries));
    private static void ShowUsage() => Console.WriteLine("Usage: UnifiedToolkit plan-first-edition-upgrade-loadout <repository> --pilot <id|name|import-id> [--ship <id>] [--faction <id>] [--upgrade <xws>]... [--upgrades <xws,xws>] [--output <folder>]");
}
