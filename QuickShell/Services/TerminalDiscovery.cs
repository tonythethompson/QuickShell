namespace QuickShell.Services;

internal static class TerminalDiscovery
{
    public static void Refresh(QuickShellSettingsManager settingsManager)
    {
        TerminalCatalog.InvalidateCache();
        settingsManager.RefreshTerminalChoices();
    }
}
