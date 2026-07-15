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
    "review-unmapped-ships" => ReviewUnmappedShipsCommand.Run(commandArgs),
    "apply-ship-dispositions" => ApplyShipDispositionsCommand.Run(commandArgs),
    "resolve-official-ship-aliases" => ResolveOfficialShipAliasesCommand.Run(commandArgs),
    "promote-official-ship-aliases" => PromoteOfficialShipAliasesCommand.Run(commandArgs),
    "prepare-first-edition-pilots" => PrepareFirstEditionPilotsCommand.Run(commandArgs),
    "approve-first-edition-pilots" => ApproveFirstEditionPilotsCommand.Run(commandArgs),
    "review-ambiguous-pilots" => ReviewAmbiguousPilotsCommand.Run(commandArgs),
    "apply-ambiguous-pilot-resolutions" => ApplyAmbiguousPilotResolutionsCommand.Run(commandArgs),
    "prepare-first-edition-upgrades" => PrepareFirstEditionUpgradesCommand.Run(commandArgs),
    "approve-first-edition-upgrades" => ApproveFirstEditionUpgradesCommand.Run(commandArgs),
    "review-ambiguous-upgrades" => ReviewAmbiguousUpgradesCommand.Run(commandArgs),
    "apply-ambiguous-upgrade-resolutions" => ApplyAmbiguousUpgradeResolutionsCommand.Run(commandArgs),
    "first-edition-repository" => FirstEditionRepositoryCommand.Run(commandArgs),
    "inspect-first-edition" => InspectFirstEditionCommand.Run(commandArgs),
    "build-asset-catalogue" => BuildAssetCatalogueCommand.Run(commandArgs),
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
    Console.WriteLine("  review-unmapped-ships <repo-folder> [mapping-folder]");
    Console.WriteLine("  apply-ship-dispositions <ship-dispositions.review.json> [mapping-folder] [--version <version>] [--apply]");
    Console.WriteLine("  resolve-official-ship-aliases <repo-folder> <xwing-data-folder> [mapping-folder]");
    Console.WriteLine("  promote-official-ship-aliases <official-alias-mappings.proposed.json> [mapping-folder] [--version <version>] [--apply]");
    Console.WriteLine("  prepare-first-edition-pilots <repo-folder> <xwing-data-folder> [mapping-folder]");
    Console.WriteLine("  approve-first-edition-pilots <pilots.canonical.proposed.json> <pilot-source-alternates.proposed.json> [mapping-folder] --version <version> [--apply]");
    Console.WriteLine("  review-ambiguous-pilots <repo-folder> <xwing-data-folder> [mapping-folder]");
    Console.WriteLine("  apply-ambiguous-pilot-resolutions <ambiguous-pilot-resolutions.review.json> [mapping-folder] --version <version> [--apply]");
    Console.WriteLine("  prepare-first-edition-upgrades <repo-folder> <xwing-data-folder>");
    Console.WriteLine("  approve-first-edition-upgrades <canonical.json> <alternates.json> <matches.csv> [mapping-folder] --version <version> [--apply]");
    Console.WriteLine("  review-ambiguous-upgrades <repo-folder> <xwing-data-folder> [mapping-folder]");
    Console.WriteLine("  apply-ambiguous-upgrade-resolutions <ambiguous-upgrade-resolutions.review.json> [mapping-folder] --version <version> [--apply]");
    Console.WriteLine("  first-edition-repository <repo-folder> [mapping-folder] [--allow-source-errors] [--output <json-file>]");
    Console.WriteLine("  inspect-first-edition <repo-folder> <ship|pilot|upgrade> <target-id> [mapping-folder] [--allow-source-errors]");
    Console.WriteLine("  build-asset-catalogue <repo-folder> <legacy-save.json> [mapping-folder] [--allow-source-errors] [--output <folder>]  # creates role-ranked review files");
}

static int UnknownCommand(string command)
{
    Console.WriteLine($"Unknown command: {command}");
    ShowHelp();
    return 1;
}
