using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Pages;

internal sealed partial class QuickShellPage : DynamicListPage, IDisposable
{
    private readonly QuickShellPageContext _context;
    private readonly IQuickShellServices _services;
    private readonly QuickShellSettingsManager _settings;
    private readonly CreateShortcutCommand _createShortcutCommand;
    private readonly SearchDebouncer _searchDebouncer;
    private readonly object _refreshSync = new();
    /// <summary>
    /// Unpinned rows are expensive to rebuild (full context menus). Reuse them across
    /// favorite moves so only favorites (~few rows) are recreated.
    /// </summary>
    private readonly Dictionary<string, ListItem> _unpinnedItemCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _directoryRepairStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _directoryRepairChecks =
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

    private IListItem[]? _cachedPageActions;
    private bool _cachedPageActionsCanUndo;
    private bool _cachedPageActionsCanRedo;

    public QuickShellPage(QuickShellPageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _services = context.Services;
        _settings = context.Settings;
        _createShortcutCommand = context.CreateShortcut;
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
        _services.CallbackQueue.Drain();

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
        // Drop cached directory-repair state so a stale probe result
        // (e.g. an offline drive that has come back online, or a folder
        // that has since been deleted) does not freeze the home list.
        _directoryRepairStates.Clear();
        _directoryRepairChecks.Clear();
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
        // Drop directory-repair caches too: a renamed/relocated folder
        // should be re-probed under its new key on the next paint.
        _directoryRepairStates.Clear();
        _directoryRepairChecks.Clear();
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
        items.AddRange(QuickShellPageActions.BuildItems(_context));
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
            using (StartupPerformanceTrace.Measure("CmdPal home list refresh"))
            {
            // One repository snapshot for the whole refresh — helpers must not re-query.
            var snapshot = _services.Shortcuts.GetSnapshot();
            var allShortcuts = snapshot.Shortcuts;
            PruneUnpinnedItemCache(allShortcuts);
            var pinnedInOrder = BuildPinnedInOrder(allShortcuts);

            var items = new List<IListItem>(capacity: Math.Max(16, allShortcuts.Count + 8));
            foreach (var action in GetOrBuildPageActions(snapshot.CanUndo, snapshot.CanRedo))
            {
                items.Add(action);
            }

            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                foreach (var item in BuildHomeLayoutItems(snapshot.Layout, allShortcuts, pinnedInOrder))
                {
                    items.Add(item);
                }
            }
            else
            {
                // Search results: do not reuse home-cache rows (different ordering / set).
                _unpinnedItemCache.Clear();
                var anyMatch = false;
                foreach (var taskAction in snapshot.SearchTaskActions(normalizedQuery))
                {
                    anyMatch = true;
                    items.Add(ShortcutTaskActionListItems.Create(_context, taskAction, Reload));
                }

                foreach (var shortcut in snapshot.Search(normalizedQuery))
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
                            ..ShortcutContextCommands.BuildUndoRedoCommands(_context.Services, Reload),
                            ShortcutContextCommands.CreateSettingsItem(_context.Services),
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
        }
        catch (Exception ex)
        {
            // #region agent log
            SupportDiagnostics.WriteException("QuickShellPage.cs:RefreshItems", ex, hypothesisId: "D", runId: "post-form");
            // #endregion

            var items = new List<IListItem>();
            items.AddRange(GetOrBuildPageActions(_cachedPageActionsCanUndo, _cachedPageActionsCanRedo));
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

    private IListItem[] GetOrBuildPageActions(bool canUndo, bool canRedo)
    {
        if (_cachedPageActions is not null
            && _cachedPageActionsCanUndo == canUndo
            && _cachedPageActionsCanRedo == canRedo)
        {
            return _cachedPageActions;
        }

        _cachedPageActions = QuickShellPageActions.BuildItems(_context).ToArray();
        _cachedPageActionsCanUndo = canUndo;
        _cachedPageActionsCanRedo = canRedo;
        return _cachedPageActions;
    }

    private ListItem BuildShortcutItem(
        TerminalShortcut shortcut,
        List<TerminalShortcut> pinnedInOrder)
    {
        var needsRepair = RequiresHomeRepair(shortcut);
        // Favorites always rebuild (move visibility depends on pin order among favorites).
        // Unpinned rows reuse cached ListItems when reordering favorites, avoiding ~40 menu rebuilds.
        if (!shortcut.IsPinned
            && !string.IsNullOrWhiteSpace(shortcut.Id)
            && _unpinnedItemCache.TryGetValue(shortcut.Id, out var cached))
        {
            return cached;
        }

        var item = ShortcutListItems.CreateOpen(
            _context,
            shortcut,
            Reload,
            PinnedMoveVisibility.ForShortcut(shortcut, pinnedInOrder),
            onFavoritesReordered: () => Reload(preserveUnpinnedItemCache: true),
            useHomePinContextMenu: true,
            needsRepairOverride: needsRepair);

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
        if (shortcut.RunAsAdmin || RequiresHomeRepair(shortcut))
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

            _services.CallbackQueue.Enqueue(() =>
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
        // Unfiltered -- BuildShortcutItem uses this for pin-move-visibility context regardless
        // of which section a shortcut ends up rendered in.
        var pinnedList = pinnedInOrder.ToList();

        // Directory reachability is populated asynchronously, preserving first-paint latency
        // while returning missing folders to the repair path as soon as it is known.
        var needsAttention = allShortcuts
            .Where(RequiresHomeRepair)
            .OrderBy(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var needsAttentionIds = needsAttention.Count == 0
            ? null
            : needsAttention.Select(shortcut => shortcut.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var visiblePinned = needsAttentionIds is null
            ? pinnedList
            : pinnedList.Where(shortcut => !needsAttentionIds.Contains(shortcut.Id)).ToList();

        var recents = ShortcutRecents.GetRecentWorkspaces(allShortcuts, _settings.RecentWorkspaceCount);
        if (needsAttentionIds is not null)
        {
            recents = recents.Where(shortcut => !needsAttentionIds.Contains(shortcut.Id)).ToList();
        }

        var excludeFromWorkspaces = recents.Count == 0
            ? needsAttentionIds
            : recents.Select(shortcut => shortcut.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (needsAttentionIds is not null && excludeFromWorkspaces is not null && excludeFromWorkspaces != needsAttentionIds)
        {
            excludeFromWorkspaces.UnionWith(needsAttentionIds);
        }

        foreach (var item in ShortcutLayoutDisplay.BuildFavoriteItems(
                     layout,
                     shortcut => BuildShortcutItem(shortcut, pinnedList),
                     visiblePinned))
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
                     excludeFromWorkspaces,
                     showDefaultWorkspacesHeader: visiblePinned.Count > 0))
        {
            yield return item;
        }

        if (needsAttention.Count > 0)
        {
            foreach (var item in SectionListItems.InSection(
                         Strings.Section_NeedsAttention,
                         needsAttention.Select(shortcut => BuildShortcutItem(shortcut, pinnedList))))
            {
                yield return item;
            }
        }
    }

    private bool RequiresHomeRepair(TerminalShortcut shortcut)
    {
        if (ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false))
        {
            return true;
        }

        var key = GetDirectoryRepairKey(shortcut);
        if (_directoryRepairStates.TryGetValue(key, out var needsRepair))
        {
            return needsRepair;
        }

        if (_directoryRepairChecks.TryAdd(key, 0))
        {
            _ = Task.Run(() => ProbeDirectoryRepairState(shortcut, key));
        }

        return false;
    }

    private void ProbeDirectoryRepairState(TerminalShortcut shortcut, string key)
    {
        var needsRepair = ShortcutHealth.WouldNeedRepair(shortcut);
        if (_disposed)
        {
            return;
        }

        // Only rebuild the home list when the probe flips the visible repair state. A
        // healthy probe that confirms an already-healthy (or already-known-bad) directory
        // does not need a full rebuild and would otherwise raise ItemsChanged N times for
        // N shortcuts. _directoryRepairStates/_directoryRepairChecks are ConcurrentDictionary,
        // so this is safe to compute and apply directly from the probe thread.
        var stateChanged = !_directoryRepairStates.TryGetValue(key, out var previous)
            || previous != needsRepair;
        _directoryRepairStates[key] = needsRepair;
        // Drop the in-flight marker so a later refresh (e.g. a previously
        // offline drive coming back) can schedule another probe instead of
        // returning the stale cached state forever.
        _directoryRepairChecks.TryRemove(key, out _);

        if (!stateChanged)
        {
            return;
        }

        _services.CallbackQueue.Enqueue(() =>
        {
            if (_disposed)
            {
                return;
            }

            // _unpinnedItemCache is a plain Dictionary (not concurrent), so this must run
            // on the COM/fetch thread via the callback queue, not directly on the probe
            // thread.
            if (!string.IsNullOrWhiteSpace(shortcut.Id))
            {
                _unpinnedItemCache.Remove(shortcut.Id);
            }

            // Use the private overload: it does not clear
            // _directoryRepairStates/_directoryRepairChecks, which would
            // erase the state just written above before the next paint
            // reads it and cause the probe to re-run indefinitely.
            Reload(preserveUnpinnedItemCache: true);
        });

        // Wake the host now. GetItems() is the only place that drains the callback queue,
        // so without this, the queued reload above only runs the next time GetItems() is
        // called for some other reason (e.g. the user searching) — which may never happen
        // if they just open the list and wait, leaving a missing/offline folder shown as
        // launchable instead of moving to Needs Attention.
        try
        {
            RaiseItemsChanged();
        }
        catch
        {
            // Nested/cross-thread ItemsChanged can throw 0x800706BA (see
            // InvalidateWorkspaces). The queued reload above still runs the next time
            // GetItems() executes for any other reason.
        }
    }

    private static string GetDirectoryRepairKey(TerminalShortcut shortcut) =>
        string.Concat(shortcut.Id, "|", shortcut.Directory);
}
