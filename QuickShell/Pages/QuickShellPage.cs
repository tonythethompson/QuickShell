using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class QuickShellPage : DynamicListPage, IDisposable
{
    private readonly QuickShellSettingsManager _settings;
    private readonly SearchDebouncer _searchDebouncer;
    private IListItem[] _items = [];
    private string _query = string.Empty;
    private bool _hasShownInitialList;

    public QuickShellPage(QuickShellSettingsManager settings)
    {
        _settings = settings;
        _searchDebouncer = new SearchDebouncer(ApplyQueryDebounced);
        Id = QuickShellNavigation.HomePageId;
        Icon = new IconInfo("\uE756");
        Title = "Quick Shell";
        Name = "Open";
        PlaceholderText = "Search shortcuts by name, path, or command...";
        RefreshItems(string.Empty);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (string.IsNullOrEmpty(oldSearch) && string.IsNullOrEmpty(newSearch))
        {
            _hasShownInitialList = false;
            ApplyQuery(string.Empty, immediate: true);
            return;
        }

        if (!_hasShownInitialList)
        {
            _hasShownInitialList = true;
            SetSearchNoUpdate(string.Empty);
            ApplyQuery(string.Empty, immediate: true);
            return;
        }

        ApplyQuery(newSearch);
    }

    public override IListItem[] GetItems() => _items;

    public void Reload()
    {
        _searchDebouncer.FlushNow();
        RefreshItems(_query);
    }

    public void Dispose() => _searchDebouncer.Dispose();

    private void ApplyQuery(string query, bool immediate = false)
    {
        var normalized = query ?? string.Empty;
        if (string.Equals(_query, normalized, StringComparison.Ordinal) && _items.Length > 0)
        {
            return;
        }

        if (immediate)
        {
            _searchDebouncer.FlushNow();
            ApplyQueryDebounced(normalized);
            return;
        }

        _searchDebouncer.Schedule(normalized);
    }

    private void ApplyQueryDebounced(string normalized)
    {
        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _query = normalized;
        RefreshItems(normalized);
    }

    private void RefreshItems(string query)
    {
        var shortcuts = ShortcutStore.Search(query).ToArray();
        var items = new List<IListItem>(shortcuts.Length + 2);

        foreach (var shortcut in shortcuts)
        {
            items.Add(BuildShortcutItem(shortcut));
        }

        items.Add(new ListItem(new ShortcutFormPage(onSaved: Reload))
        {
            Title = "Create new shortcut",
            Subtitle = "Directory and optional command",
        });
        items.Add(new ListItem(new ReloadShortcutsCommand(Reload))
        {
            Title = "Refresh terminals",
            Subtitle = "Reload shortcuts and rediscover installed terminals",
        });

        if (!string.IsNullOrWhiteSpace(query) && shortcuts.Length == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "No matching shortcuts",
                Subtitle = "Try a different search",
            });
        }

        _items = items.ToArray();
        RaiseItemsChanged();
    }

    private ListItem BuildShortcutItem(TerminalShortcut shortcut)
    {
        var item = new ListItem(new OpenTerminalShortcutCommand(shortcut, _settings))
        {
            Title = shortcut.Name,
            Subtitle = ShortcutDisplay.BuildSubtitle(shortcut),
        };

        var tags = ShortcutDisplay.BuildTags(shortcut);
        if (tags is not null)
        {
            item.Tags = tags;
        }

        var moreCommands = new List<CommandContextItem>(ShortcutContextCommands.Build(shortcut, Reload, _settings));

        if (!shortcut.RunAsAdmin)
        {
            var adminCommand = new OpenTerminalShortcutCommand(shortcut, _settings, runAsAdmin: true);
            moreCommands.Insert(0, ShortcutContextCommands.CreateOpenAsAdminContextItem(adminCommand));
        }

        item.MoreCommands = moreCommands.ToArray();
        return item;
    }
}
