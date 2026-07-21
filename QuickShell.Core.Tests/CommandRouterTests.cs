using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using QuickShell.Services.CommandRouting;
using QuickShell.Services.WorkspaceEditor;
using System.Threading;

namespace QuickShell.Core.Tests;

public sealed class CommandRouterTests : IDisposable
{
    private readonly string _configDirectory;
    private readonly ServiceProvider _provider;

    public CommandRouterTests()
    {
        _configDirectory = Path.Join(
            Path.GetTempPath(),
            "quickshell-router-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);

        var services = new ServiceCollection();
        var settingsManager = new QuickShellSettingsManager();

        // Use AddQuickShellHost to get the real routing registrations, then replace
        // the heavy/git-dependent services with deterministic test doubles.
        services.AddQuickShellHost(settingsManager, _configDirectory);

        services.AddSingleton<IWorkspaceGitOperations, TestWorkspaceGitOperations>();
        services.AddSingleton<IWorkspaceHealthChecker, TestWorkspaceHealthChecker>();
        services.AddSingleton<IProjectAnalysisService, TestProjectAnalysisService>();
        services.AddSingleton<IGitRepoIndex, TestGitRepoIndex>();

        // Replace the default service facade with a thin adapter that delegates to the
        // real core services except for the fakes registered above.
        services.AddSingleton<IQuickShellServices>(sp => new TestQuickShellServices(sp));

        _provider = services.BuildServiceProvider();

        var quickShellServices = _provider.GetRequiredService<IQuickShellServices>();
        settingsManager.InitializeServices(quickShellServices);
    }

    [Fact]
    public void TryHandle_resolves_each_routable_deep_link_command_kind_to_expected_item_type()
    {
        var workspaceDir = Path.Join(_configDirectory, "workspace");
        Directory.CreateDirectory(workspaceDir);

        var workspaceId = Guid.NewGuid().ToString("N");
        var launchId = Guid.NewGuid().ToString("N");
        var shortcut = new TerminalShortcut
        {
            Id = workspaceId,
            Name = "Test",
            Directory = workspaceDir,
            Launches =
            [
                new WorkspaceEntry { Id = launchId, Label = "Run", IsEnabled = true }
            ],
        };

        var quickShellServices = _provider.GetRequiredService<IQuickShellServices>();
        quickShellServices.Shortcuts.Upsert(shortcut);

        // The repository assigns a fresh stable ID on insert, so resolve the stored IDs.
        var stored = quickShellServices.Shortcuts.GetShortcuts()[0];
        var assignedWorkspaceId = stored.Id;
        var assignedLaunchId = stored.Launches[0].Id;

        var host = _provider.GetRequiredService<QuickShellHostServices>();
        var createShortcut = new CreateShortcutCommand(() => { }, quickShellServices);
        var context = new QuickShellPageContext(host, createShortcut, () => { });
        var router = _provider.GetRequiredService<ICommandRouter>();

        AssertCommandItemRoutedTo<QuickShellExtensionSettingsPage>(
            router, context, CommandDescriptor.Settings().Id);
        AssertCommandItemRoutedTo<ImportConflictPage>(
            router, context, CommandDescriptor.ImportConflict().Id);
        AssertCommandItemRoutedTo<PendingShortcutEditPage>(
            router, context, CommandDescriptor.PendingShortcutEdit().Id);
        AssertCommandItemRoutedTo<CreateShortcutCommand>(
            router, context, CommandDescriptor.CreateWorkspace().Id);
        AssertCommandItemRoutedTo<OpenDiscoverGitReposCommand>(
            router, context, CommandDescriptor.DiscoverGitRepos().Id);
        AssertCommandItemRoutedTo<CreateShortcutCommand>(
            router, context, CommandDescriptor.DiscoverCreate(workspaceDir).Id);

        AssertListItemRoutedTo<OpenTerminalShortcutCommand>(
            router, context, CommandDescriptor.OpenWorkspace(assignedWorkspaceId).Id);
        AssertListItemRoutedTo<OpenShortcutLaunchCommand>(
            router, context, CommandDescriptor.OpenLaunch(assignedWorkspaceId, assignedLaunchId).Id);
        AssertCommandItemRoutedTo<WorkspaceStatusPage>(
            router, context, CommandDescriptor.WorkspaceStatus(assignedWorkspaceId).Id);
        AssertCommandItemRoutedTo<WorktreeBranchPickerPage>(
            router, context, CommandDescriptor.WorktreeBranchPicker(assignedWorkspaceId).Id);
        AssertCommandItemRoutedTo<SelectWorktreeBranchCommand>(
            router, context, CommandDescriptor.WorktreeBranchSelect(assignedWorkspaceId, "feature/x").Id);
        AssertCommandItemRoutedTo<UseCurrentWorktreeBranchCommand>(
            router, context, CommandDescriptor.WorktreeBranchClear(assignedWorkspaceId).Id);
    }

    [Fact]
    public void TryHandle_returns_false_for_in_page_favorite_command_kinds()
    {
        var host = _provider.GetRequiredService<QuickShellHostServices>();
        var createShortcut = new CreateShortcutCommand(() => { }, _provider.GetRequiredService<IQuickShellServices>());
        var context = new QuickShellPageContext(host, createShortcut, () => { });
        var router = _provider.GetRequiredService<ICommandRouter>();

        var favoriteToggleId = CommandDescriptor.FavoriteToggle("my-workspace").Id;
        var favoriteMoveId = CommandDescriptor.FavoriteMove(
            "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
            "Up").Id;

        Assert.False(
            router.TryHandle(favoriteToggleId, context, out var toggleItem),
            "FavoriteToggle is an in-page ID and should not be routed as an external deep link.");
        Assert.Null(toggleItem);

        Assert.False(
            router.TryHandle(favoriteMoveId, context, out var moveItem),
            "FavoriteMove is an in-page ID and should not be routed as an external deep link.");
        Assert.Null(moveItem);
    }

    private static void AssertCommandItemRoutedTo<TCommand>(
        ICommandRouter router,
        QuickShellPageContext context,
        string commandId)
        where TCommand : class
    {
        Assert.True(
            router.TryHandle(commandId, context, out var item),
            $"Router should handle {commandId}");
        Assert.NotNull(item);
        var commandItem = Assert.IsType<CommandItem>(item);
        Assert.IsType<TCommand>(commandItem.Command);
    }

    private static void AssertListItemRoutedTo<TCommand>(
        ICommandRouter router,
        QuickShellPageContext context,
        string commandId)
        where TCommand : class
    {
        Assert.True(
            router.TryHandle(commandId, context, out var item),
            $"Router should handle {commandId}");
        Assert.NotNull(item);
        var listItem = Assert.IsType<ListItem>(item);
        Assert.IsType<TCommand>(listItem.Command);
    }

    public void Dispose()
    {
        _provider.Dispose();

        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    private sealed class TestQuickShellServices : IQuickShellServices
    {
        private readonly IServiceProvider _provider;

        public TestQuickShellServices(IServiceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public IShortcutRepository Shortcuts => _provider.GetRequiredService<IShortcutRepository>();
        public IDraftStore Drafts => _provider.GetRequiredService<IDraftStore>();
        public QuickShellSettingsManager Settings => _provider.GetRequiredService<QuickShellSettingsManager>();
        public IProjectAnalysisService ProjectAnalysis => _provider.GetRequiredService<IProjectAnalysisService>();
        public ICommandSuggestionService CommandSuggestions => _provider.GetRequiredService<ICommandSuggestionService>();
        public IWorkspaceLaunchService WorkspaceLaunch => _provider.GetRequiredService<IWorkspaceLaunchService>();
        public IShortcutLaunchExecutor LaunchExecutor => _provider.GetRequiredService<IShortcutLaunchExecutor>();
        public IWorkspaceGitOperations GitOperations => _provider.GetRequiredService<IWorkspaceGitOperations>();
        public IWorktreeBranchTargetStore TargetStore => _provider.GetRequiredService<IWorktreeBranchTargetStore>();
        public ICompanionAppLauncher CompanionApps => _provider.GetRequiredService<ICompanionAppLauncher>();
        public IWorkspaceHealthChecker HealthChecker => _provider.GetRequiredService<IWorkspaceHealthChecker>();
        public WorkspaceGitLaunchGate GitLaunchGate => _provider.GetRequiredService<WorkspaceGitLaunchGate>();
        public IGitRepoIndex GitRepos => _provider.GetRequiredService<IGitRepoIndex>();
        public IProjectClassificationCache ClassificationCache => _provider.GetRequiredService<IProjectClassificationCache>();
        public IExtensionCallbackQueue CallbackQueue => _provider.GetRequiredService<IExtensionCallbackQueue>();
        public IWorkspaceRowPresentationCache RowPresentation => _provider.GetRequiredService<IWorkspaceRowPresentationCache>();
        public IRowPresentationDiagnostics RowPresentationDiagnostics => _provider.GetRequiredService<IRowPresentationDiagnostics>();
        public ISettingsFormRefreshScheduler RefreshScheduler => _provider.GetRequiredService<ISettingsFormRefreshScheduler>();
        public IQuickShellLifetime Lifetime => _provider.GetRequiredService<IQuickShellLifetime>();
        public ITerminalCatalog TerminalCatalog => _provider.GetRequiredService<ITerminalCatalog>();
        public IWtProfilesService WtProfiles => _provider.GetRequiredService<IWtProfilesService>();
        public ITerminalListIconCache TerminalListIcons => _provider.GetRequiredService<ITerminalListIconCache>();
        public ITerminalLaunchGlyphs TerminalLaunchGlyphs => _provider.GetRequiredService<ITerminalLaunchGlyphs>();
        public TerminalCatalogPrewarm TerminalCatalogPrewarm => _provider.GetRequiredService<TerminalCatalogPrewarm>();
        public IShortcutFormViewBuilder FormViewBuilder => _provider.GetRequiredService<IShortcutFormViewBuilder>();
        public IWorkspaceEditorFactory WorkspaceEditors =>
            _provider.GetRequiredService<IWorkspaceEditorFactory>();
    }

    private sealed class TestWorkspaceGitOperations : IWorkspaceGitOperations
    {
        public bool TryResolveWorktreeKey(string directory, out string worktreeKey)
        {
            worktreeKey = string.Empty;
            return false;
        }

        public bool TryGetStatus(string directory, out WorkspaceGitStatus status)
        {
            status = null!;
            return false;
        }

        public bool TryGetStatusForLaunch(string directory, out WorkspaceGitStatus status, out bool timedOut)
        {
            status = null!;
            timedOut = false;
            return false;
        }

        public IReadOnlyList<string> ListLocalBranches(string directory) => [];

        public bool TrySwitchBranch(string directory, string branch, out string? error)
        {
            error = null;
            return false;
        }
    }

    private sealed class TestWorkspaceHealthChecker : IWorkspaceHealthChecker
    {
        public WorkspaceHealthResult Check(
            TerminalShortcut shortcut,
            string terminalApplicationId,
            string defaultProfileId,
            bool includeVolatile = true,
            bool includeGit = true) =>
            new([]);

        public WorkspaceHealthResult CheckEntry(
            TerminalShortcut shortcut,
            WorkspaceEntry entry,
            string terminalApplicationId,
            string defaultProfileId,
            bool includeVolatile = true,
            bool includeGit = true) =>
            new([]);
    }

    private sealed class TestProjectAnalysisService : IProjectAnalysisService
    {
        public ProjectClassification Classify(string directory) => ProjectClassification.Empty;

        public bool HasAvailableTaskTypes(string? directory) => false;

        public IReadOnlyList<string> GetAvailableTaskTypes(string? directory, TaskTypePickContext pickContext) => [];

        public bool IsTaskTypeAvailable(string? directory, string? taskType, TaskTypePickContext pickContext) => false;

        public string? TrySuggestTaskCommand(string? directory, string? taskType, TaskTypePickContext pickContext) => null;

        public string GetTaskTypeChoiceTooltip(string? directory, string? taskType, TaskTypePickContext pickContext) => string.Empty;

        public string BuildTaskTypeChoicesJson(string? directory = null, TaskTypePickContext? pickContext = null, bool includePlaceholder = true) => "{}";

        public CompanionAppSuggestion? TrySuggestCompanionApp(string directory) => null;

        public string? TryDetectDevServerUrl(string directory) => null;

        public string? TryInferTaskType(string directory) => null;

        public string? TryDetectDevLaunchCommand(string directory) => null;

        public string FormatPackageScriptCommand(string directory, string scriptName) => scriptName;
    }

    private sealed class TestGitRepoIndex : IGitRepoIndex
    {
        public bool IsRefreshInFlight => false;

        public void Invalidate()
        {
        }

        public void Prewarm(IReadOnlyList<string> searchRoots, CancellationToken cancellationToken = default)
        {
        }

        public IReadOnlyList<GitRepoCandidate> Search(
            string query,
            IReadOnlyList<string> searchRoots,
            IReadOnlySet<string>? savedDirectories = null,
            int maxResults = 8,
            CancellationToken cancellationToken = default) => [];

        public IReadOnlyList<GitRepoCandidate> GetAll(
            IReadOnlyList<string>? extraRoots = null,
            CancellationToken cancellationToken = default) => [];

        public void RunAfterNextRefresh(Action callback)
        {
        }

        public bool TryRunAfterNextRefreshIfInFlight(Action callback) => false;
    }
}
