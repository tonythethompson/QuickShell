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

        if (ShouldSuppress(query))
        {
            ClearResult();
            return;
        }

        try
        {
            var taskActions = _context.Services.Shortcuts.SearchTaskActions(_lastQuery).ToArray();
            if (taskActions.Length > 0)
            {
                if (taskActions.Length == 1)
                {
                    ApplyTaskResult(taskActions[0]);
                    return;
                }

                var listPage = _listPage.Value;
                listPage.SetTaskResults(_lastQuery, taskActions);
                ApplyTaskResults(taskActions);
                return;
            }

            var shortcuts = _context.Services.Shortcuts.SearchForRootPalette(_lastQuery).ToArray();
            if (shortcuts.Length > 0)
            {
                var listPage = _listPage.Value;
                listPage.SetWorkspaceResults(_lastQuery, shortcuts);
                ApplyWorkspaceResult(shortcuts);
                return;
            }

            if (GitRepoIndex.IsDiscoverQuery(_lastQuery))
            {
                _listPage.Value.SetDiscoverEntry(_lastQuery);
                ApplyDiscoverResult();
                return;
            }

            var allShortcuts = _context.Services.Shortcuts.GetShortcuts();
            var extraRoots = GitRepoSearchRoots.FromShortcuts(allShortcuts).ToList();
            var savedDirectories = allShortcuts
                .Select(shortcut => shortcut.Directory)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var gitRepos = _context.Services.GitRepos
                .Search(_lastQuery, extraRoots, savedDirectories)
                .ToArray();
            if (gitRepos.Length > 0)
            {
                var listPage = _listPage.Value;
                listPage.SetGitRepoResults(_lastQuery, gitRepos);
                ApplyGitRepoResult(gitRepos);
                return;
            }
        }
        catch (TimeoutException)
        {
            // The shortcut store lock was stuck; fall through to no-result rather than
            // surfacing a host error on a fallback keystroke.
        }

        ClearResult();
    }

    private void ApplyWorkspaceResult(TerminalShortcut[] shortcuts)
    {
        if (shortcuts.Length == 1)
        {
            Title = shortcuts[0].Name;
            Subtitle = ShortcutDisplay.BuildDirectorySubtitle(shortcuts[0]);
        }
        else
        {
            Title = $"{shortcuts.Length} workspaces";
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

    private void ApplyTaskResults(WorkspaceTaskAction[] taskActions)
    {
        Title = $"{taskActions.Length} task actions";
        Subtitle = $"Matching \"{_lastQuery}\"";
        Icon = QuickShellBrandIcons.App;
        Command = _listPage.Value;
        MoreCommands = [];
    }

    private void ApplyGitRepoResult(GitRepoCandidate[] gitRepos)
    {
        if (gitRepos.Length == 1)
        {
            Title = $"Add {gitRepos[0].Name}";
            Subtitle = ShortcutDisplay.ShortenPathForDisplay(gitRepos[0].Directory);
        }
        else
        {
            Title = $"{gitRepos.Length} git repos";
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

    private static bool ShouldSuppress(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return query.Contains("quick shell", StringComparison.OrdinalIgnoreCase);
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
