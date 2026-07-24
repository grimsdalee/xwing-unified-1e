namespace UnifiedToolkit.Commands;

public static class ImportFirstEditionDialsCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  import-first-edition-dials <first-edition-repo-folder>");
            return 1;
        }

        return ImportAssetsCommand.Run(new[] { args[0], "first-edition-dials" });
    }
}
