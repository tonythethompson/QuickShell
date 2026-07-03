using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell;

public partial class QuickShellCommandsProvider : CommandProvider, IDisposable
{
#if CMDPAL_HOVER_ACTIONS
    public override HoverActionsMode DefaultHoverActionsMode => HoverActionsMode.Explicit;
#endif
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly QuickShellPage _page;
    private readonly CreateShortcutCommand _createShortcutCommand;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
    private readonly QuickShellFallbackPage _fallbackPage;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;
    private readonly EventHandler _settingsChangedHandler;

    public QuickShellCommandsProvider()
    {
        _settingsManager = new QuickShellSettingsManager(ReloadPages);
        QuickShellRuntimeServices.Initialize(_settingsManager);

        DisplayName = QuickShellBrand.DisplayName;
        Icon = QuickShellBrandIcons.App;
        Id = "com.quickshell";
        Settings = _settingsManager.Settings;

        _createShortcutCommand = new CreateShortcutCommand(ReloadPages);
        _discoverGitReposCommand = new OpenDiscoverGitReposCommand(ReloadPages);
        _page = new QuickShellPage(_settingsManager, _createShortcutCommand);
        _settingsChangedHandler = (_, _) => _page.Reload();
        _settingsManager.SettingsChanged += _settingsChangedHandler;

        var settingsPage = _settingsManager.SettingsPage;

        _commands =
        [
            new CommandItem(_page)
            {
                Title = DisplayName,
                Subtitle = "Open saved folders in any terminal you use",
                Icon = QuickShellBrandIcons.App,
#if CMDPAL_HOVER_ACTIONS
                HomeHoverActionsMode = HoverActionsMode.Explicit,
#endif
                MoreCommands =
                [
                    new CommandContextItem(_createShortcutCommand)
                    {
                        Title = "Create workspace",
                        Icon = new IconInfo("\uE710"),
                        RequestedShortcut = QuickShellKeyboardShortcuts.CreateShortcut,
#if CMDPAL_HOVER_ACTIONS
                        ShowInHoverActions = true,
                        HoverOrder = 0,
#endif
                    },
                    new CommandContextItem(settingsPage)
                    {
                        Title = QuickShellBrand.SettingsTitle,
                        Icon = new IconInfo("\uE713"),
#if CMDPAL_HOVER_ACTIONS
                        ShowInHoverActions = true,
                        HoverOrder = 10,
#endif
                    },
                ],
            },
        ];

        _fallbackPage = new QuickShellFallbackPage(_settingsManager, ReloadPages);
        _fallbacks = [new QuickShellFallback(_fallbackPage, _discoverGitReposCommand)];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;

    private void ReloadPages()
    {
        GitRepoIndex.Invalidate();
        _page.Reload();
        _fallbackPage.ClearResults();
    }

    public override ICommandItem? GetCommandItem(string id)
    {
        if (string.Equals(id, QuickShellExtensionSettingsPage.PageId, StringComparison.Ordinal) ||
            string.Equals(id, ImportConflictPage.PageId, StringComparison.Ordinal) ||
            string.Equals(id, PendingShortcutEditPage.PageId, StringComparison.Ordinal))
        {
            return new CommandItem(_settingsManager.SettingsPage)
            {
                Title = _settingsManager.SettingsPage.Title,
                Icon = _settingsManager.SettingsPage.Icon,
            };
        }

        if (string.Equals(id, ShortcutCommandIds.CreateShortcut, StringComparison.Ordinal))
        {
            return new CommandItem(new CreateShortcutCommand(ReloadPages))
            {
                Title = "Create workspace",
                Subtitle = "Folder and terminal launches",
                Icon = new IconInfo("\uE710"),
            };
        }

        if (string.Equals(id, DiscoverGitReposPage.PageId, StringComparison.Ordinal))
        {
            return new CommandItem(new OpenDiscoverGitReposCommand(ReloadPages))
            {
                Title = "Discover git repos",
                Icon = new IconInfo(ShortcutGlyphs.Discover),
            };
        }

        if (string.Equals(id, ShortcutLaunchFormPage.PageId, StringComparison.Ordinal))
        {
            return new CommandItem(new ShortcutLaunchFormPage())
            {
                Title = "Edit terminal",
                Icon = new IconInfo("\uE756"),
            };
        }

        if (ShortcutCommandIds.TryParseOpen(id, out var openKey))
        {
            var shortcut = QuickShellRuntimeServices.Shortcuts.ResolveForOpenCommand(openKey);
            if (shortcut is null)
            {
                return null;
            }

            return ShortcutListItems.CreateOpen(shortcut, _settingsManager, ReloadPages, _createShortcutCommand);
        }

        return base.GetCommandItem(id);
    }

    public override void Dispose()
    {
        _settingsManager.SettingsChanged -= _settingsChangedHandler;
        _page.Dispose();
        _fallbackPage.Dispose();
        QuickShellRuntimeServices.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
