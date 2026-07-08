using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class QuickShellExtensionSettingsPage : ContentPage
{
    public const string PageId = QuickShellDeepLinkIds.Settings;

    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action _onReload;
    private readonly object _contentSync = new();

    private TerminalDefaultsSettingsForm? _terminalDefaultsForm;
    private MultiLaunchSettingsForm? _multiLaunchForm;
    private HomeDisplaySettingsForm? _homeDisplayForm;
    private GitLaunchSettingsForm? _gitLaunchForm;
    private ShortcutTransferSettingsForm? _transferForm;

    public QuickShellExtensionSettingsPage(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null)
    {
        _settingsManager = settingsManager;
        _onReload = onReload ?? (() => { });

        Id = PageId;
        Name = "Settings";
        Title = QuickShellBrand.SettingsTitle;
        Icon = new IconInfo("\uE713");
        Commands = ShortcutContextCommands.BuildUndoRedoCommands(_onReload);
    }

    public void RefreshContent()
    {
        EnsureSettingsForms();
        _terminalDefaultsForm?.SyncFromSettings();
        _multiLaunchForm?.SyncFromSettings();
        _homeDisplayForm?.SyncFromSettings();
        _gitLaunchForm?.SyncFromSettings();
        RaiseItemsChanged();
    }

    public void PrewarmContent() => EnsureSettingsForms();

    public override IContent[] GetContent()
    {
        EnsureSettingsForms();
        return BuildContent();
    }

    private IContent[] BuildContent()
    {
        EnsureSettingsForms();
        var refreshSettings = (Action)RefreshContent;
        var content = new List<IContent>();

        if (QuickShellRuntimeServices.Drafts.HasPending)
        {
            content.Add(new PendingShortcutEditForm(_onReload, refreshSettings));
        }

        content.Add(_terminalDefaultsForm!);
        content.Add(_multiLaunchForm!);
        content.Add(_homeDisplayForm!);
        content.Add(_gitLaunchForm!);
        content.Add(_transferForm!);
        return content.ToArray();
    }

    private void EnsureSettingsForms()
    {
        lock (_contentSync)
        {
            var refreshSettings = (Action)RefreshContent;
            _terminalDefaultsForm ??= new TerminalDefaultsSettingsForm(_settingsManager, _onReload, refreshSettings);
            _multiLaunchForm ??= new MultiLaunchSettingsForm(_settingsManager, _onReload, refreshSettings);
            _homeDisplayForm ??= new HomeDisplaySettingsForm(_settingsManager, _onReload, refreshSettings);
            _gitLaunchForm ??= new GitLaunchSettingsForm(_settingsManager, _onReload, refreshSettings);
            _transferForm ??= new ShortcutTransferSettingsForm(_onReload, refreshSettings);
        }
    }
}
