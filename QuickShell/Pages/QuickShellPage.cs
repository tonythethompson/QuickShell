using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class QuickShellPage : DynamicListPage, IDisposable
{
    private readonly QuickShellSettingsManager _settings;
    private readonly CreateShortcutCommand _createShortcutCommand;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
    private readonly SearchDebouncer _searchDebouncer;
    private IListItem[] _items = [];
    private string _query = string.Empty;
    private bool _hasShownInitialList;

    public QuickShellPage(
        QuickShellSettingsManager settings,
        CreateShortcutCommand createShortcutCommand)
    {
        _settings = settings;
        _createShortcutCommand = createShortcutCommand;
        _discoverGitReposCommand = new OpenDiscoverGitReposCommand(Reload);
        _searchDebouncer = new SearchDebouncer(ApplyQueryDebounced);
        Id = QuickShellNavigation.HomePageId;
        Icon = QuickShellBrandIcons.App;
        Title = QuickShellBrand.DisplayName;
        Name = "Open";
        PlaceholderText = "Search workspaces by name, path, or command...";
        EmptyContent = new CommandItem(_createShortcutCommand)
        {
            Title = "Create your first workspace",
            Subtitle = "Pick a folder and configure terminal launches",
            Icon = new IconInfo("\uE710"),
            MoreCommands =
            [
                new CommandContextItem(_settings.SettingsPage)
                {
                    Title = QuickShellBrand.SettingsTitle,
                    Icon = new IconInfo("\uE713"),
                },
            ],
        };
#if CMDPAL_HOVER_ACTIONS
        HoverActionsMode = HoverActionsMode.Explicit;
        MaxHoverActions = -1;
        HoverActionsVisibility = HoverActionsVisibility.HoverOrSelected;
#endif
        RefreshItems(string.Empty);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var normalized = newSearch ?? string.Empty;

        if (!_hasShownInitialList)
        {
            _hasShownInitialList = true;
            if (!string.IsNullOrEmpty(oldSearch) || !string.IsNullOrEmpty(normalized))
            {
                SetSearchNoUpdate(string.Empty);
            }

            ApplyQuery(string.Empty, immediate: true);
            return;
        }

        if (string.IsNullOrEmpty(oldSearch) && string.IsNullOrEmpty(normalized))
        {
            return;
        }

        ApplyQuery(normalized);
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
        var pinnedInOrder = QuickShellRuntimeServices.Shortcuts.GetShortcuts()
            .Where(s => s.IsPinned)
            .OrderBy(s => s.PinOrder ?? int.MaxValue)
            .ToList();
        var items = new List<IListItem>();
        items.AddRange(QuickShellPageActions.BuildItems(_createShortcutCommand, _discoverGitReposCommand, _settings, Reload));

        if (string.IsNullOrWhiteSpace(query))
        {
            var layout = QuickShellRuntimeServices.Shortcuts.GetLayout();
            items.AddRange(BuildHomeLayoutItems(layout, pinnedInOrder));
        }
        else
        {
            var shortcuts = QuickShellRuntimeServices.Shortcuts.Search(query).ToArray();
            foreach (var shortcut in shortcuts)
            {
                items.Add(BuildShortcutItem(shortcut, pinnedInOrder));
            }

            if (shortcuts.Length == 0)
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = "No matching workspaces",
                    Subtitle = "Try a different search",
                    MoreCommands =
                    [
                        ..ShortcutContextCommands.BuildUndoRedoCommands(Reload),
                        ShortcutContextCommands.CreateSettingsItem(_settings),
                    ],
                });
            }
        }

        _items = items.ToArray();
        RaiseItemsChanged();
    }

    private ListItem BuildShortcutItem(TerminalShortcut shortcut, List<TerminalShortcut> pinnedInOrder)
    {
        var item = ShortcutListItems.CreateOpen(shortcut, _settings, Reload);

        if (ShortcutHealth.NeedsRepair(shortcut))
        {
            return item;
        }

        var moveVisibility = PinnedMoveVisibility.ForShortcut(shortcut, pinnedInOrder);

        var moreCommands = new List<CommandContextItem>(
            ShortcutContextCommands.Build(
                shortcut,
                Reload,
                _settings,
                _createShortcutCommand,
                moveVisibility: moveVisibility));

        item.MoreCommands = moreCommands.ToArray();

        return item;
    }

    private IEnumerable<IListItem> BuildHomeLayoutItems(
        IReadOnlyList<ShortcutLayoutEntry> layout,
        List<TerminalShortcut> pinnedInOrder)
    {
        var allShortcuts = QuickShellRuntimeServices.Shortcuts.GetShortcuts();
        var recents = ShortcutRecents.GetRecentWorkspaces(allShortcuts, _settings.RecentWorkspaceCount);
        var recentIds = recents
            .Select(shortcut => shortcut.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasFavorites = layout.Any(entry =>
            entry.Kind == ShortcutLayoutEntryKind.Shortcut && entry.Shortcut?.IsPinned == true);

        foreach (var item in ShortcutLayoutDisplay.BuildFavoriteItems(
                     layout,
                     shortcut => BuildShortcutItem(shortcut, pinnedInOrder)))
        {
            yield return item;
        }

        if (recents.Count > 0)
        {
            foreach (var item in SectionListItems.InSection(
                         ShortcutRecents.SectionTitle,
                         recents.Select(shortcut => BuildShortcutItem(shortcut, pinnedInOrder))))
            {
                yield return item;
            }
        }

        foreach (var item in ShortcutLayoutDisplay.BuildWorkspaceItems(
                     layout,
                     shortcut => BuildShortcutItem(shortcut, pinnedInOrder),
                     recentIds,
                     showDefaultWorkspacesHeader: hasFavorites))
        {
            yield return item;
        }
    }
}
