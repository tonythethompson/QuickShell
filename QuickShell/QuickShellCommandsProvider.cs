using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Pages;

namespace QuickShell;

public partial class QuickShellCommandsProvider : CommandProvider, IDisposable
{
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly QuickShellPage _page;
    private readonly QuickShellFallbackPage _fallbackPage;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;
    private readonly EventHandler _settingsChangedHandler;

    public QuickShellCommandsProvider()
    {
        _settingsManager = new QuickShellSettingsManager();

        DisplayName = "Quick Shell";
        Icon = new IconInfo("\uE756");
        Settings = _settingsManager.Settings;

        _page = new QuickShellPage(_settingsManager);
        _settingsChangedHandler = (_, _) => _page.Reload();
        _settingsManager.SettingsChanged += _settingsChangedHandler;

        var settingsPage = _settingsManager.SettingsPage;

        _commands =
        [
            new CommandItem(_page)
            {
                Title = DisplayName,
                Subtitle = "Open saved folders in your terminal",
                Icon = new IconInfo("\uE756"),
                MoreCommands =
                [
                    new CommandContextItem(settingsPage),
                ],
            },
        ];

        _fallbackPage = new QuickShellFallbackPage(_settingsManager);
        _fallbacks = [new QuickShellFallback(_fallbackPage)];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;

    public override void Dispose()
    {
        _settingsManager.SettingsChanged -= _settingsChangedHandler;
        _page.Dispose();
        _fallbackPage.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
