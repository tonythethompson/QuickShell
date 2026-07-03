namespace QuickShell.Services;

internal static class ShortcutFormTemplateCache
{
    private static readonly object Sync = new();

    private static string? _templateJson;
    private static int _commandCount = -1;
    private static string? _terminalApplicationId;
    private static string? _companionChoicesJson;

    public static string GetOrBuild(
        int commandCount,
        string terminalApplicationId,
        string companionChoicesJson,
        Func<string> buildTemplate)
    {
        lock (Sync)
        {
            if (_templateJson is not null
                && _commandCount == commandCount
                && string.Equals(_terminalApplicationId, terminalApplicationId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_companionChoicesJson, companionChoicesJson, StringComparison.Ordinal))
            {
                return _templateJson;
            }

            var built = buildTemplate();
            _commandCount = commandCount;
            _terminalApplicationId = terminalApplicationId;
            _companionChoicesJson = companionChoicesJson;
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
            _companionChoicesJson = null;
        }
    }
}
