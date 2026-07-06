using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class QuickShellFallbackPage : DynamicListPage, IDisposable
{
    private readonly QuickShellSettingsManager _settings;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
    private readonly Action _onReload;
    private readonly SearchDebouncer _searchDebouncer;
    private IListItem[] _items = [];
    private string _query = string.Empty;
    private WorkspaceTaskAction[] _taskActions = [];
    private TerminalShortcut[] _shortcuts = [];
    private IReadOnlyList<GitRepoCandidate> _gitRepos = [];
    private bool _showDiscoverEntry;

    public QuickShellFallbackPage(QuickShellSettingsManager settings, Action onReload)
    {
        _settings = settings;
        _onReload = onReload;
        _discoverGitReposCommand = new OpenDiscoverGitReposCommand(onReload);
        _searchDebouncer = new SearchDebouncer(ApplyQueryDebounced);
        Icon = QuickShellBrandIcons.App;
        Title = "Saved workspace";
        Name = "Open";
    }

    public void SetTaskResults(string query, WorkspaceTaskAction[] taskActions)
    {
        _query = query ?? string.Empty;
        _taskActions = taskActions;
        _shortcuts = [];
        _gitRepos = [];
        _showDiscoverEntry = false;
        RefreshItems();
    }

    public void SetWorkspaceResults(string query, TerminalShortcut[] shortcuts)
    {
        _query = query ?? string.Empty;
        _taskActions = [];
        _shortcuts = shortcuts;
        _gitRepos = [];
        _showDiscoverEntry = false;
        RefreshItems();
    }

    public void SetGitRepoResults(string query, IReadOnlyList<GitRepoCandidate> gitRepos)
    {
        _query = query ?? string.Empty;
        _taskActions = [];
        _shortcuts = [];
        _gitRepos = gitRepos;
        _showDiscoverEntry = false;
        RefreshItems();
    }

    public void SetDiscoverEntry(string query)
    {
        _query = query ?? string.Empty;
        _taskActions = [];
        _shortcuts = [];
        _gitRepos = [];
        _showDiscoverEntry = true;
        RefreshItems();
    }

    public void ClearResults()
    {
        _query = string.Empty;
        _taskActions = [];
        _shortcuts = [];
        _gitRepos = [];
        _showDiscoverEntry = false;
        RefreshItems();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var normalized = newSearch ?? string.Empty;
        if (string.Equals(_query, normalized, StringComparison.Ordinal)
            && _taskActions.Length == 0
            && _shortcuts.Length == 0
            && _gitRepos.Count == 0
            && !_showDiscoverEntry)
        {
            return;
        }

        _searchDebouncer.Schedule(normalized);
    }

    public override IListItem[] GetItems() => _items;

    public void Dispose() => _searchDebouncer.Dispose();

    private void ApplyQueryDebounced(string normalized)
    {
        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _query = normalized;
        RefreshItems();
    }

    private void RefreshItems()
    {
        var items = new List<IListItem>();

        if (_showDiscoverEntry)
        {
            items.Add(new ListItem(_discoverGitReposCommand)
            {
                Title = "Discover git repos",
                Subtitle = "Scan local folders and add as workspaces",
                Icon = new IconInfo(ShortcutGlyphs.Discover),
            });

            items.AddRange(BuildGitRepoItems(GetDiscoverPreviewRepos()));
        }
        else
        {
            items.AddRange(_taskActions.Select(BuildTaskActionItem));
            items.AddRange(_shortcuts.Select(BuildShortcutItem));
            items.AddRange(BuildGitRepoItems(_gitRepos));
        }

        _items = items.ToArray();
        RaiseItemsChanged();
    }

    private static List<GitRepoCandidate> GetDiscoverPreviewRepos()
    {
        var extraRoots = GitRepoSearchRoots.FromShortcuts(QuickShellRuntimeServices.Shortcuts.GetShortcuts());
        var savedDirectories = QuickShellRuntimeServices.Shortcuts.GetShortcuts()
            .Select(shortcut => shortcut.Directory)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GitRepoIndex.GetAll(extraRoots)
            .Where(candidate => !savedDirectories.Contains(candidate.Directory))
            .Take(8)
            .ToList();
    }

    private IEnumerable<IListItem> BuildGitRepoItems(IReadOnlyList<GitRepoCandidate> gitRepos)
    {
        foreach (var candidate in gitRepos)
        {
            yield return DiscoverGitRepoListItems.CreateNew(candidate, OnGitRepoAdded, title: $"Add {candidate.Name}");
        }
    }

    private void OnGitRepoAdded()
    {
        GitRepoIndex.Invalidate();
        _onReload();
    }

    private ListItem BuildShortcutItem(TerminalShortcut shortcut)
    {
        var item = ShortcutListItems.CreateOpen(shortcut, _settings, _onReload);
        if (ShortcutHealth.WouldNeedRepair(shortcut))
        {
            return item;
        }

        item.Subtitle = ShortcutDisplay.BuildDirectorySubtitle(shortcut);
        item.MoreCommands = ShortcutContextCommands.Build(shortcut, _onReload, _settings, includeEdit: false);
        return item;
    }

    private ListItem BuildTaskActionItem(WorkspaceTaskAction action) =>
        ShortcutTaskActionListItems.Create(action, _settings, _onReload);
}
