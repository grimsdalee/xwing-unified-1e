using UnifiedToolkit.Commands;

if (args.Length == 0)
{
    ShowHelp();
    return 1;
}

var command = args[0].ToLowerInvariant();
var commandArgs = args.Skip(1).ToArray();

return command switch
{
    "extract" => ExtractCommand.Run(commandArgs),
    "analyse" => AnalyseCommand.Run(commandArgs),
    "repo" => RepoCommand.Run(commandArgs),
    "search" => SearchCommand.Run(commandArgs),
    "ships" => ShipsCommand.Run(commandArgs),
    "pilots" => PilotsCommand.Run(commandArgs),
    "upgrades" => UpgradesCommand.Run(commandArgs),
    "repository" => RepositoryCommand.Run(commandArgs),
    "restrictions" => RestrictionsCommand.Run(commandArgs),
    "schema" => SchemaCommand.Run(commandArgs),
    "convert" => ConvertCommand.Run(commandArgs),
    "inspect-mapping" => InspectMappingCommand.Run(commandArgs),
    "prepare-ship-mappings" => PrepareShipMappingsCommand.Run(commandArgs),
    "import-first-edition-ships" => ImportFirstEditionShipsCommand.Run(commandArgs),
    "approve-ship-mappings" => ApproveShipMappingsCommand.Run(commandArgs),
    _ => UnknownCommand(command)
};

static void ShowHelp()
{
    Console.WriteLine("UnifiedToolkit");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  extract <tts-json-file> [output-folder]");
    Console.WriteLine("  analyse <tts-json-file>");
    Console.WriteLine("  repo <repo-folder>");
    Console.WriteLine("  search <tts-json-file> <repo-folder> <text>");
    Console.WriteLine("  ships <repo-folder>");
    Console.WriteLine("  upgrades <repo-folder>");
    Console.WriteLine("  repository <repo-folder>");
    Console.WriteLine("  restrictions <repo-folder>");
    Console.WriteLine("  pilots <repo-folder>");
    Console.WriteLine("  schema <pilots|ships|upgrades> <repo-folder>");
    Console.WriteLine("  convert <repo-folder> [mapping-folder] [--allow-source-errors]");
    Console.WriteLine("  inspect-mapping <repo-folder> <source-ship-id> [mapping-folder]");
    Console.WriteLine("  prepare-ship-mappings <repo-folder> [mapping-folder]");
    Console.WriteLine("  import-first-edition-ships <repo-folder> <xwing-data-folder> [mapping-folder]");
    Console.WriteLine("  approve-ship-mappings <ships.proposed.json> [mapping-folder] [--version <version>] [--apply]");
}

static int UnknownCommand(string command)
{
    Console.WriteLine($"Unknown command: {command}");
    ShowHelp();
    return 1;
}
