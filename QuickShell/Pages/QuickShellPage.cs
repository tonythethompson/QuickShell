using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Pages;

internal sealed partial class QuickShellPage : DynamicListPage, IDisposable
{
    private readonly QuickShellSettingsManager _settings;
    private readonly CreateShortcutCommand _createShortcutCommand;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
    private readonly SearchDebouncer _searchDebouncer;
    private readonly object _reloadSync = new();
    private readonly object _refreshSync = new();
    private IListItem[] _items = [];
    private string _query = string.Empty;
    private bool _hasShownInitialList;
    private bool _reloadScheduled;
    private bool _refreshInProgress;
    private bool _refreshQueued;
    private bool _needsInitialRefresh = true;
    private bool _disposed;
    private int _iconUpgradeGeneration;
    private List<(ListItem Item, TerminalShortcut Shortcut)> _iconUpgradeTargets = [];
    private IReadOnlyList<(ListItem Item, string Icon)>? _pendingIconApplies;

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
        PlaceholderText = Strings.SearchPlaceholder;
        EmptyContent = new CommandItem(_createShortcutCommand)
        {
            Title = Strings.EmptyState_Title,
            Subtitle = Strings.EmptyState_Subtitle,
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
        SetOpeningItems();
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

            SchedulePostNavigationReload();
            return;
        }

        if (string.IsNullOrEmpty(oldSearch) && string.IsNullOrEmpty(normalized))
        {
            return;
        }

        ApplyQuery(normalized);
    }

    public override IListItem[] GetItems()
    {
        ExtensionCallbackQueue.Drain();
        ApplyPendingProfileIcons();

        if (!_disposed && _needsInitialRefresh && !_refreshInProgress && !_reloadScheduled)
        {
            SchedulePostNavigationReload();
        }

        return _items;
    }

    public void Reload()
    {
        SchedulePostNavigationReload();
    }

    public void Dispose()
    {
        _disposed = true;
        _searchDebouncer.Dispose();
    }

    private void ReloadNow()
    {
        _searchDebouncer.FlushNow();
        RefreshItems(_query);
    }

    private void ApplyQuery(string query, bool immediate = false)
    {
        if (_disposed)
        {
            return;
        }

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
        if (_disposed)
        {
            return;
        }

        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _query = normalized;
        RefreshItems(normalized);
    }

    private void SchedulePostNavigationReload()
    {
        lock (_reloadSync)
        {
            if (_reloadScheduled || _disposed)
            {
                return;
            }

            _reloadScheduled = true;
        }

        SettingsFormHelpers.SchedulePostNavigationRefresh(() =>
        {
            lock (_reloadSync)
            {
                _reloadScheduled = false;
            }

            if (_disposed)
            {
                return;
            }

            ReloadNow();
        });
    }

    private void SetOpeningItems()
    {
        var items = new List<IListItem>();
        items.AddRange(QuickShellPageActions.BuildItems(_createShortcutCommand, _discoverGitReposCommand, _settings, Reload));
        items.Add(CreateStatusItem("Loading workspaces", "Workspace list will appear in a moment."));
        _items = items.ToArray();
    }

    private static ListItem CreateStatusItem(string title, string subtitle) =>
        new(new NoOpCommand())
        {
            Title = title,
            Subtitle = subtitle,
            Icon = QuickShellBrandIcons.App,
        };

    private void RefreshItems(string query)
    {
        if (_disposed)
        {
            return;
        }

        lock (_refreshSync)
        {
            if (_refreshInProgress)
            {
                _refreshQueued = true;
                _query = query ?? string.Empty;
                return;
            }

            _refreshInProgress = true;
        }

        // #region agent log
        var refreshStartedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        AgentDebugLog.Write(
            "QuickShellPage.cs:RefreshItems",
            "start",
            new { queryLength = query?.Length ?? 0, startedUtc = refreshStartedUtc },
            runId: "post-fix",
            hypothesisId: "D");
        // #endregion

        try
        {
            // #region agent log
            var phaseLayoutStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // #endregion
            _iconUpgradeTargets = [];
            var pinnedInOrder = QuickShellServices.Current.Shortcuts.GetShortcuts()
            .Where(s => s.IsPinned)
            .OrderBy(s => s.PinOrder ?? int.MaxValue)
            .ToList();
        var items = new List<IListItem>();
        items.AddRange(QuickShellPageActions.BuildItems(_createShortcutCommand, _discoverGitReposCommand, _settings, Reload));

        if (string.IsNullOrWhiteSpace(query))
        {
            var layout = QuickShellServices.Current.Shortcuts.GetLayout();
            items.AddRange(BuildHomeLayoutItems(layout, pinnedInOrder));
        }
        else
        {
            var taskActions = QuickShellServices.Current.Shortcuts.SearchTaskActions(query).ToArray();
            foreach (var action in taskActions)
            {
                items.Add(ShortcutTaskActionListItems.Create(action, _settings, Reload, _createShortcutCommand));
            }

            var shortcuts = QuickShellServices.Current.Shortcuts.Search(query).ToArray();
            foreach (var shortcut in shortcuts)
            {
                items.Add(BuildShortcutItem(shortcut, pinnedInOrder));
            }

            if (taskActions.Length == 0 && shortcuts.Length == 0)
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = Strings.NoMatch_Title,
                    Subtitle = Strings.NoMatch_Subtitle,
                    MoreCommands =
                    [
                        ..ShortcutContextCommands.BuildUndoRedoCommands(Reload),
                        ShortcutContextCommands.CreateSettingsItem(_settings),
                    ],
                });
            }
        }

        _items = items.ToArray();
        _needsInitialRefresh = false;
        RaiseItemsChanged();

        var upgradeTargets = _iconUpgradeTargets;
        var upgradeGeneration = Interlocked.Increment(ref _iconUpgradeGeneration);
        ScheduleProfileIconUpgrade(upgradeTargets, upgradeGeneration);

        // #region agent log
        AgentDebugLog.Write(
            "QuickShellPage.cs:RefreshItems",
            "complete",
            new
            {
                itemCount = _items.Length,
                elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - refreshStartedUtc,
                buildElapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - phaseLayoutStart,
                upgradeTargets = upgradeTargets.Count,
                hypothesis = "F-glyphs",
            },
            runId: "post-fix",
            hypothesisId: "F");
        // #endregion
        }
        catch (Exception ex)
        {
            // #region agent log
            AgentDebugLog.WriteException("QuickShellPage.cs:RefreshItems", ex, hypothesisId: "D", runId: "post-fix");
            // #endregion
            throw;
        }
        finally
        {
            string? queuedQuery = null;
            var shouldRefreshAgain = false;
            lock (_refreshSync)
            {
                _refreshInProgress = false;
                if (_refreshQueued)
                {
                    _refreshQueued = false;
                    queuedQuery = _query;
                    shouldRefreshAgain = true;
                }
            }

            if (shouldRefreshAgain && queuedQuery is not null)
            {
                RefreshItems(queuedQuery);
            }
        }
    }

    private ListItem BuildShortcutItem(TerminalShortcut shortcut, List<TerminalShortcut> _)
    {
        var item = ShortcutListItems.CreateOpen(shortcut, _settings, Reload, _createShortcutCommand);
        _iconUpgradeTargets.Add((item, shortcut));
        return item;
    }

    private IEnumerable<IListItem> BuildHomeLayoutItems(
        IReadOnlyList<ShortcutLayoutEntry> layout,
        List<TerminalShortcut> pinnedInOrder)
    {
        var allShortcuts = QuickShellServices.Current.Shortcuts.GetShortcuts();
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

    private void ScheduleProfileIconUpgrade(
        IReadOnlyList<(ListItem Item, TerminalShortcut Shortcut)> targets,
        int generation)
    {
        if (targets.Count == 0 || _disposed)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            // #region agent log
            var startedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            AgentDebugLog.Write(
                "QuickShellPage.cs:IconUpgrade",
                "start",
                new { targetCount = targets.Count, generation },
                runId: "post-fix",
                hypothesisId: "G");
            // #endregion

            try
            {
                TerminalListIconCache.PrewarmProfiles();

                var upgrades = new List<(ListItem Item, string Icon)>();
                foreach (var (item, shortcut) in targets)
                {
                    if (_disposed || generation != Interlocked.Add(ref _iconUpgradeGeneration, 0))
                    {
                        return;
                    }

                    var upgraded = TerminalListIconCache.TryResolveUpgradedListIcon(shortcut);
                    if (string.IsNullOrWhiteSpace(upgraded))
                    {
                        continue;
                    }

                    var fast = ShortcutHealth.GetListGlyph(shortcut, needsRepair: false);
                    if (string.Equals(upgraded, fast, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    upgrades.Add((item, upgraded));
                }

                if (upgrades.Count == 0)
                {
                    // #region agent log
                    AgentDebugLog.Write(
                        "QuickShellPage.cs:IconUpgrade",
                        "complete-noop",
                        new
                        {
                            generation,
                            elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedUtc,
                        },
                        runId: "post-fix",
                        hypothesisId: "G");
                    // #endregion
                    return;
                }

                ExtensionCallbackQueue.Enqueue(() =>
                {
                    if (_disposed || generation != Interlocked.Add(ref _iconUpgradeGeneration, 0))
                    {
                        return;
                    }

                    _pendingIconApplies = upgrades;
                    ApplyPendingProfileIcons();
                    RaiseItemsChanged();

                    // #region agent log
                    AgentDebugLog.Write(
                        "QuickShellPage.cs:IconUpgrade",
                        "applied",
                        new
                        {
                            upgradedCount = upgrades.Count,
                            generation,
                            listIconPixels = TerminalListIconCache.ListIconPixels,
                            elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedUtc,
                        },
                        runId: "post-fix",
                        hypothesisId: "G");
                    // #endregion
                });
            }
            catch (Exception ex)
            {
                // #region agent log
                AgentDebugLog.WriteException(
                    "QuickShellPage.cs:IconUpgrade",
                    ex,
                    hypothesisId: "G",
                    runId: "post-fix");
                // #endregion
            }
        });
    }

    private void ApplyPendingProfileIcons()
    {
        IReadOnlyList<(ListItem Item, string Icon)>? pending =
            Interlocked.Exchange(ref _pendingIconApplies, null);
        if (pending is null || pending.Count == 0)
        {
            return;
        }

        foreach (var (item, icon) in pending)
        {
            item.Icon = new IconInfo(icon);
        }
    }
}
