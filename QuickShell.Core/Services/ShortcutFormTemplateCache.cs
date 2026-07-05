namespace QuickShell.Services;

internal static class ShortcutFormTemplateCache
{
    private static readonly object Sync = new();

    private static string? _templateJson;
    private static int _commandCount = -1;
    private static string? _terminalApplicationId;
    private static string? _companionChoicesJson;
    private static string? _taskTypeChoicesJson;

    public static string GetOrBuild(
        int commandCount,
        string terminalApplicationId,
        string companionChoicesJson,
        string taskTypeChoicesJson,
        Func<string> buildTemplate)
    {
        lock (Sync)
        {
            if (Matches(commandCount, terminalApplicationId, companionChoicesJson, taskTypeChoicesJson))
            {
                return _templateJson!;
            }
        }

        // Build outside the lock so an expensive template build for one
        // (commandCount, terminalApplicationId) combo doesn't block unrelated
        // GetOrBuild/Invalidate callers for its full duration.
        var built = buildTemplate();

        lock (Sync)
        {
            _commandCount = commandCount;
            _terminalApplicationId = terminalApplicationId;
            _companionChoicesJson = companionChoicesJson;
            _taskTypeChoicesJson = taskTypeChoicesJson;
            _templateJson = built;
            return built;
        }
    }

    private static bool Matches(
        int commandCount,
        string terminalApplicationId,
        string companionChoicesJson,
        string taskTypeChoicesJson) =>
        _templateJson is not null
        && _commandCount == commandCount
        && string.Equals(_terminalApplicationId, terminalApplicationId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(_companionChoicesJson, companionChoicesJson, StringComparison.Ordinal)
        && string.Equals(_taskTypeChoicesJson, taskTypeChoicesJson, StringComparison.Ordinal);

    public static void Invalidate()
    {
        lock (Sync)
        {
            _templateJson = null;
            _commandCount = -1;
            _terminalApplicationId = null;
            _companionChoicesJson = null;
            _taskTypeChoicesJson = null;
        }
    }
}
