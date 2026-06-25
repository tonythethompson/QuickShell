using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell;

public partial class QuickShellCommandsProvider : CommandProvider, IDisposable
{
    public override HoverActionsMode DefaultHoverActionsMode => HoverActionsMode.Explicit;
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly QuickShellPage _page;
    private readonly ImportConflictPage _importConflictPage;
    private readonly PendingShortcutEditPage _pendingShortcutEditPage;
    private readonly CreateShortcutCommand _createShortcutCommand;
    private readonly QuickShellFallbackPage _fallbackPage;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;
    private readonly EventHandler _settingsChangedHandler;

    public QuickShellCommandsProvider()
    {
        _settingsManager = new QuickShellSettingsManager();

        DisplayName = "Quick Shell";
        Icon = new IconInfo("\uE756");
        Id = "com.quickshell";
        Settings = _settingsManager.Settings;

        _importConflictPage = new ImportConflictPage(ReloadPages);
        _pendingShortcutEditPage = new PendingShortcutEditPage(ReloadPages);
        _createShortcutCommand = new CreateShortcutCommand(ReloadPages);
        _page = new QuickShellPage(_settingsManager, _importConflictPage, _pendingShortcutEditPage, _createShortcutCommand);
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
                HomeHoverActionsMode = HoverActionsMode.Explicit,
                MoreCommands =
                [
                    new CommandContextItem(_createShortcutCommand)
                    {
                        Title = "Create new shortcut",
                        Icon = new IconInfo("\uE710"),
                        ShowInHoverActions = true,
                        HoverOrder = 0,
                    },
                    new CommandContextItem(new UndoShortcutCommand(ReloadPages))
                    {
                        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: Windows.System.VirtualKey.Z),
                        ShowInHoverActions = false,
                    },
                    new CommandContextItem(new RedoShortcutCommand(ReloadPages))
                    {
                        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: Windows.System.VirtualKey.Y),
                        ShowInHoverActions = false,
                    },
                    new CommandContextItem(new ExportShortcutsCommand())
                    {
                        ShowInHoverActions = false,
                    },
                    new CommandContextItem(new ImportShortcutsCommand(ReloadPages))
                    {
                        ShowInHoverActions = false,
                    },
                    new CommandContextItem(settingsPage)
                    {
                        Icon = new IconInfo("\uE713"),
                        ShowInHoverActions = true,
                        HoverOrder = 10,
                    },
                ],
            },
        ];

        _fallbackPage = new QuickShellFallbackPage(_settingsManager);
        _fallbacks = [new QuickShellFallback(_fallbackPage, _settingsManager)];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;

    private void ReloadPages()
    {
        _page.Reload();
        _fallbackPage.UpdateSearchText(string.Empty, string.Empty);
    }

    public override ICommandItem? GetCommandItem(string id)
    {
        if (string.Equals(id, ImportConflictPage.PageId, StringComparison.Ordinal))
        {
            return new CommandItem(_importConflictPage)
            {
                Title = _importConflictPage.Title,
                Icon = _importConflictPage.Icon,
            };
        }

        if (string.Equals(id, PendingShortcutEditPage.PageId, StringComparison.Ordinal))
        {
            return new CommandItem(_pendingShortcutEditPage)
            {
                Title = _pendingShortcutEditPage.Title,
                Icon = _pendingShortcutEditPage.Icon,
            };
        }

        if (string.Equals(id, ShortcutCommandIds.CreateShortcut, StringComparison.Ordinal))
        {
            return ShortcutListItems.CreateNewShortcut(_createShortcutCommand);
        }

        if (ShortcutCommandIds.TryParseOpen(id, out var openKey))
        {
            var shortcut = ShortcutStore.ResolveForOpenCommand(openKey);
            if (shortcut is null)
            {
                return null;
            }

            return ShortcutListItems.CreateOpen(shortcut, _settingsManager, ReloadPages);
        }

        return base.GetCommandItem(id);
    }

    public override void Dispose()
    {
        _settingsManager.SettingsChanged -= _settingsChangedHandler;
        _page.Dispose();
        _fallbackPage.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
