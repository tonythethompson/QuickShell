using QuickShell.Commands;

namespace QuickShell.Services;

/// <summary>
/// Per-provider context built at the composition root. This is never registered in DI
/// because it captures provider-local callbacks (e.g. ReloadPages).
/// </summary>
internal sealed class QuickShellPageContext
{
    public QuickShellPageContext(
        QuickShellHostServices host,
        CreateShortcutCommand createShortcut,
        Action reloadRootPages)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        CreateShortcut = createShortcut ?? throw new ArgumentNullException(nameof(createShortcut));
        ReloadRootPages = reloadRootPages ?? throw new ArgumentNullException(nameof(reloadRootPages));
    }

    public QuickShellHostServices Host { get; }

    public IQuickShellServices Services => Host.Services;

    public IShortcutRepository Shortcuts => Host.Shortcuts;

    public QuickShellSettingsManager Settings => Host.Settings;

    public CreateShortcutCommand CreateShortcut { get; }

    public Action ReloadRootPages { get; }
}
