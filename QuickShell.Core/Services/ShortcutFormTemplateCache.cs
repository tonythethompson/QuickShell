namespace QuickShell.Services;

internal static class ShortcutFormTemplateCache
{
    private static readonly object Sync = new();

    private static string? _templateJson;
    private static int _commandCount = -1;
    private static string? _terminalApplicationId;

    public static string GetOrBuild(
        int commandCount,
        string terminalApplicationId,
        Func<string> buildTemplate)
    {
        lock (Sync)
        {
            if (_templateJson is not null
                && _commandCount == commandCount
                && string.Equals(_terminalApplicationId, terminalApplicationId, StringComparison.OrdinalIgnoreCase))
            {
                return _templateJson;
            }

            var built = buildTemplate();
            _commandCount = commandCount;
            _terminalApplicationId = terminalApplicationId;
            _templateJson = built;
            return built;
        }
    }

    public static void Invalidate()
    {
        lock (Sync)
        {
            _templateJson = null;
            _commandCount = -1;
            _terminalApplicationId = null;
        }
    }
}
