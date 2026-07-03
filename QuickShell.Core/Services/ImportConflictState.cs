namespace QuickShell.Services;

internal enum ImportTransferKind
{
    Projects,
    Workspaces,
}

internal static class ImportConflictState
{
    private static PendingImport? _pending;

    public static bool HasPending => _pending is not null;

    public static PendingImport? Pending => _pending;

    public static void Set(
        ImportTransferKind kind,
        string path,
        int conflictCount,
        int importCount,
        Action onReload) =>
        _pending = new PendingImport(kind, path, conflictCount, importCount, onReload);

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

    internal sealed record PendingImport(
        ImportTransferKind Kind,
        string Path,
        int ConflictCount,
        int ImportCount,
        Action OnReload);
}
