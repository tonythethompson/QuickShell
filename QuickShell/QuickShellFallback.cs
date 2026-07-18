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
    private const string CommandId = CommandDescriptor.FallbackCommandId;

    private static readonly NoOpCommand BaseCommand = new() { Id = CommandId };

    private readonly QuickShellPageContext _context;
    private readonly Lazy<QuickShellFallbackPage> _listPage;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
    private readonly object _searchIndexSync = new();
    private string _lastQuery = string.Empty;
    private bool _awaitingGitRefresh;
    private bool _disposed;
    private long _queryGeneration;
    private RootPaletteSearchIndex? _cachedSearchIndex;

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

        if (ShouldSuppress(querySnapshot))
        {
            ClearResult();
            return;
        }

        try
        {
            var snapshot = _context.Services.Shortcuts.GetSnapshot();
            RootPaletteSearchIndex searchIndex;
            lock (_searchIndexSync)
            {
                if (_cachedSearchIndex is null || _cachedSearchIndex.Revision != snapshot.Version)
                {
                    _cachedSearchIndex = new RootPaletteSearchIndex(snapshot);
                }

                searchIndex = _cachedSearchIndex;
            }

            var result = searchIndex.Search(querySnapshot, _context.Services.GitRepos);
            if (generation != Volatile.Read(ref _queryGeneration))
            {
                return;
            }

            ApplyResult(result, querySnapshot, searchIndex.Revision);
            if (result.Kind == RootPaletteResultKind.None)
            {
                RegisterForGitRefresh();
            }
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

    private void ApplyResult(
        in RootPaletteSearchResult result,
        string query,
        long repositoryVersion)
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
                    listPage.SetWorkspaceResults(query, result.Workspaces!, repositoryVersion);
                    ApplyWorkspaceResult(result.Workspaces!, query, repositoryVersion);
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

    private void ApplyWorkspaceResult(
        IReadOnlyList<TerminalShortcut> shortcuts,
        string query,
        long repositoryVersion)
    {
        if (shortcuts.Count == 1)
        {
            // Reuse the shared row presentation so root-palette and list-page rows agree.
            var presentation = _context.Services.RowPresentation.GetOrBuild(
                shortcuts[0],
                repositoryVersion,
                _context.Settings.RowPresentationFingerprint,
                WorkspaceRowPresentationMode.SearchResult);
            Title = presentation.Title;
            Subtitle = presentation.Subtitle;
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

    private void RegisterForGitRefresh()
    {
        if (_awaitingGitRefresh)
        {
            return;
        }

        _awaitingGitRefresh = true;
        if (!_context.Services.GitRepos.TryRunAfterNextRefreshIfInFlight(OnGitRefreshCompleted))
        {
            _awaitingGitRefresh = false;
        }
    }

    private void OnGitRefreshCompleted()
    {
        _awaitingGitRefresh = false;
        if (_disposed)
        {
            return;
        }

        ReloadCurrentQuery();
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
        _disposed = true;
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

    private static bool ShouldSuppress(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        query.Contains("quick shell", StringComparison.OrdinalIgnoreCase);
}
