namespace QuickShell.Services;

internal static class TerminalDiscovery
{
    public static void Refresh(QuickShellSettingsManager settingsManager)
    {
        settingsManager.Services.TerminalCatalog.InvalidateCache();
        settingsManager.RefreshTerminalChoices();
    }
}
