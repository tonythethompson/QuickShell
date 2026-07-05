namespace QuickShell.Services;

internal static class LaunchDiagnosticsState
{
    public static LaunchDiagnosticsReport? LastReport { get; private set; }

    public static void Set(LaunchDiagnosticsReport? report)
    {
        if (report is not null)
        {
            LastReport = report;
        }
    }

    public static bool TryCopyLastReport(out string message)
    {
        if (LastReport is null)
        {
            message = "No launch diagnostics are available yet.";
            return false;
        }

        if (!StaClipboard.TrySetText(LastReport.ToClipboardText()))
        {
            message = "Failed to copy launch diagnostics to clipboard.";
            return false;
        }

        message = "Launch diagnostics copied to clipboard.";
        return true;
    }
}
