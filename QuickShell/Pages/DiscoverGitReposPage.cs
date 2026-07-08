using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;

namespace QuickShell.Pages;

internal partial class DiscoverGitReposPage : DynamicListPage
{
    public const string PageId = QuickShellDeepLinkIds.DiscoverGitRepos;

    private readonly Action _onReload;
    private readonly object _refreshSync = new();
    private IListItem[] _items = [];
    private string _query = string.Empty;
    private bool _refreshScheduled;

    public DiscoverGitReposPage(Action onReload)
    {
        _onReload = onReload;
        Id = PageId;
        Icon = new IconInfo(ShortcutGlyphs.Discover);
        GitRepoIndex.Invalidate();
        SetOpeningItems();
        ScheduleRefreshItems();
    }

    public override IListItem[] GetItems() => _items;

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var normalized = newSearch ?? string.Empty;
        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _query = normalized;
        ScheduleRefreshItems();
    }

    private void ScheduleRefreshItems()
    {
        lock (_refreshSync)
        {
            if (_refreshScheduled)
            {
                return;
            }

            _refreshScheduled = true;
        }

        SettingsFormHelpers.SchedulePostNavigationRefresh(() =>
        {
            lock (_refreshSync)
            {
                _refreshScheduled = false;
            }

            RefreshItems(_query);
        });
    }

    private void SetOpeningItems()
    {
        _items =
        [
            new ListItem(new NoOpCommand())
            {
                Title = "Scanning for git repositories",
                Subtitle = "Results will appear in a moment.",
                Icon = new IconInfo(ShortcutGlyphs.Discover),
            },
        ];
    }

    private void RefreshItems(string query)
    {
        try
        {
            var shortcuts = QuickShellRuntimeServices.Shortcuts.GetShortcuts();
            var extraRoots = GitRepoSearchRoots.FromShortcuts(shortcuts);
            var discovered = GitRepoIndex.GetAll(extraRoots).ToList();
            var shortcutsByDirectory = DiscoverGitRepoListItems.GroupShortcutsByDirectory(shortcuts);
            var settings = QuickShellRuntimeServices.Settings;

            if (!string.IsNullOrWhiteSpace(query))
            {
                discovered = discovered
                    .Where(candidate =>
                        candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || candidate.Directory.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (candidate.RemoteUrl?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        || candidate.Classification.Labels.Any(label => label.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var items = DiscoverGitRepoListItems
                .BuildSectionedItems(discovered, _onReload, shortcutsByDirectory, settings)
                .ToList();

            if (items.Count == 0)
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = string.IsNullOrWhiteSpace(query)
                        ? Strings.Discover_NoReposFound
                        : Strings.Discover_NoMatchingRepos,
                    Subtitle = Strings.Discover_TrySearching,
                    Icon = new IconInfo("\uE946"),
                });
            }

            _items = items.ToArray();
            RaiseItemsChanged();
        }
        catch
        {
            _items =
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Git discovery failed",
                    Subtitle = "Open settings and copy diagnostics if this keeps happening.",
                    Icon = new IconInfo(ShortcutGlyphs.IncidentTriangle),
                },
            ];
            RaiseItemsChanged();
        }
    }
}
