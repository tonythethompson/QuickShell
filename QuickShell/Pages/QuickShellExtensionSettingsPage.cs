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
    private BehaviorSettingsForm? _behaviorSettingsForm;
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
        _behaviorSettingsForm?.SyncFromSettings();
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

        if (QuickShellServices.Current.Drafts.HasPending)
        {
            content.Add(new PendingShortcutEditForm(_onReload, refreshSettings));
        }

        content.Add(_terminalDefaultsForm!);
        content.Add(_behaviorSettingsForm!);
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
            _behaviorSettingsForm ??= new BehaviorSettingsForm(_settingsManager, _onReload, refreshSettings);
            _gitLaunchForm ??= new GitLaunchSettingsForm(_settingsManager, _onReload, refreshSettings);
            _transferForm ??= new ShortcutTransferSettingsForm(_onReload, refreshSettings);
        }
    }
}
