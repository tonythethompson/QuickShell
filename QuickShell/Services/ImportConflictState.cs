namespace QuickShell.Services;

internal static class ImportConflictState
{
    private static PendingImport? _pending;

    public static bool HasPending => _pending is not null;

    public static PendingImport? Pending => _pending;

    public static void Set(string path, int conflictCount, int importCount, Action onReload) =>
        _pending = new PendingImport(path, conflictCount, importCount, onReload);

    public static void Clear() => _pending = null;

    public static bool TryAbandonPending(out string message)
    {
        if (_pending is null)
        {
            message = string.Empty;
            return false;
        }

        Clear();
        message = "Import cancelled because you left settings.";
        return true;
    }

    internal sealed record PendingImport(string Path, int ConflictCount, int ImportCount, Action OnReload);
}
