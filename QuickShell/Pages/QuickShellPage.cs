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
    private readonly IQuickShellServices _services;
    private readonly QuickShellSettingsManager _settings;
    private readonly CreateShortcutCommand _createShortcutCommand;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
    private readonly SearchDebouncer _searchDebouncer;
    private readonly object _refreshSync = new();
    /// <summary>
    /// Unpinned rows are expensive to rebuild (full context menus). Reuse them across
    /// favorite moves so only favorites (~few rows) are recreated.
    /// </summary>
    private readonly Dictionary<string, ListItem> _unpinnedItemCache =
        new(StringComparer.OrdinalIgnoreCase);
    private IListItem[] _items = [];
    private string _query = string.Empty;
    private bool _hasShownInitialList;
    private bool _hasLoadedWorkspaces;
    private bool _workspacesStale;
    private bool _refreshInProgress;
    private bool _refreshQueued;
    private bool _forceQueryRefresh;
    private bool _disposed;

    public QuickShellPage(
        QuickShellSettingsManager settings,
        CreateShortcutCommand createShortcutCommand,
        IQuickShellServices? services = null)
    {
        _services = services ?? throw new InvalidOperationException("IQuickShellServices is required.");
        _settings = settings;
        _settings.Services = _services;
        _createShortcutCommand = createShortcutCommand;
        _discoverGitReposCommand = new OpenDiscoverGitReposCommand(Reload, _services);
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
        // Do NOT load workspaces here — provider construction is COM activation.
        // Host FetchItems calls GetItems on a background thread; load there instead.
        SetOpeningItems();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var normalized = ListSearchQuery.Normalize(newSearch);

        if (!_hasShownInitialList)
        {
            _hasShownInitialList = true;
            // Do not SetSearchNoUpdate("") — that desyncs extension SearchText from the host box.
            if (ListSearchQuery.HasChanged(_query, normalized))
            {
                _query = normalized;
            }

            // First paint is owned by GetItems (sync build on the fetch thread).
            // On re-open, the host can restore matching text while _items still holds the
            // previous unfiltered snapshot. Force one apply so the restored text wins.
            if (_hasLoadedWorkspaces && ListSearchQuery.HasChanged(string.Empty, normalized))
            {
                ApplyQuery(normalized, immediate: true, force: true);
            }

            return;
        }

        // Compare against the query we last applied, not host oldSearch.
        if (!ListSearchQuery.HasChanged(_query, normalized))
        {
            // Replace a queued different query when the host text returns to the
            // query already applied; otherwise the stale debounce wins later.
            _searchDebouncer.Schedule(normalized);
            return;
        }

        ApplyQuery(normalized);
    }

    public override IListItem[] GetItems()
    {
        ExtensionCallbackQueue.Drain();

        // Host FetchItems awaits this return. Build on this COM/fetch thread so the
        // first result is the real workspace list. ThreadPool + RaiseItemsChanged left
        // the UI stuck on "Loading workspaces" (ItemsChanged from ThreadPool is dropped).
        // Same path handles post-form reloads: save only marks stale so SubmitForm can
        // return GoBack immediately; the host re-fetches here after navigation.
        if (!_hasLoadedWorkspaces || _workspacesStale)
        {
            _hasShownInitialList = true;
            IsLoading = true;
            try
            {
                RefreshItems(_query, notifyHost: false);
            }
            finally
            {
                IsLoading = false;
            }
        }

        return _items;
    }

    /// <summary>
    /// Rebuild the home list now so CmdPal repaints immediately.
    /// </summary>
    public void Reload()
    {
        Reload(preserveUnpinnedItemCache: false);
    }

    private void Reload(bool preserveUnpinnedItemCache)
    {
        if (_disposed)
        {
            return;
        }

        if (!preserveUnpinnedItemCache)
        {
            _unpinnedItemCache.Clear();
        }

        _searchDebouncer.FlushNow();
        RefreshItems(_query, notifyHost: true);
    }

    /// <summary>
    /// Mark the list dirty without building rows on the caller thread. Use when leaving a
    /// form/settings page (SubmitForm runs on Task.Run; a full rebuild there freezes GoBack).
    /// The host re-fetches via GetItems after navigation.
    /// </summary>
    public void InvalidateWorkspaces()
    {
        if (_disposed)
        {
            return;
        }

        // Edits may change titles/subtitles — drop cached rows so next paint is fresh.
        _unpinnedItemCache.Clear();
        _workspacesStale = true;
        try
        {
            RaiseItemsChanged();
        }
        catch
        {
            // Nested ItemsChanged during form SubmitForm can throw 0x800706BA.
            // Stale flag remains; GetItems rebuilds when the home page is shown again.
        }
    }

    private void PruneUnpinnedItemCache(IReadOnlyList<TerminalShortcut> shortcuts)
    {
        var liveUnpinnedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < shortcuts.Count; i++)
        {
            var shortcut = shortcuts[i];
            if (!shortcut.IsPinned && !string.IsNullOrWhiteSpace(shortcut.Id))
            {
                liveUnpinnedIds.Add(shortcut.Id);
            }
        }

        if (_unpinnedItemCache.Count == 0)
        {
            return;
        }

        List<string>? dead = null;
        foreach (var id in _unpinnedItemCache.Keys)
        {
            if (!liveUnpinnedIds.Contains(id))
            {
                dead ??= [];
                dead.Add(id);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var id in dead)
        {
            _unpinnedItemCache.Remove(id);
        }
    }

    private static List<TerminalShortcut> BuildPinnedInOrder(IReadOnlyList<TerminalShortcut> shortcuts)
    {
        var pinned = new List<TerminalShortcut>();
        for (var i = 0; i < shortcuts.Count; i++)
        {
            if (shortcuts[i].IsPinned)
            {
                pinned.Add(shortcuts[i]);
            }
        }

        if (pinned.Count <= 1)
        {
            return pinned;
        }

        pinned.Sort(static (a, b) =>
        {
            var order = (a.PinOrder ?? int.MaxValue).CompareTo(b.PinOrder ?? int.MaxValue);
            return order != 0
                ? order
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return pinned;
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_refreshSync)
        {
            Monitor.PulseAll(_refreshSync);
        }

        _searchDebouncer.Dispose();
    }

    private void SetOpeningItems()
    {
        var items = new List<IListItem>();
        items.AddRange(QuickShellPageActions.BuildItems(_createShortcutCommand, _discoverGitReposCommand, _settings, Reload, _services));
        items.Add(CreateStatusItem("Loading workspaces", "Workspace list will appear in a moment."));
        _items = items.ToArray();
        _hasLoadedWorkspaces = false;
    }

    private void ApplyQuery(string query, bool immediate = false, bool force = false)
    {
        if (_disposed)
        {
            return;
        }

        var normalized = ListSearchQuery.Normalize(query);
        if (!force && !ListSearchQuery.HasChanged(_query, normalized))
        {
            return;
        }

        if (immediate)
        {
            _forceQueryRefresh |= force;
            _searchDebouncer.Schedule(normalized);
            _searchDebouncer.FlushNow();
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

        var next = ListSearchQuery.Normalize(normalized);
        var forceRefresh = _forceQueryRefresh;
        _forceQueryRefresh = false;
        if (!forceRefresh && !ListSearchQuery.HasChanged(_query, next))
        {
            return;
        }

        _query = next;
        RefreshItems(next, notifyHost: true);
    }

    private static ListItem CreateStatusItem(string title, string subtitle) =>
        new(new NoOpCommand())
        {
            Title = title,
            Subtitle = subtitle,
            Icon = QuickShellBrandIcons.App,
        };

    private void RefreshItems(string query, bool notifyHost = true)
    {
        if (_disposed)
        {
            return;
        }

        var normalizedQuery = ListSearchQuery.Normalize(query);

        // Wait for any in-flight refresh instead of returning early with a stale
        // "Loading workspaces" list (that race left first open stuck forever).
        lock (_refreshSync)
        {
            while (_refreshInProgress && !_disposed)
            {
                Monitor.Wait(_refreshSync, 100);
            }

            if (_disposed)
            {
                return;
            }

            if (_hasLoadedWorkspaces
                && !_workspacesStale
                && !notifyHost
                && string.Equals(_query, normalizedQuery, StringComparison.Ordinal))
            {
                // First GetItems after another thread already finished the load.
                return;
            }

            _refreshInProgress = true;
            _query = normalizedQuery;
        }

        // #region agent log
        var refreshStartedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SupportDiagnostics.Write(
            "QuickShellPage.cs:RefreshItems",
            "start",
            new
            {
                queryLength = normalizedQuery.Length,
                notifyHost,
                startedUtc = refreshStartedUtc,
                unpinnedCache = _unpinnedItemCache.Count,
            },
            runId: "post-fix",
            hypothesisId: "D");
        // #endregion

        try
        {
            // One repository snapshot for the whole refresh — helpers must not re-query.
            var allShortcuts = _services.Shortcuts.GetShortcuts();
            PruneUnpinnedItemCache(allShortcuts);
            var pinnedInOrder = BuildPinnedInOrder(allShortcuts);

            var items = new List<IListItem>(capacity: Math.Max(16, allShortcuts.Count + 8));
            foreach (var action in QuickShellPageActions.BuildItems(
                         _createShortcutCommand,
                         _discoverGitReposCommand,
                         _settings,
                         Reload,
                         _services))
            {
                items.Add(action);
            }

            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                var layout = _services.Shortcuts.GetLayout();
                foreach (var item in BuildHomeLayoutItems(layout, allShortcuts, pinnedInOrder))
                {
                    items.Add(item);
                }
            }
            else
            {
                // Search results: do not reuse home-cache rows (different ordering / set).
                _unpinnedItemCache.Clear();
                var anyMatch = false;
                foreach (var taskAction in _services.Shortcuts.SearchTaskActions(normalizedQuery))
                {
                    anyMatch = true;
                    items.Add(ShortcutTaskActionListItems.Create(
                        taskAction,
                        _settings,
                        Reload,
                        _createShortcutCommand,
                        services: _services));
                }

                foreach (var shortcut in _services.Shortcuts.Search(normalizedQuery))
                {
                    anyMatch = true;
                    items.Add(BuildShortcutItem(shortcut, pinnedInOrder));
                }

                if (!anyMatch)
                {
                    items.Add(new ListItem(new NoOpCommand())
                    {
                        Title = Strings.NoMatch_Title,
                        Subtitle = Strings.NoMatch_Subtitle,
                        MoreCommands =
                        [
                            ..ShortcutContextCommands.BuildUndoRedoCommands(Reload, _services),
                            ShortcutContextCommands.CreateSettingsItem(_settings, _services),
                        ],
                    });
                }
            }

            // Materialize once at the UI boundary.
            _items = items.ToArray();
            _hasLoadedWorkspaces = true;
            _workspacesStale = false;
            if (notifyHost)
            {
                RaiseItemsChanged();
            }

            // #region agent log
            SupportDiagnostics.Write(
                "QuickShellPage.cs:RefreshItems",
                "complete",
                new
                {
                    itemCount = _items.Length,
                    notifyHost,
                    unpinnedCache = _unpinnedItemCache.Count,
                    elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - refreshStartedUtc,
                },
                runId: "post-form",
                hypothesisId: "D");
            // #endregion
        }
        catch (Exception ex)
        {
            // #region agent log
            SupportDiagnostics.WriteException("QuickShellPage.cs:RefreshItems", ex, hypothesisId: "D", runId: "post-form");
            // #endregion

            var items = new List<IListItem>();
            items.AddRange(QuickShellPageActions.BuildItems(_createShortcutCommand, _discoverGitReposCommand, _settings, Reload, _services));
            items.Add(CreateStatusItem(
                "Could not load workspaces",
                string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message));
            _items = items.ToArray();
            _hasLoadedWorkspaces = true;
            _workspacesStale = false;
            if (notifyHost)
            {
                try
                {
                    RaiseItemsChanged();
                }
                catch
                {
                    // Host may reject nested notifications.
                }
            }
        }
        finally
        {
            string? queuedQuery = null;
            lock (_refreshSync)
            {
                _refreshInProgress = false;
                if (_refreshQueued)
                {
                    _refreshQueued = false;
                    queuedQuery = _query;
                }

                Monitor.PulseAll(_refreshSync);
            }

            if (queuedQuery is not null)
            {
                RefreshItems(queuedQuery, notifyHost: true);
            }
        }
    }

    private ListItem BuildShortcutItem(
        TerminalShortcut shortcut,
        List<TerminalShortcut> pinnedInOrder)
    {
        // Favorites always rebuild (move visibility depends on pin order among favorites).
        // Unpinned rows reuse cached ListItems when reordering favorites, avoiding ~40 menu rebuilds.
        if (!shortcut.IsPinned
            && !string.IsNullOrWhiteSpace(shortcut.Id)
            && _unpinnedItemCache.TryGetValue(shortcut.Id, out var cached))
        {
            return cached;
        }

        var item = ShortcutListItems.CreateOpen(
            shortcut,
            _settings,
            Reload,
            _createShortcutCommand,
            PinnedMoveVisibility.ForShortcut(shortcut, pinnedInOrder),
            onFavoritesReordered: () => Reload(preserveUnpinnedItemCache: true),
            useHomePinContextMenu: true,
            services: _services);

        ScheduleProfileIconUpgrade(shortcut, item);

        if (!string.IsNullOrWhiteSpace(shortcut.Id))
        {
            if (shortcut.IsPinned)
            {
                _unpinnedItemCache.Remove(shortcut.Id);
            }
            else
            {
                _unpinnedItemCache[shortcut.Id] = item;
            }
        }

        return item;
    }

    private void ScheduleProfileIconUpgrade(TerminalShortcut shortcut, ListItem item)
    {
        if (shortcut.RunAsAdmin || ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            TerminalListIconCache.PrewarmProfiles();
            var icon = TerminalListIconCache.TryResolveUpgradedListIcon(shortcut);
            if (string.IsNullOrWhiteSpace(icon))
            {
                return;
            }

            ExtensionCallbackQueue.Enqueue(() =>
            {
                if (_disposed)
                {
                    return;
                }

                item.Icon = new IconInfo(icon);
            });
        });
    }

    private IEnumerable<IListItem> BuildHomeLayoutItems(
        IReadOnlyList<ShortcutLayoutEntry> layout,
        IReadOnlyList<TerminalShortcut> allShortcuts,
        IReadOnlyList<TerminalShortcut> pinnedInOrder)
    {
        var pinnedList = pinnedInOrder.ToList();
        var recents = ShortcutRecents.GetRecentWorkspaces(allShortcuts, _settings.RecentWorkspaceCount);
        var recentIds = recents.Count == 0
            ? null
            : recents.Select(shortcut => shortcut.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in ShortcutLayoutDisplay.BuildFavoriteItems(
                     layout,
                     shortcut => BuildShortcutItem(shortcut, pinnedList),
                     pinnedList))
        {
            yield return item;
        }

        if (recents.Count > 0)
        {
            foreach (var item in SectionListItems.InSection(
                         ShortcutRecents.SectionTitle,
                         recents.Select(shortcut => BuildShortcutItem(shortcut, pinnedList))))
            {
                yield return item;
            }
        }

        foreach (var item in ShortcutLayoutDisplay.BuildWorkspaceItems(
                     layout,
                     shortcut => BuildShortcutItem(shortcut, pinnedList),
                     recentIds,
                     showDefaultWorkspacesHeader: pinnedList.Count > 0))
        {
            yield return item;
        }
    }
}
