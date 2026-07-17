using System.Text;
using System.Text.Json;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class IngestRuntimePrototypeAssetsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: ingest-runtime-prototype-assets <runtime-ship-prototype.json> <unified-repo-folder> <first-edition-repo-folder> [--public-base-url <url>] [--download-external] [--output <folder>]");
            return 1;
        }

        var prototypePath = Path.GetFullPath(args[0]);
        var unifiedRepository = Path.GetFullPath(args[1]);
        var firstEditionRepository = Path.GetFullPath(args[2]);
        var publicBaseUrl = Option(args, "--public-base-url") ?? "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/main";
        var downloadExternal = args.Any(x => x.Equals("--download-external", StringComparison.OrdinalIgnoreCase));
        var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(firstEditionRepository, "_unifiedtoolkit_reports", "runtime-asset-ingestion-r1"));

        if (!File.Exists(prototypePath)) { Console.Error.WriteLine($"Prototype file was not found: {prototypePath}"); return 1; }
        if (!Directory.Exists(unifiedRepository)) { Console.Error.WriteLine($"Unified repository folder was not found: {unifiedRepository}"); return 1; }
        if (!Directory.Exists(firstEditionRepository)) { Console.Error.WriteLine($"First Edition repository folder was not found: {firstEditionRepository}"); return 1; }

        Console.WriteLine("UnifiedToolkit Phase 6C Revision 1 - Runtime Asset Ingestion");
        Console.WriteLine("===========================================================");
        Console.WriteLine();
        Console.WriteLine($"Runtime prototype:       {prototypePath}");
        Console.WriteLine($"Unified repository:      {unifiedRepository}");
        Console.WriteLine($"First Edition repository:{firstEditionRepository}");
        Console.WriteLine($"Public asset base URL:   {publicBaseUrl}");
        Console.WriteLine($"Download external:       {downloadExternal}");
        Console.WriteLine($"Output folder:           {output}");
        Console.WriteLine();

        try
        {
            var result = RuntimePrototypeAssetIngester.Ingest(prototypePath, unifiedRepository, firstEditionRepository, publicBaseUrl, downloadExternal, output);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "runtime-asset-ingestion-report.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            WriteManifestCsv(Path.Combine(output, "runtime-asset-manifest.csv"), result);
            WriteReport(Path.Combine(output, "RUNTIME-ASSET-INGESTION-REPORT.md"), result);

            Console.WriteLine($"URL occurrences:                 {result.UrlOccurrences}");
            Console.WriteLine($"Unique URLs:                     {result.UniqueUrls}");
            Console.WriteLine($"Local assets copied:             {result.LocalAssetsCopied}");
            Console.WriteLine($"Existing assets verified:        {result.ExistingAssetsVerified}");
            Console.WriteLine($"External assets downloaded:      {result.ExternalAssetsDownloaded}");
            Console.WriteLine($"External assets deferred:        {result.ExternalAssetsDeferred}");
            Console.WriteLine($"Rewritten URL occurrences:       {result.RewrittenUrlOccurrences}");
            Console.WriteLine($"Validation errors:               {result.ValidationErrors.Count}");
            Console.WriteLine($"Ready for repository-owned clone:{result.ReadyForRepositoryOwnedClone}");
            Console.WriteLine();
            Console.WriteLine($"Rewritten prototype: {result.RewrittenPrototypePath}");
            return result.ReadyForRepositoryOwnedClone ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static void WriteManifestCsv(string path, RuntimeAssetIngestionResult result)
    {
        var lines = new List<string> { "SourceKind,Status,Occurrences,SizeBytes,SHA256,SourceURL,SourceLocalPath,RepositoryRelativePath,DestinationLocalPath,RepositoryURL,Error" };
        lines.AddRange(result.Assets.Select(x => string.Join(',', new[]
        {
            Csv(x.SourceKind), Csv(x.Status), x.Occurrences.ToString(), x.SizeBytes.ToString(), Csv(x.Sha256), Csv(x.SourceUrl),
            Csv(x.SourceLocalPath), Csv(x.RepositoryRelativePath), Csv(x.DestinationLocalPath), Csv(x.RepositoryUrl), Csv(x.Error)
        })));
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static void WriteReport(string path, RuntimeAssetIngestionResult r)
    {
        var text = new StringBuilder();
        text.AppendLine("# Runtime Asset Ingestion Report").AppendLine();
        text.AppendLine($"- Unique URLs: `{r.UniqueUrls}`");
        text.AppendLine($"- Local assets copied: `{r.LocalAssetsCopied}`");
        text.AppendLine($"- Existing assets verified: `{r.ExistingAssetsVerified}`");
        text.AppendLine($"- External assets downloaded: `{r.ExternalAssetsDownloaded}`");
        text.AppendLine($"- External assets deferred: `{r.ExternalAssetsDeferred}`");
        text.AppendLine($"- Ready for repository-owned clone: `{r.ReadyForRepositoryOwnedClone}`");
        text.AppendLine().AppendLine("## Repository layout").AppendLine();
        text.AppendLine("- Unified assets: `assets/first-edition/mirrored/unified-2.5/...`");
        text.AppendLine("- Steam assets: `assets/first-edition/mirrored/external/steamworkshop/...`");
        text.AppendLine("- Other external assets: `assets/first-edition/mirrored/external/external/...`");
        text.AppendLine().AppendLine("## Important").AppendLine();
        text.AppendLine("The rewritten prototype points to the configured public repository URL. Commit and push the mirrored assets before loading a clone that uses those URLs in Tabletop Simulator.");
        if (r.ValidationErrors.Count > 0)
        {
            text.AppendLine().AppendLine("## Validation errors").AppendLine();
            foreach (var error in r.ValidationErrors) text.AppendLine($"- {error}");
        }
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }
}
