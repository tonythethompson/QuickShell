namespace QuickShell.Services;

internal static class SuggestCommandLineArgs
{
    public static bool TryParse(
        string[] args,
        out string? directory,
        out IReadOnlyList<string> usedCommands,
        out int generation)
    {
        directory = null;
        usedCommands = [];
        generation = 0;
        var used = new List<string>();

        if (args.Length == 0 || !string.Equals(args[0], "suggest", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                directory = args[++i];
                continue;
            }

            if (string.Equals(args[i], "--used", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                used.Add(args[++i]);
                continue;
            }

            if (string.Equals(args[i], "--generation", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out generation);
            }
        }

        usedCommands = used;
        return !string.IsNullOrWhiteSpace(directory);
    }
}
