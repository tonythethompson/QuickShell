using Microsoft.CommandPalette.Extensions;

using Microsoft.CommandPalette.Extensions.Toolkit;

using QuickShell.Models;

using QuickShell.Pages;

using QuickShell.Services;

using QuickShell.Commands;



namespace QuickShell;



internal sealed partial class QuickShellFallback : FallbackCommandItem

{

    private const string CommandId = "com.quickshell.fallback";

    private static readonly NoOpCommand BaseCommand = new() { Id = CommandId };



    private readonly Lazy<QuickShellFallbackPage> _listPage;

    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;

    private string _lastQuery = string.Empty;



    public QuickShellFallback(Lazy<QuickShellFallbackPage> listPage, OpenDiscoverGitReposCommand discoverGitReposCommand)

        : base(BaseCommand, "Saved workspace", CommandId)

    {

        _listPage = listPage;

        _discoverGitReposCommand = discoverGitReposCommand;

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



        var shortcuts = QuickShellRuntimeServices.Shortcuts.SearchForRootPalette(_lastQuery).ToArray();

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



        var allShortcuts = QuickShellRuntimeServices.Shortcuts.GetShortcuts();

        var extraRoots = GitRepoSearchRoots.FromShortcuts(allShortcuts);

        var savedDirectories = allShortcuts

            .Select(shortcut => shortcut.Directory)

            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var gitRepos = GitRepoIndex.Search(_lastQuery, extraRoots, savedDirectories).ToArray();

        if (gitRepos.Length > 0)

        {

            var listPage = _listPage.Value;

            listPage.SetGitRepoResults(_lastQuery, gitRepos);

            ApplyGitRepoResult(gitRepos);

            return;

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


