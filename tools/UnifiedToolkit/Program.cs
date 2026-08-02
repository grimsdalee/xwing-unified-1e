using UnifiedToolkit.Commands;
using UnifiedToolkit.Commands.RepositoryMaintenance;

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
    "approve-asset-resolutions" => ApproveAssetResolutionsCommand.Run(commandArgs),
    "review-ship-assets" => ReviewShipAssetsCommand.Run(commandArgs),
    "apply-reviewed-ship-assets" => ApplyReviewedShipAssetsCommand.Run(commandArgs),
    "approve-clear-ship-recommendations" => ApproveClearShipRecommendationsCommand.Run(commandArgs),
    "prepare-curated-ship-asset-reviews" => PrepareCuratedShipAssetReviewsCommand.Run(commandArgs),
    "build-hybrid-ships" => BuildHybridShipDefinitionsCommand.Run(commandArgs),
    "build-ship-prototypes" => BuildShipPrototypesCommand.Run(commandArgs),
    "inspect-spawner-runtime" => InspectSpawnerRuntimeCommand.Run(commandArgs),
    "extract-ship-construction-recipes" => ExtractShipConstructionRecipesCommand.Run(commandArgs),
    "build-first-edition-ship-recipe" => BuildFirstEditionShipRecipeCommand.Run(commandArgs),
    "build-first-edition-ship-object-model" => BuildFirstEditionShipObjectModelCommand.Run(commandArgs),
    "serialize-first-edition-ship-test-save" => SerializeFirstEditionShipTestSaveCommand.Run(commandArgs),
    "capture-runtime-ship-prototype" => CaptureRuntimeShipPrototypeCommand.Run(commandArgs),
    "clone-runtime-ship-prototype" => CloneRuntimeShipPrototypeCommand.Run(commandArgs),
    "ingest-runtime-prototype-assets" => IngestRuntimePrototypeAssetsCommand.Run(commandArgs),
    "catalogue-repository-assets" => CatalogueRepositoryAssetsCommand.Run(commandArgs),
    "optimise-ship-textures" => OptimiseShipTexturesCommand.Run(commandArgs),
    "import-unified-assets" => ImportUnifiedAssetsCommand.Run(commandArgs),
    "import-legacy-first-edition-assets" => ImportLegacyFirstEditionAssetsCommand.Run(commandArgs),
    "import-xwing-data" => ImportXWingDataCommand.Run(commandArgs),
    "build-knowledge-base" => BuildKnowledgeBaseCommand.Run(commandArgs),
    "query-knowledge-base" => QueryKnowledgeBaseCommand.Run(commandArgs),
    "link-ship-assets" => LinkShipAssetsCommand.Run(commandArgs),
    "link-pilot-assets" => LinkPilotAssetsCommand.Run(commandArgs),
    "prepare-pilot-token-review" => PreparePilotTokenReviewCommand.Run(commandArgs),
    "apply-pilot-token-sheet-decisions" => ApplyPilotTokenSheetDecisionsCommand.Run(commandArgs),
    "prepare-pilot-token-extraction" => PreparePilotTokenExtractionCommand.Run(commandArgs),
    "prepare-pilot-token-extraction-review" => PreparePilotTokenExtractionReviewCommand.Run(commandArgs),
    "extract-pilot-tokens" => ExtractPilotTokensCommand.Run(commandArgs),
    "recover-pilot-tokens" => RecoverPilotTokensCommand.Run(commandArgs),
    "prepare-pilot-token-generation" => PreparePilotTokenGenerationCommand.Run(commandArgs),
    "prepare-pilot-token-editor" => PreparePilotTokenEditorCommand.Run(commandArgs),
    "audit-pilot-token-inventory" => AuditPilotTokenInventoryCommand.Run(commandArgs),
    "audit-pilot-token-images" => AuditPilotTokenImagesCommand.Run(commandArgs),
    "import-generated-pilot-tokens" => ImportGeneratedPilotTokensCommand.Run(commandArgs),
    "import-assets" => ImportAssetsCommand.Run(commandArgs),
    "import-first-edition-dials" => ImportFirstEditionDialsCommand.Run(commandArgs),
    "standardise-first-edition-dials" => StandardiseFirstEditionDialsCommand.Run(commandArgs),
    "inspect-legacy-pilot-source" => InspectLegacyPilotSourceCommand.Run(commandArgs),
    "plan-ship-packages" => PlanShipPackagesCommand.Run(commandArgs),
    "analyse-dial-runtime" => AnalyseDialRuntimeCommand.Run(commandArgs),
    "analyse-ship-runtime" => AnalyseShipRuntimeCommand.Run(commandArgs),
    "prepare-first-edition-dial-data" => PrepareFirstEditionDialDataCommand.Run(commandArgs),
    "extend-standard-first-edition-ships" => ExtendStandardFirstEditionShipsCommand.Run(commandArgs),
    "extend-standard-first-edition-pilots" => ExtendStandardFirstEditionPilotsCommand.Run(commandArgs),
    "import-official-first-edition-pilots" => ImportOfficialFirstEditionPilotsCommand.Run(commandArgs),
    "audit-official-first-edition-content" => AuditOfficialFirstEditionContentCommand.Run(commandArgs),
    "audit-first-edition-pilot-completeness" => AuditFirstEditionPilotCompletenessCommand.Run(commandArgs),
    "prepare-missing-first-edition-pilots" => PrepareMissingFirstEditionPilotsCommand.Run(commandArgs),
    "import-missing-first-edition-pilots" => ImportMissingFirstEditionPilotsCommand.Run(commandArgs),
    "prepare-missing-pilot-package-assets" => PrepareMissingPilotPackageAssetsCommand.Run(commandArgs),
    "import-deferred-epic-first-edition-pilots" => ImportDeferredEpicFirstEditionPilotsCommand.Run(commandArgs),
    "build-official-artwork-manifest" => BuildOfficialArtworkManifestCommand.Run(commandArgs),
    "prepare-standard-first-edition-runtime-data" => PrepareStandardFirstEditionRuntimeDataCommand.Run(commandArgs),
    "analyse-runtime-action-codes" => AnalyseRuntimeActionCodesCommand.Run(commandArgs),
    "generate-standard-first-edition-runtime-payloads" => GenerateStandardFirstEditionRuntimePayloadsCommand.Run(commandArgs),
    "prepare-first-edition-maneuver-icons" => PrepareFirstEditionManeuverIconsCommand.Run(commandArgs),
    "build-first-edition-maneuver-icon-library" => BuildFirstEditionManeuverIconLibraryCommand.Run(commandArgs),
    "register-first-edition-maneuver-icons" => RegisterFirstEditionManeuverIconsCommand.Run(commandArgs),
    "build-first-edition-dial-runtime" => BuildFirstEditionDialRuntimeCommand.Run(commandArgs),
    "build-first-edition-dial-model" => BuildFirstEditionDialModelCommand.Run(commandArgs),
    "prepare-five-ship-prototype-assembly" => PrepareFiveShipPrototypeAssemblyCommand.Run(commandArgs),
    "inspect-prototype-runtime-templates" => InspectPrototypeRuntimeTemplatesCommand.Run(commandArgs),
    "catalogue-ship-peg-assets" => CatalogueShipPegAssetsCommand.Run(commandArgs),
    "extract-runtime-templates" => ExtractRuntimeTemplatesCommand.Run(commandArgs),
    "generate-prototype-save" => GeneratePrototypeSaveCommand.Run(commandArgs),
    "generate-ship-validation-saves" => GenerateShipValidationSavesCommand.Run(commandArgs),
    "verify-obsolete-models" => VerifyObsoleteModelsCommand.Run(commandArgs),
    "quarantine-obsolete-models" => QuarantineObsoleteModelsCommand.Run(commandArgs),
    "restore-quarantined-models" => RestoreQuarantinedModelsCommand.Run(commandArgs),
    "purge-quarantined-models" => PurgeQuarantinedModelsCommand.Run(commandArgs),
    "audit-ship-model-inventory" => AuditShipModelInventoryCommand.Run(commandArgs),
    "promote-ship-model-review-candidates" => PromoteShipModelReviewCandidatesCommand.Run(commandArgs),
    "migrate-ship-model-pipeline-references" => MigrateShipModelPipelineReferencesCommand.Run(commandArgs),
    "audit-prototype-artwork-candidates" => AuditPrototypeArtworkCandidatesCommand.Run(commandArgs),
    "generate-first-edition-dial-backs" => GenerateFirstEditionDialBacksCommand.Run(commandArgs),
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
    Console.WriteLine("  approve-asset-resolutions <asset-resolutions.review.json> <asset-catalogue.json> [mapping-folder] [--version <asset-version>] [--apply]");
    Console.WriteLine("  review-ship-assets <asset-resolutions.review.json> [mapping-folder] [--output <review.json>]");
    Console.WriteLine("  apply-reviewed-ship-assets <ship-assets.review.json> <asset-catalogue.json> [mapping-folder] --version <asset-version> [--apply]");
    Console.WriteLine("  approve-clear-ship-recommendations <ship-assets.review.json> <asset-catalogue.json> [mapping-folder] --version <asset-version> [--output <reviewed.json>] [--apply]");
    Console.WriteLine("  prepare-curated-ship-asset-reviews <ship-assets.review.json> [--output <folder>]");
    Console.WriteLine("  build-hybrid-ships <repo-folder> <unified-2.5-save.json> <legacy-1e-save.json> [mapping-folder] [--allow-source-errors] [--output <folder>]");
    Console.WriteLine("  build-ship-prototypes <hybrid-ship-definitions.json> <unified-2.5-save.json> [--output <folder>]");
    Console.WriteLine("  inspect-spawner-runtime <unified-2.5-save.json> [--output <folder>]");
    Console.WriteLine("  extract-ship-construction-recipes <unified-2.5-save.json> [--runtime-report <spawner-runtime-report.json>] [--output <folder>]");
    Console.WriteLine("  build-first-edition-ship-recipe <hybrid-ship-definitions.json> <ship-construction-recipes.json> [--ship <id-or-name>] [--output <folder>]");
    Console.WriteLine("  build-first-edition-ship-object-model <hybrid-ship-definitions.json> <ship-construction-recipes.json> [--ship <id-or-name>] [--output <folder>]");
    Console.WriteLine("  serialize-first-edition-ship-test-save <first-edition-ship-object-model.json> <unified-2.5-save.json> <unified-repo-folder> [--output <folder>]");
    Console.WriteLine("  capture-runtime-ship-prototype <spawned-save.json> --guid <object-guid> [--output <folder>]");
    Console.WriteLine("  clone-runtime-ship-prototype <runtime-ship-prototype.json> <tts-envelope-save.json> [--output <folder>]");
    Console.WriteLine("  ingest-runtime-prototype-assets <runtime-ship-prototype.json> <unified-repo-folder> <first-edition-repo-folder> [--public-base-url <url>] [--download-external] [--output <folder>]");
    Console.WriteLine("  catalogue-repository-assets <first-edition-repo-folder> [--output <folder>]");
    Console.WriteLine("  optimise-ship-textures <first-edition-repo-folder> [--quality <1-100>] [--minimum-savings-percent <0-100>] [--output <folder>] [--apply] [--overwrite]");
    Console.WriteLine("  import-unified-assets <unified-repo-folder> <first-edition-repo-folder> [--dry-run] [--output <folder>]");
    Console.WriteLine("  import-xwing-data <xwing-data-folder> <first-edition-repo-folder> [--dry-run] [--no-rebuild-knowledge-base]");
    Console.WriteLine("  import-legacy-first-edition-assets <legacy-save.json> <first-edition-repo-folder> [--dry-run] [--output <folder>]");
    Console.WriteLine("  build-knowledge-base <first-edition-repo-folder> [--output <folder>] [--no-refresh-catalogue]");
    Console.WriteLine("  query-knowledge-base <first-edition-repo-folder> <stats|asset|search|duplicates|unavailable|validation> [arguments]");
    Console.WriteLine("  link-ship-assets <first-edition-repo-folder> [--ships <ships.json>] [--candidates <1-50>] [--output <folder>]");
    Console.WriteLine("  link-pilot-assets <first-edition-repo-folder> [--pilots <pilots.json>] [--candidates <1-50>] [--output <folder>]");
    Console.WriteLine("  prepare-pilot-token-review <first-edition-repo-folder> [--pilot-links <pilot-links.json>] [--output <folder>]");
    Console.WriteLine("  apply-pilot-token-sheet-decisions <first-edition-repo-folder> <decisions.csv> [--candidates <1-50>]");
    Console.WriteLine("  prepare-pilot-token-extraction <first-edition-repo-folder> [--pilot-links <pilot-links.json>] [--output <folder>]");
    Console.WriteLine("  prepare-pilot-token-extraction-review <first-edition-repo-folder> <existing-plan.json> [--output <folder>]");
    Console.WriteLine("  extract-pilot-tokens <first-edition-repo-folder> <completed-plan.json> [--output <folder>]");
    Console.WriteLine("  recover-pilot-tokens <first-edition-repo-folder> <completed-recovery-plan.json> [--output <folder>] [--overwrite]");
    Console.WriteLine("  prepare-pilot-token-generation <first-edition-repo-folder> [--output <folder>]");
    Console.WriteLine("  prepare-pilot-token-editor <first-edition-repo-folder> <completed-generation-plan.json> [--output <folder>]");
    Console.WriteLine("  audit-pilot-token-inventory <first-edition-repo-folder> [--output <folder>]");
    Console.WriteLine("  audit-pilot-token-images <first-edition-repo-folder> [--output <folder>]");
    Console.WriteLine("  import-generated-pilot-tokens <first-edition-repo-folder>");
    Console.WriteLine("  import-assets <first-edition-repo-folder> <generated-pilot-tokens|first-edition-dials>");
    Console.WriteLine("  import-first-edition-dials <first-edition-repo-folder>");
    Console.WriteLine("  standardise-first-edition-dials <first-edition-repo-folder> [--inventory-only]");
    Console.WriteLine("  inspect-legacy-pilot-source <first-edition-repo-folder> <pilot-name> [--legacy-save <save.json>] [--output <folder>]");
    Console.WriteLine("  plan-ship-packages <first-edition-repo-folder> [mapping-folder] [--allow-source-errors] [--output <folder>]");
    Console.WriteLine("  analyse-dial-runtime <tts-save.json> [--output <folder>]");
    Console.WriteLine("  analyse-ship-runtime <tts-save.json> [--output <folder>]");
    Console.WriteLine("  prepare-first-edition-dial-data <first-edition-repo-folder> <xwing-data-folder> [mapping-folder] [--output <folder>]");
    Console.WriteLine("  extend-standard-first-edition-ships <first-edition-repo-folder> <xwing-data-folder> [--mapping-folder <folder>] [--version <version>] [--apply]");
    Console.WriteLine("  extend-standard-first-edition-pilots <first-edition-repo-folder> <xwing-data-folder> [--mapping-folder <folder>] [--version <version>] [--apply]");
    Console.WriteLine("  import-official-first-edition-pilots <first-edition-repo-folder> <xwing-data-folder> [--mapping-folder <folder>] [--version <version>] [--apply]");
    Console.WriteLine("  audit-official-first-edition-content <first-edition-repo-folder> [xwing-data-folder] [mapping-folder] [--output <folder>]");
    Console.WriteLine("  build-official-artwork-manifest <first-edition-repo-folder> [xwing-data-folder] [--output <folder>]");
    Console.WriteLine("  prepare-standard-first-edition-runtime-data <first-edition-repo-folder> [xwing-data-folder] [mapping-folder] [--output <folder>]");
    Console.WriteLine("  analyse-runtime-action-codes <first-edition-repo-folder> [--runtime-data <file>] [--output <folder>]");
    Console.WriteLine("  generate-standard-first-edition-runtime-payloads <first-edition-repo-folder> [--runtime-data <file>] [--action-analysis <file>] [--output <folder>]");
    Console.WriteLine("  prepare-first-edition-maneuver-icons <first-edition-repo-folder> [--runtime-payloads <file>] [--output <folder>] [--inventory-only]");
    Console.WriteLine("  build-first-edition-maneuver-icon-library <first-edition-repo-folder> [--runtime-data <file>] [--output <folder>] [--validate-only]");
    Console.WriteLine("  register-first-edition-maneuver-icons <first-edition-repo-folder> [--icon-library <file>] [--runtime-data <file>] [--output <folder>]");
    Console.WriteLine("  build-first-edition-dial-runtime <first-edition-repo-folder> [--icon-contract <file>] [--asset-base-url <url>] [--output <folder>]");
    Console.WriteLine("  build-first-edition-dial-model <first-edition-repo-folder> [--front-rotation-degrees <degrees>] [--source <file-or-url>] [--output <file>]");
    Console.WriteLine("  prepare-five-ship-prototype-assembly <first-edition-repo-folder> [--package-plan <file>] [--runtime-payloads <file>] [--dial-runtime <file>] [--output <folder>]");
    Console.WriteLine("  inspect-prototype-runtime-templates <tts-save.json> [--output <folder>]");
    Console.WriteLine("  catalogue-ship-peg-assets <first-edition-repo-folder> [--output <folder>]");
    Console.WriteLine("  extract-runtime-templates <first-edition-repo-folder> <tts-save.json> [--peg-catalogue <file>] [--asset-base-url <url>] [--output <folder>]");
    Console.WriteLine("  generate-prototype-save <first-edition-repo-folder> <reference-save.json> [--assembly-plan <file>] [--runtime-templates <file>] [--asset-base-url <url>] [--output <file>]");
    Console.WriteLine("  generate-ship-validation-saves <first-edition-repo-folder> <reference-save.json> [--package-plan <file>] [--runtime-payloads <file>] [--runtime-templates <file>] [--asset-base-url <url>] [--output <folder>]");
    Console.WriteLine("  verify-obsolete-models <first-edition-repo-folder> [--audit <file>]");
    Console.WriteLine("  quarantine-obsolete-models <first-edition-repo-folder> [--audit <file>]");
    Console.WriteLine("  restore-quarantined-models <first-edition-repo-folder> [--audit <file>]");
    Console.WriteLine("  purge-quarantined-models <first-edition-repo-folder> [--audit <file>] --confirm-purge");
    Console.WriteLine("  audit-ship-model-inventory <first-edition-repo-folder> [--output <folder>]");
    Console.WriteLine("  promote-ship-model-review-candidates <first-edition-repo-folder> [--inventory <file>] [--audit <file>]");
    Console.WriteLine("  migrate-ship-model-pipeline-references <first-edition-repo-folder> [--apply]");
    Console.WriteLine("  audit-prototype-artwork-candidates <first-edition-repo-folder> [--output <folder>]");
    Console.WriteLine("  prepare-missing-first-edition-pilots <repository> [--audit <file>] [--output <folder>]");
    Console.WriteLine("  import-missing-first-edition-pilots <repository> [--proposals <file>] [--mapping-folder <folder>] [--version <version>] [--apply]");
    Console.WriteLine("  prepare-missing-pilot-package-assets <repository> [--package-plan <file>] [--output <folder>]");
    Console.WriteLine("  import-deferred-epic-first-edition-pilots <repository> [--proposals <file>] [--mapping-folder <folder>] [--version <version>] [--apply]");
    Console.WriteLine("  generate-first-edition-dial-backs <first-edition-repo-folder> [--output <folder>]");
}

static int UnknownCommand(string command)
{
    Console.WriteLine($"Unknown command: {command}");
    ShowHelp();
    return 1;
}
