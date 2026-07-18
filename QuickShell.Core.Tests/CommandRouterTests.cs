using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Services;
using QuickShell.Services.CommandRouting;
using System.Threading;

namespace QuickShell.Core.Tests;

[Collection(WtProfilesServiceIsolation.Name)]
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
    public void TryHandle_resolves_all_deep_link_command_kinds()
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

        var commandIds = new[]
        {
            CommandDescriptor.Settings().Id,
            CommandDescriptor.ImportConflict().Id,
            CommandDescriptor.PendingShortcutEdit().Id,
            CommandDescriptor.CreateWorkspace().Id,
            CommandDescriptor.DiscoverGitRepos().Id,
            CommandDescriptor.DiscoverCreate(workspaceDir).Id,
            CommandDescriptor.OpenWorkspace(assignedWorkspaceId).Id,
            CommandDescriptor.OpenLaunch(assignedWorkspaceId, assignedLaunchId).Id,
            CommandDescriptor.WorkspaceStatus(assignedWorkspaceId).Id,
            CommandDescriptor.WorktreeBranchPicker(assignedWorkspaceId).Id,
            CommandDescriptor.WorktreeBranchSelect(assignedWorkspaceId, "feature/x").Id,
            CommandDescriptor.WorktreeBranchClear(assignedWorkspaceId).Id,
        };

        foreach (var commandId in commandIds)
        {
            Assert.True(
                router.TryHandle(commandId, context, out var item),
                $"Router should handle {commandId}");
            Assert.NotNull(item);
        }
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
        public IShortcutLaunchExecutor LaunchExecutor => _provider.GetRequiredService<IShortcutLaunchExecutor>();
        public IWorkspaceGitOperations GitOperations => _provider.GetRequiredService<IWorkspaceGitOperations>();
        public ICompanionAppLauncher CompanionApps => _provider.GetRequiredService<ICompanionAppLauncher>();
        public IWorkspaceHealthChecker HealthChecker => _provider.GetRequiredService<IWorkspaceHealthChecker>();
        public WorkspaceGitLaunchGate GitLaunchGate => _provider.GetRequiredService<WorkspaceGitLaunchGate>();
        public IGitRepoIndex GitRepos => _provider.GetRequiredService<IGitRepoIndex>();
        public IProjectClassificationCache ClassificationCache => _provider.GetRequiredService<IProjectClassificationCache>();
        public IExtensionCallbackQueue CallbackQueue => _provider.GetRequiredService<IExtensionCallbackQueue>();
        public IQuickShellLifetime Lifetime => _provider.GetRequiredService<IQuickShellLifetime>();
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
