using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class QuickShellFallbackPage : DynamicListPage, IDisposable
{
    private readonly QuickShellSettingsManager _settings;
    private readonly SearchDebouncer _searchDebouncer;
    private IListItem[] _items = [];
    private string _query = string.Empty;

    public QuickShellFallbackPage(QuickShellSettingsManager settings)
    {
        _settings = settings;
        _searchDebouncer = new SearchDebouncer(ApplyQueryDebounced);
        Icon = new IconInfo("\uE756");
        Title = "Saved shortcut";
        Name = "Open";
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var normalized = newSearch ?? string.Empty;
        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _searchDebouncer.Schedule(normalized);
    }

    public override IListItem[] GetItems() => _items;

    public void Dispose() => _searchDebouncer.Dispose();

    private void ApplyQueryDebounced(string normalized)
    {
        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _query = normalized;
        RefreshItems();
    }

    private void RefreshItems()
    {
        var shortcuts = QuickShellRuntimeServices.Shortcuts.SearchForRootPalette(_query).ToArray();
        _items = shortcuts.Length == 0
            ? []
            : shortcuts.Select(BuildShortcutItem).Cast<IListItem>().ToArray();

        RaiseItemsChanged();
    }

    private void Reload()
    {
        _searchDebouncer.FlushNow();
        RefreshItems();
    }

    private ListItem BuildShortcutItem(TerminalShortcut shortcut)
    {
        var item = ShortcutListItems.CreateOpen(shortcut, _settings);
        item.Subtitle = ShortcutDisplay.BuildDirectorySubtitle(shortcut);

        var moreCommands = new List<CommandContextItem>(ShortcutContextCommands.Build(shortcut, Reload, _settings, includeEdit: false));

        item.MoreCommands = moreCommands.ToArray();
        return item;
    }
}
