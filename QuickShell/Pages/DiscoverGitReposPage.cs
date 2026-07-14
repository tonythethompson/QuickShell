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
    private bool _hasShownInitialList;
    private bool _awaitingGitRefresh;
    private bool _needsInitialRefresh = true;

    private const string ScanningTitle = "Scanning for git repositories";

    public DiscoverGitReposPage(Action onReload)
    {
        _onReload = onReload;
        Id = PageId;
        Icon = new IconInfo(ShortcutGlyphs.Discover);
        SetOpeningItems();
    }

    public override IListItem[] GetItems()
    {
        ExtensionCallbackQueue.Drain();

        if (_needsInitialRefresh && !_refreshScheduled && !_awaitingGitRefresh)
        {
            ScheduleRefreshItems();
        }
        else if ((_awaitingGitRefresh || IsShowingScanningPlaceholder())
                 && !GitRepoIndex.IsRefreshInFlight
                 && !_refreshScheduled)
        {
            ScheduleRefreshItems();
        }

        return _items;
    }

    private bool IsShowingScanningPlaceholder() =>
        _items.Length == 1
        && _items[0] is ListItem { Title: ScanningTitle };

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var normalized = newSearch ?? string.Empty;

        if (!_hasShownInitialList)
        {
            _hasShownInitialList = true;
            ScheduleRefreshItems();
            return;
        }

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
                Title = ScanningTitle,
                Subtitle = "Results will appear in a moment.",
                Icon = new IconInfo(ShortcutGlyphs.Discover),
            },
        ];
    }

    private void RefreshItems(string query)
    {
        // #region agent log
        var refreshStartedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        AgentDebugLog.Write(
            "DiscoverGitReposPage.cs:RefreshItems",
            "start",
            new { queryLength = query?.Length ?? 0, refreshInFlight = GitRepoIndex.IsRefreshInFlight },
            runId: "post-fix",
            hypothesisId: "H");
        // #endregion

        try
        {
            var shortcuts = QuickShellServices.Current.Shortcuts.GetShortcuts();
            var extraRoots = GitRepoSearchRoots.FromShortcuts(shortcuts);
            var discovered = GitRepoIndex.GetAll(extraRoots).ToList();
            if (discovered.Count == 0
                && GitRepoIndex.TryRunAfterNextRefreshIfInFlight(() => _awaitingGitRefresh = true))
            {
                _awaitingGitRefresh = true;
                // #region agent log
                AgentDebugLog.Write(
                    "DiscoverGitReposPage.cs:RefreshItems",
                    "waiting-for-scan",
                    new { elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - refreshStartedUtc },
                    runId: "post-fix",
                    hypothesisId: "H");
                // #endregion
                return;
            }

            _awaitingGitRefresh = false;

            var shortcutsByDirectory = DiscoverGitRepoListItems.GroupShortcutsByDirectory(shortcuts);
            var settings = QuickShellServices.Current.Settings;

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
            _needsInitialRefresh = false;
            _awaitingGitRefresh = false;
            RaiseItemsChanged();

            // #region agent log
            AgentDebugLog.Write(
                "DiscoverGitReposPage.cs:RefreshItems",
                "complete",
                new
                {
                    itemCount = _items.Length,
                    discoveredCount = discovered.Count,
                    elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - refreshStartedUtc,
                },
                runId: "post-fix",
                hypothesisId: "H");
            // #endregion
        }
        catch (Exception ex)
        {
            // #region agent log
            AgentDebugLog.WriteException(
                "DiscoverGitReposPage.cs:RefreshItems",
                ex,
                hypothesisId: "H",
                runId: "post-fix");
            // #endregion
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
