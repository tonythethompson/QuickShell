using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalShortcuts.Commands;
using TerminalShortcuts.Services;

namespace TerminalShortcuts.Pages;

internal sealed partial class TerminalShortcutsPage : DynamicListPage
{
    private IListItem[] _items = [];
    private string _query = string.Empty;

    public TerminalShortcutsPage()
    {
        Icon = new IconInfo("\uE756");
        Title = "Terminal Shortcuts";
        Name = "Open";
        PlaceholderText = "Search shortcuts by name, abbreviation, path, or command...";
        RefreshItems(string.Empty);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        _query = newSearch;
        RefreshItems(newSearch);
    }

    public override IListItem[] GetItems() => _items;

    public void Reload() => RefreshItems(_query);

    private void RefreshItems(string query)
    {
        var shortcuts = ShortcutStore.Search(query).ToArray();
        var items = new List<IListItem>(shortcuts.Length + 2);

        foreach (var shortcut in shortcuts)
        {
            items.Add(new ListItem(new OpenTerminalShortcutCommand(shortcut))
            {
                Title = shortcut.Name,
                Subtitle = BuildSubtitle(shortcut),
            });
        }

        items.Add(new ListItem(new ReloadShortcutsCommand(Reload))
        {
            Title = "Reload shortcuts",
            Subtitle = $"Reload {ShortcutStore.ConfigPath}",
        });

        items.Add(new ListItem(new OpenUrlCommand($"file:///{ShortcutStore.ConfigPath.Replace('\\', '/')}"))
        {
            Title = "Open shortcuts.json",
            Subtitle = ShortcutStore.ConfigPath,
        });

        _items = items.ToArray();
        RaiseItemsChanged();
    }

    private static string BuildSubtitle(Models.TerminalShortcut shortcut)
    {
        var parts = new List<string> { shortcut.Directory };

        if (!string.IsNullOrWhiteSpace(shortcut.Abbreviation))
        {
            parts.Add($"abbr: {shortcut.Abbreviation}");
        }

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            parts.Add($"run: {shortcut.Command}");
        }

        parts.Add($"terminal: {shortcut.Terminal}");
        return string.Join(" | ", parts);
    }
}
