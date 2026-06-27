using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Pages;

namespace QuickShell;

internal sealed partial class QuickShellCommandSettings : ICommandSettings
{
    public QuickShellCommandSettings(Settings settings, IContentPage settingsPage)
    {
        SettingsPage = settingsPage;
    }

    public IContentPage SettingsPage { get; }
}
