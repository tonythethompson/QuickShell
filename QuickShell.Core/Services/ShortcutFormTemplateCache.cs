namespace QuickShell.Services;

internal static class ShortcutFormTemplateCache
{
    private static readonly object Sync = new();

    private static string? _templateJson;
    private static int _commandCount = -1;
    private static int _companionCount = -1;
    private static string? _terminalApplicationId;
    private static string? _companionChoicesJson;
    private static string? _taskTypeChoicesJson;

    /// <summary>
    /// Retrieves the cached shortcut form template or builds and caches a template for the specified inputs.
    /// </summary>
    /// <param name="commandCount">The number of commands represented by the template.</param>
    /// <param name="companionCount">The number of companions represented by the template.</param>
    /// <param name="terminalApplicationId">The terminal application identifier represented by the template.</param>
    /// <param name="companionChoicesJson">The JSON representation of the companion choices.</param>
    /// <param name="taskTypeChoicesJson">The JSON representation of the task type choices.</param>
    /// <param name="buildTemplate">The delegate used to build the template when the cache does not match the specified inputs.</param>
    /// <returns>The cached or newly built shortcut form template.</returns>
    public static string GetOrBuild(
        int commandCount,
        int companionCount,
        string terminalApplicationId,
        string companionChoicesJson,
        string taskTypeChoicesJson,
        Func<string> buildTemplate)
    {
        lock (Sync)
        {
            if (Matches(commandCount, companionCount, terminalApplicationId, companionChoicesJson, taskTypeChoicesJson))
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
            _companionCount = companionCount;
            _terminalApplicationId = terminalApplicationId;
            _companionChoicesJson = companionChoicesJson;
            _taskTypeChoicesJson = taskTypeChoicesJson;
            _templateJson = built;
            return built;
        }
    }

    /// <summary>
    /// Determines whether the specified inputs match the cached template key.
    /// </summary>
    /// <param name="commandCount">The number of commands.</param>
    /// <param name="companionCount">The number of companions.</param>
    /// <param name="terminalApplicationId">The terminal application identifier.</param>
    /// <param name="companionChoicesJson">The serialized companion choices.</param>
    /// <param name="taskTypeChoicesJson">The serialized task type choices.</param>
    /// <returns><c>true</c> if a cached template exists and all inputs match; <c>false</c> otherwise.</returns>
    private static bool Matches(
        int commandCount,
        int companionCount,
        string terminalApplicationId,
        string companionChoicesJson,
        string taskTypeChoicesJson) =>
        _templateJson is not null
        && _commandCount == commandCount
        && _companionCount == companionCount
        && string.Equals(_terminalApplicationId, terminalApplicationId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(_companionChoicesJson, companionChoicesJson, StringComparison.Ordinal)
        && string.Equals(_taskTypeChoicesJson, taskTypeChoicesJson, StringComparison.Ordinal);

    /// <summary>
    /// Clears the cached shortcut form template and its associated cache key.
    /// </summary>
    public static void Invalidate()
    {
        lock (Sync)
        {
            _templateJson = null;
            _commandCount = -1;
            _companionCount = -1;
            _terminalApplicationId = null;
            _companionChoicesJson = null;
            _taskTypeChoicesJson = null;
        }
    }
}
