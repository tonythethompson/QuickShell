using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class QuickShellExtensionSettingsPage : ContentPage
{
    public const string PageId = QuickShellDeepLinkIds.Settings;

    private readonly IQuickShellServices _services;
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action _onReload;
    private readonly object _contentSync = new();

    private BehaviorSettingsForm? _behaviorSettingsForm;

    public QuickShellExtensionSettingsPage(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null,
        IQuickShellServices? services = null)
    {
        _services = services ?? throw new InvalidOperationException("IQuickShellServices is required.");
        _settingsManager = settingsManager;
        _onReload = onReload ?? (() => { });

        Id = PageId;
        Name = "Settings";
        Title = QuickShellBrand.SettingsTitle;
        Icon = new IconInfo("\uE713");
        Commands = ShortcutContextCommands.BuildUndoRedoCommands(_onReload, _services);
    }

    public void RefreshContent()
    {
        EnsureSettingsForms();
        _behaviorSettingsForm?.SyncFromSettings();
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

        if (_services.Drafts.HasPending)
        {
            content.Add(new PendingShortcutEditForm(_onReload, _services, refreshSettings));
        }

        // Single card: terminal + Home/Multi/Git + Backup & Transfer (controlled spacing).
        content.Add(_behaviorSettingsForm!);
        return content.ToArray();
    }

    private void EnsureSettingsForms()
    {
        lock (_contentSync)
        {
            var refreshSettings = (Action)RefreshContent;
            _behaviorSettingsForm ??= new BehaviorSettingsForm(_settingsManager, _onReload, refreshSettings, _services);
        }
    }
}
