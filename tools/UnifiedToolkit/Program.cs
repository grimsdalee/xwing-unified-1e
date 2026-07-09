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
}

static int UnknownCommand(string command)
{
    Console.WriteLine($"Unknown command: {command}");
    ShowHelp();
    return 1;
}