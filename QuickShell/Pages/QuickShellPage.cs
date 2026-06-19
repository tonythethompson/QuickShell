using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class QuickShellPage : DynamicListPage
{
    private IListItem[] _items = [];
    private string _query = string.Empty;

    public QuickShellPage()
    {
        Icon = new IconInfo("\uE756");
        Title = "Quick Shell";
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
            var item = new ListItem(new OpenTerminalShortcutCommand(shortcut))
            {
                Title = shortcut.Name,
                Subtitle = BuildSubtitle(shortcut),
            };

            if (!shortcut.RunAsAdmin)
            {
                item.MoreCommands =
                [
                    new CommandContextItem(new OpenTerminalShortcutCommand(shortcut, runAsAdmin: true))
                    {
                        Title = "Open as administrator",
                    },
                ];
            }

            items.Add(item);
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

        if (shortcut.RunAsAdmin)
        {
            parts.Add("admin");
        }

        return string.Join(" | ", parts);
    }
}
