using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using QuickShell.Commands;

namespace QuickShell;

internal sealed partial class QuickShellFallback : FallbackCommandItem, IDisposable
{
    private const string CommandId = "com.quickshell.fallback";

    private static readonly NoOpCommand BaseCommand = new() { Id = CommandId };

    private readonly QuickShellPageContext _context;
    private readonly Lazy<QuickShellFallbackPage> _listPage;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
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
        _lastQuery = query ?? string.Empty;
        var generation = ++_queryGeneration;

        try
        {
            var snapshot = _context.Services.Shortcuts.GetSnapshot();
            if (_cachedSearchIndex is null || _cachedSearchIndex.Revision != snapshot.Version)
            {
                _cachedSearchIndex = new RootPaletteSearchIndex(snapshot);
                _cachedSearchRevision = snapshot.Version;
            }

            var result = _cachedSearchIndex.Search(_lastQuery, _context.Services.GitRepos, generation);
            if (result.Generation != generation)
            {
                return;
            }

            ApplyResult(result);
        }
        catch (TimeoutException)
        {
            // The shortcut store lock was stuck; fall through to no-result rather than
            // surfacing a host error on a fallback keystroke.
            ClearResult();
        }
    }

    private void ApplyResult(in RootPaletteSearchResult result)
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
                    listPage.SetTaskResults(_lastQuery, result.TaskActions);
                    ApplyTaskResults(result.TaskActions);
                }
                break;

            case RootPaletteResultKind.Workspaces:
                {
                    var listPage = _listPage.Value;
                    listPage.SetWorkspaceResults(_lastQuery, result.Workspaces!);
                    ApplyWorkspaceResult(result.Workspaces!);
                }
                break;

            case RootPaletteResultKind.Discover:
                _listPage.Value.SetDiscoverEntry(_lastQuery);
                ApplyDiscoverResult();
                break;

            case RootPaletteResultKind.GitRepos:
                {
                    var listPage = _listPage.Value;
                    listPage.SetGitRepoResults(_lastQuery, result.GitRepos!);
                    ApplyGitRepoResult(result.GitRepos!);
                }
                break;

            default:
                ClearResult();
                break;
        }
    }

    private void ApplyWorkspaceResult(IReadOnlyList<TerminalShortcut> shortcuts)
    {
        if (shortcuts.Count == 1)
        {
            Title = shortcuts[0].Name;
            Subtitle = ShortcutDisplay.BuildDirectorySubtitle(shortcuts[0]);
        }
        else
        {
            Title = $"{shortcuts.Count} workspaces";
            Subtitle = $"Matching \"{_lastQuery}\"";
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

    private void ApplyTaskResults(IReadOnlyList<WorkspaceTaskAction> taskActions)
    {
        Title = $"{taskActions.Count} task actions";
        Subtitle = $"Matching \"{_lastQuery}\"";
        Icon = QuickShellBrandIcons.App;
        Command = _listPage.Value;
        MoreCommands = [];
    }

    private void ApplyGitRepoResult(IReadOnlyList<GitRepoCandidate> gitRepos)
    {
        if (gitRepos.Count == 1)
        {
            Title = $"Add {gitRepos[0].Name}";
            Subtitle = ShortcutDisplay.ShortenPathForDisplay(gitRepos[0].Directory);
        }
        else
        {
            Title = $"{gitRepos.Count} git repos";
            Subtitle = $"Matching \"{_lastQuery}\"";
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
