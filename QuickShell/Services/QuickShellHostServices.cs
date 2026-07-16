namespace QuickShell.Services;

internal sealed class QuickShellHostServices
{
    public QuickShellHostServices(IQuickShellServices services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IQuickShellServices Services { get; }

    public IShortcutRepository Shortcuts => Services.Shortcuts;

    public QuickShellSettingsManager Settings => Services.Settings;
}
