using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class QuickShellExtensionSettingsPage : ContentPage
{
    public const string PageId = CommandDescriptor.SettingsId;

    private readonly IQuickShellServices _services;
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action _onReload;
    private readonly object _contentSync = new();

    private BehaviorSettingsForm? _behaviorSettingsForm;

    public QuickShellExtensionSettingsPage(
        QuickShellSettingsManager settingsManager,
        IQuickShellServices services,
        Action? onReload = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _settingsManager = settingsManager;
        _onReload = onReload ?? (() => { });

        Id = PageId;
        Name = "Settings";
        Title = QuickShellBrand.SettingsTitle;
        Icon = new IconInfo("\uE713");
        Commands = ShortcutContextCommands.BuildUndoRedoCommands(_services, _onReload);
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
            content.Add(new PendingShortcutEditForm(_services, _onReload, refreshSettings));
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
            _behaviorSettingsForm ??= new BehaviorSettingsForm(_settingsManager, _services, _onReload, refreshSettings);
        }
    }
}
