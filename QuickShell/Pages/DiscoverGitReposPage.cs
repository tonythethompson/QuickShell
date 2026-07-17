using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickShell.Pages;

/// <summary>
/// Shared discover-page behavior. Concrete commands must provide their own CmdPal metadata.
/// </summary>
internal abstract partial class DiscoverGitReposPage : DynamicListPage, IDisposable
{
    public const string PageId = CommandDescriptor.DiscoverGitReposId;

    /// <summary>
    /// CmdPal host sentinel (<c>ListViewModel.IncrementalRefresh</c>): keep list selection
    /// across a GetItems refetch. Default <c>RaiseItemsChanged()</c> forces first-item selection.
    /// </summary>
    private const int KeepSelectionRefresh = -2;

    private readonly QuickShellPageContext _context;
    private readonly IQuickShellServices _services;
    private readonly Action _onReload;
    private readonly SynchronizationContext? _extensionSynchronizationContext;
    private readonly object _refreshSync = new();
    private readonly SearchDebouncer _searchDebouncer;
    private readonly Dictionary<string, ListItem> _itemCache = new(StringComparer.OrdinalIgnoreCase);
    private IListItem[] _items = [];
    private string _query = string.Empty;
    private bool _refreshScheduled;
    private bool _hasShownInitialList;
    private bool _awaitingGitRefresh;
    private bool _hasPublishedResults;
    private bool _disposed;

    protected DiscoverGitReposPage(QuickShellPageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _services = context.Services;
        _onReload = context.ReloadRootPages;
        _extensionSynchronizationContext = SynchronizationContext.Current ?? GitRepoIndex.ExtensionSynchronizationContext;
        _searchDebouncer = new SearchDebouncer(ApplyQueryDebounced);
#if CMDPAL_HOVER_ACTIONS
        // Match home list so Tab/hover keyboard can reach secondary actions (open folder, etc.).
        HoverActionsMode = HoverActionsMode.Explicit;
        MaxHoverActions = -1;
        HoverActionsVisibility = HoverActionsVisibility.HoverOrSelected;
#endif
        SetOpeningItems();
        // Kick the first scan immediately. Waiting for UpdateSearchText alone races
        // CmdPal hosts that never nudge search text on first open.
        ScheduleRefreshItems();
    }

    public override IListItem[] GetItems()
    {
        ExtensionCallbackQueue.Drain();

        // Never call RaiseItemsChanged from GetItems — CmdPal may be mid-fetch and a nested
        // ItemsChanged defers a second fetch that rebuilds the list and drops keyboard selection.
        if (_awaitingGitRefresh && !GitRepoIndex.IsRefreshInFlight)
        {
            _awaitingGitRefresh = false;
            ScheduleRefreshItems();
        }

        return _items;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var normalized = newSearch ?? string.Empty;

        if (!_hasShownInitialList)
        {
            _hasShownInitialList = true;
            // Constructor already scheduled the first refresh; only adopt a non-empty query.
            if (!string.Equals(_query, normalized, StringComparison.Ordinal))
            {
                _query = normalized;
                ScheduleRefreshItems();
            }

            return;
        }

        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            // Replace any pending different query with the text currently in CmdPal.
            _searchDebouncer.Schedule(normalized);
            return;
        }

        // Debounce typing so each keystroke does not rebuild the list and snap selection
        // back to the first row (which feels like broken keyboard navigation).
        _searchDebouncer.Schedule(normalized);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _searchDebouncer.Dispose();
    }

    private void ApplyQueryDebounced(string normalized)
    {
        if (_extensionSynchronizationContext is not null
            && !ReferenceEquals(SynchronizationContext.Current, _extensionSynchronizationContext))
        {
            _extensionSynchronizationContext.Post(_ => ApplyQueryDebounced(normalized), null);
            return;
        }

        if (_disposed)
        {
            return;
        }

        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _query = normalized ?? string.Empty;
        // Filter changes should select the first match, not keep a now-missing row.
        RefreshItems(_query, resetSelection: true);
    }

    private void ScheduleRefreshItems()
    {
        if (_disposed)
        {
            return;
        }

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

            if (_disposed)
            {
                return;
            }

            RefreshItems(_query, resetSelection: !_hasPublishedResults);
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

    private void RefreshItems(string query, bool resetSelection)
    {
        try
        {
            var shortcuts = _services.Shortcuts.GetShortcuts();
            var extraRoots = GitRepoSearchRoots.FromShortcuts(shortcuts);
            var discovered = GitRepoIndex.GetAll(extraRoots).ToList();
            if (discovered.Count == 0
                && GitRepoIndex.TryRunAfterNextRefreshIfInFlight(OnGitRefreshCompleted))
            {
                _awaitingGitRefresh = true;
                // Keep the scanning placeholder visible until the in-flight scan finishes.
                if (_items.Length == 0)
                {
                    SetOpeningItems();
                    RaiseItemsChanged();
                }

                return;
            }

            _awaitingGitRefresh = false;

            var shortcutsByDirectory = DiscoverGitRepoListItems.GroupShortcutsByDirectory(shortcuts);
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
                .BuildSectionedItems(_context, discovered, _onReload, shortcutsByDirectory, _itemCache)
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
            // First publish / filter changes: select first useful row.
            // Later in-place refreshes (e.g. scan complete with stable items): keep selection.
            RaiseItemsChanged(resetSelection || !_hasPublishedResults ? -1 : KeepSelectionRefresh);
            _hasPublishedResults = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
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
            try
            {
                RaiseItemsChanged();
            }
            catch (Exception refreshException) when (refreshException is InvalidOperationException or COMException)
            {
                // CmdPal may reject notifications while tearing down the page.
            }
        }
    }

    private void OnGitRefreshCompleted()
    {
        if (_disposed)
        {
            return;
        }

        _awaitingGitRefresh = false;
        // Push results as soon as the background scan finishes. Do not wait for
        // another GetItems/UpdateSearchText cycle (that was the empty-until-revisit bug).
        RefreshItems(_query, resetSelection: !_hasPublishedResults);
    }
}
