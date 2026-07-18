using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using QuickShell.Commands;
using System.Threading;

namespace QuickShell;

internal sealed partial class QuickShellFallback : FallbackCommandItem, IDisposable
{
    private const string CommandId = "com.quickshell.fallback";

    private static readonly NoOpCommand BaseCommand = new() { Id = CommandId };

    private readonly QuickShellPageContext _context;
    private readonly Lazy<QuickShellFallbackPage> _listPage;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
    private readonly object _searchIndexSync = new();
    private string _lastQuery = string.Empty;
    private long _queryGeneration;
    private RootPaletteSearchIndex? _cachedSearchIndex;
    private long _cachedSearchRevision = -1;

    public QuickShellFallback(
        QuickShellPageContext context,
        Lazy<QuickShellFallbackPage> listPage)
        : base(BaseCommand, "Saved workspace", CommandId)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _listPage = listPage;
        _discoverGitReposCommand = new OpenDiscoverGitReposCommand(context);

        Title = string.Empty;
        Subtitle = string.Empty;
        Icon = QuickShellBrandIcons.App;
    }

    public override void UpdateQuery(string query)
    {
        var querySnapshot = query ?? string.Empty;
        _lastQuery = querySnapshot;
        var generation = Interlocked.Increment(ref _queryGeneration);

        try
        {
            var snapshot = _context.Services.Shortcuts.GetSnapshot();
            RootPaletteSearchIndex searchIndex;
            lock (_searchIndexSync)
            {
                if (_cachedSearchIndex is null || _cachedSearchIndex.Revision != snapshot.Version)
                {
                    _cachedSearchIndex = new RootPaletteSearchIndex(snapshot);
                    _cachedSearchRevision = snapshot.Version;
                }

                searchIndex = _cachedSearchIndex;
            }

            var result = searchIndex.Search(querySnapshot, _context.Services.GitRepos);
            if (generation != Volatile.Read(ref _queryGeneration))
            {
                return;
            }

            ApplyResult(result, querySnapshot);
        }
        catch (TimeoutException)
        {
            // The shortcut store lock was stuck; fall through to no-result rather than
            // surfacing a host error on a fallback keystroke.
            if (generation == Volatile.Read(ref _queryGeneration))
            {
                ClearResult();
            }
        }
    }

    private void ApplyResult(in RootPaletteSearchResult result, string query)
    {
        switch (result.Kind)
        {
            case RootPaletteResultKind.TaskActions:
                if (result.TaskActions!.Count == 1)
                {
                    ApplyTaskResult(result.TaskActions[0]);
                }
                else
                {
                    var listPage = _listPage.Value;
                    listPage.SetTaskResults(query, result.TaskActions);
                    ApplyTaskResults(result.TaskActions, query);
                }
                break;

            case RootPaletteResultKind.Workspaces:
                {
                    var listPage = _listPage.Value;
                    listPage.SetWorkspaceResults(query, result.Workspaces!);
                    ApplyWorkspaceResult(result.Workspaces!, query);
                }
                break;

            case RootPaletteResultKind.Discover:
                _listPage.Value.SetDiscoverEntry(query);
                ApplyDiscoverResult();
                break;

            case RootPaletteResultKind.GitRepos:
                {
                    var listPage = _listPage.Value;
                    listPage.SetGitRepoResults(query, result.GitRepos!);
                    ApplyGitRepoResult(result.GitRepos!, query);
                }
                break;

            default:
                ClearResult();
                break;
        }
    }

    private void ApplyWorkspaceResult(IReadOnlyList<TerminalShortcut> shortcuts, string query)
    {
        if (shortcuts.Count == 1)
        {
            Title = shortcuts[0].Name;
            Subtitle = ShortcutDisplay.BuildDirectorySubtitle(shortcuts[0]);
        }
        else
        {
            Title = $"{shortcuts.Count} workspaces";
            Subtitle = $"Matching \"{query}\"";
        }

        Icon = QuickShellBrandIcons.App;
        Command = _listPage.Value;
        MoreCommands = [];
    }

    private void ApplyTaskResult(WorkspaceTaskAction action)
    {
        var item = ShortcutTaskActionListItems.Create(_context, action, ReloadCurrentQuery);
        Title = item.Title;
        Subtitle = item.Subtitle;
        Icon = item.Icon;
        Command = item.Command;
        MoreCommands = item.MoreCommands;
    }

    private void ReloadCurrentQuery()
    {
        var query = _lastQuery;
        ClearResult();
        UpdateQuery(query);
    }

    private void ApplyTaskResults(IReadOnlyList<WorkspaceTaskAction> taskActions, string query)
    {
        Title = $"{taskActions.Count} task actions";
        Subtitle = $"Matching \"{query}\"";
        Icon = QuickShellBrandIcons.App;
        Command = _listPage.Value;
        MoreCommands = [];
    }

    private void ApplyGitRepoResult(IReadOnlyList<GitRepoCandidate> gitRepos, string query)
    {
        if (gitRepos.Count == 1)
        {
            Title = $"Add {gitRepos[0].Name}";
            Subtitle = ShortcutDisplay.ShortenPathForDisplay(gitRepos[0].Directory);
        }
        else
        {
            Title = $"{gitRepos.Count} git repos";
            Subtitle = $"Matching \"{query}\"";
        }

        Icon = new IconInfo(ShortcutGlyphs.Discover);
        Command = _listPage.Value;
        MoreCommands = [];
    }

    private void ApplyDiscoverResult()
    {
        Title = "Discover git repos";
        Subtitle = "Scan local folders and add as workspaces";
        Icon = new IconInfo(ShortcutGlyphs.Discover);
        Command = _discoverGitReposCommand;
        MoreCommands = [];
    }

    public void Dispose()
    {
        _discoverGitReposCommand.Dispose();
    }

    private void ClearResult()
    {
        Title = string.Empty;
        Subtitle = string.Empty;
        Command = BaseCommand;
        MoreCommands = [];
        if (_listPage.IsValueCreated)
        {
            _listPage.Value.ClearResults();
        }
    }
}
