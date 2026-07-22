using Microsoft.Extensions.DependencyInjection;
using QuickShell;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using System.Reflection;
using System.Threading;

namespace QuickShell.Core.Tests.Performance;

/// <summary>
/// Deterministic, CI-blocking assertions for the documented critical-path contract (see
/// docs/architecture/performance.md). These use instrumented fakes/counters, never
/// wall-clock thresholds, so they are safe to run on every machine and in CI.
///
/// Root-palette contracts (single snapshot per query, generation guard against stale
/// results, one-character/local-hit suppression of git search, revision-driven index
/// rebuild) are already covered by <c>RootPaletteSearchTests</c> and are intentionally
/// not duplicated here.
/// </summary>
[Collection(PerformanceHarnessIsolation.Name)]
public sealed class CriticalPathContractTests : IDisposable
{
    private readonly string _tempRoot;

    public CriticalPathContractTests()
    {
        _tempRoot = Path.Join(Path.GetTempPath(), "qs-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    // --- Provider construction ----------------------------------------------------------

    [Fact]
    public void ProviderConstruction_DoesNotScanForGitRepositories()
    {
        var localAppData = Path.Join(_tempRoot, "localappdata");
        Directory.CreateDirectory(localAppData);
        using var appDataScope = new AppDataRoot.TestScope(localAppData);

        using var provider = new QuickShellCommandsProvider();

        // Construction itself must not have started a refresh — the staged warmup
        // coordinator schedules discovery for later, after the first real workspace
        // list is published (see StartupWarmupStages.GitIndexWarmup).
        var contextField = typeof(QuickShellCommandsProvider).GetField(
            "_context", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(contextField);
        var context = (QuickShellPageContext)contextField.GetValue(provider)!;

        Assert.False(context.Services.GitRepos.IsRefreshInFlight);
    }

    // --- First list construction ---------------------------------------------------------

    [Fact]
    public void PageConstruction_DoesNotAccessTheRepository()
    {
        var repository = new FakeShortcutRepository([BuildShortcut("ws-1", _tempRoot)]);
        var context = BuildPageContext(repository, out _);

        using var page = new QuickShellPage(context);

        Assert.Equal(0, repository.GetSnapshotCallCount);
    }

    [Fact]
    public void FirstListConstruction_RunsNoGitProcess()
    {
        var gitInvocations = 0;
        var bundle = LaunchTestServices.CreateBundle(
            git: LaunchTestServices.CreateGit(runGit: (_, _) =>
            {
                Interlocked.Increment(ref gitInvocations);
                return GitCommandResult.Failed;
            }));

        var repository = new FakeShortcutRepository(
        [
            BuildShortcut("ws-1", _tempRoot),
            BuildShortcut("ws-2", _tempRoot),
            BuildShortcut("ws-3", _tempRoot),
        ]);
        var context = BuildPageContext(repository, out _, bundle);
        using var page = new QuickShellPage(context);

        var items = page.GetItems();

        Assert.True(items.Length >= 3);
        Assert.Equal(0, Volatile.Read(ref gitInvocations));
    }

    [Theory]
    [InlineData(@"\\wsl$\NoSuchDistro-QuickShellContract\home\dev")]
    [InlineData(@"\\wsl.localhost\NoSuchDistro-QuickShellContract\home\dev")]
    [InlineData(@"\\no-such-server-quickshell-contract\share\repo")]
    public void FirstListConstruction_WslAndUncPaths_DoesNotSynchronouslyProbeDirectoryExistence(string directory)
    {
        var directoryExistenceProbes = 0;
        var scheduledDirectoryProbes = 0;
        ShortcutValidation.DirectoryExistsOverride = _ =>
        {
            Interlocked.Increment(ref directoryExistenceProbes);
            return false;
        };
        QuickShellPage.DirectoryRepairProbeSchedulerOverride =
            _ => Interlocked.Increment(ref scheduledDirectoryProbes);

        try
        {
            var shortcut = BuildShortcut("ws-remote", directory);
            var repository = new FakeShortcutRepository([shortcut]);
            var context = BuildPageContext(repository, out _);
            using var page = new QuickShellPage(context);

            var items = page.GetItems();

            Assert.True(items.Length >= 1);
            Assert.Equal(0, Volatile.Read(ref directoryExistenceProbes));
            Assert.Equal(1, Volatile.Read(ref scheduledDirectoryProbes));
        }
        finally
        {
            QuickShellPage.DirectoryRepairProbeSchedulerOverride = null;
            ShortcutValidation.DirectoryExistsOverride = null;
        }
    }

    [Fact]
    public void FirstListConstruction_DoesNotBuildContextMenusEagerly()
    {
        ShortcutContextCommands.ResetBuildInvocationCount();

        var repository = new FakeShortcutRepository(
        [
            BuildShortcut("ws-1", _tempRoot),
            BuildShortcut("ws-2", _tempRoot),
            BuildShortcut("ws-3", _tempRoot),
        ]);
        var context = BuildPageContext(repository, out _);
        using var page = new QuickShellPage(context);

        var items = page.GetItems();

        Assert.True(items.Length >= 3);
        Assert.Equal(0, ShortcutContextCommands.BuildInvocationCount);

        // Selecting a workspace row (host SlowInitialize) materializes that row's menu only.
        var workspaceRow = items.OfType<LazyMoreCommandsListItem>().FirstOrDefault();
        Assert.NotNull(workspaceRow);
        Assert.False(workspaceRow.HasBuiltMoreCommands);

        _ = workspaceRow.MoreCommands;

        Assert.True(workspaceRow.HasBuiltMoreCommands);
        Assert.True(ShortcutContextCommands.BuildInvocationCount > 0);
    }

    // --- Root palette ---------------------------------------------------------------------

    [Fact]
    public void RootPaletteQuery_OneCharacter_NeverCallsGitSearch()
    {
        // Name/directory/profile carry no 'z', so this one-character query cannot match
        // locally either — it must short-circuit before ever reaching git search (queries
        // under two characters are too broad to scan the filesystem for).
        var shortcut = new TerminalShortcut
        {
            Id = "ws-1",
            Name = "Sample workspace",
            Directory = _tempRoot,
        };
        var snapshot = new WorkspaceRepositorySnapshot(1, [shortcut], []);
        var index = new RootPaletteSearchIndex(snapshot, new TerminalCatalog(new WtProfilesService()));
        var gitIndex = new CountingGitRepoIndex();

        var result = index.Search("z", gitIndex);

        Assert.Equal(RootPaletteResultKind.None, result.Kind);
        Assert.Equal(0, gitIndex.SearchCalls);
    }

    [Fact]
    public void RootPaletteQuery_StrongLocalMatch_DoesNotFallThroughToGitSearch()
    {
        var shortcut = BuildShortcut("ws-1", _tempRoot);
        shortcut.Name = "UniqueLocalWorkspaceName";
        var snapshot = new WorkspaceRepositorySnapshot(1, [shortcut], []);
        var index = new RootPaletteSearchIndex(snapshot, new TerminalCatalog(new WtProfilesService()));
        var gitIndex = new CountingGitRepoIndex();

        var result = index.Search("UniqueLocalWorkspaceName", gitIndex);

        Assert.Equal(RootPaletteResultKind.Workspaces, result.Kind);
        Assert.Equal(0, gitIndex.SearchCalls);
    }

    // --- Launch ------------------------------------------------------------------------

    [Fact]
    public void Launch_EvaluatesHealthAndGitEveryCall_NeverMemoizesAcrossLaunches()
    {
        LaunchTestServices.ApplyTerminalDiscoveryStubs();
        try
        {
            var healthCalls = 0;
            var gitStatusCalls = 0;
            var launchDirectory = Path.Join(_tempRoot, "launch-target");
            Directory.CreateDirectory(launchDirectory);
            var gitOps = LaunchTestServices.CreateGit(getStatus: _ =>
            {
                Interlocked.Increment(ref gitStatusCalls);
                return new WorkspaceGitStatus("main", IsDirty: false, IsDetached: false);
            });
            var health = new CountingHealthChecker(() => Interlocked.Increment(ref healthCalls));
            var catalog = new TerminalCatalog(new WtProfilesService());
            var terminal = new TerminalLauncher(LaunchTestServices.CreateProcessStarter(), catalog);
            var companion = new CompanionAppLauncher(LaunchTestServices.CreateProcessStarter());
            var gate = new WorkspaceGitLaunchGate(gitOps, new FakeWorktreeBranchTargetStore(_ => "main"));
            var executor = new ShortcutLaunchExecutor(terminal, health, companion, gate, catalog: catalog);

            var shortcut = BuildShortcut("ws-launch", launchDirectory);

            _ = executor.Launch(shortcut, "wt", "wt-default");
            _ = executor.Launch(shortcut, "wt", "wt-default");
            _ = executor.Launch(shortcut, "wt", "wt-default");

            // Nothing about launch should short-circuit after the first call — trust and
            // health are re-derived from current repository/process state every time.
            Assert.Equal(3, Volatile.Read(ref healthCalls));
            Assert.Equal(3, Volatile.Read(ref gitStatusCalls));
        }
        finally
        {
            LaunchTestServices.ResetTerminalDiscoveryStubs();
        }
    }

    // --- Fixtures ------------------------------------------------------------------------

    private static TerminalShortcut BuildShortcut(string id, string directory) =>
        new()
        {
            Id = id,
            Name = "Workspace " + id,
            Directory = directory,
            Command = "echo " + id,
        };

    private static QuickShellPageContext BuildPageContext(
        IShortcutRepository repository,
        out QuickShellServices services,
        LaunchTestBundle? launchBundle = null)
    {
        var collection = new ServiceCollection();
        collection.AddQuickShellCore();
        var provider = collection.BuildServiceProvider();
        var drafts = provider.GetRequiredService<IDraftStore>();
        var analysis = provider.GetRequiredService<IProjectAnalysisService>();
        var settings = new QuickShellSettingsManager();
        var lifetime = provider.GetRequiredService<IQuickShellLifetime>();
        services = TestQuickShellServicesFactory.Create(repository, drafts, settings, analysis, lifetime, launchBundle);

        return new QuickShellPageContext(
            new QuickShellHostServices(services),
            new CreateShortcutCommand(() => { }, services),
            () => { });
    }

    private sealed class CountingGitRepoIndex : IGitRepoIndex
    {
        public int SearchCalls { get; private set; }

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
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return [];
        }

        public IReadOnlyList<GitRepoCandidate> GetAll(
            IReadOnlyList<string>? extraRoots = null,
            CancellationToken cancellationToken = default) => [];

        public void RunAfterNextRefresh(Action callback)
        {
        }

        public bool TryRunAfterNextRefreshIfInFlight(Action callback) => false;
    }

    private sealed class CountingHealthChecker : IWorkspaceHealthChecker
    {
        private readonly Action _onCheck;

        public CountingHealthChecker(Action onCheck)
        {
            _onCheck = onCheck;
        }

        public WorkspaceHealthResult Check(
            TerminalShortcut shortcut,
            string terminalApplicationId,
            string defaultProfileId,
            bool includeVolatile = true,
            bool includeGit = true)
        {
            _onCheck();
            return new WorkspaceHealthResult([]);
        }

        public WorkspaceHealthResult CheckEntry(
            TerminalShortcut shortcut,
            WorkspaceEntry entry,
            string terminalApplicationId,
            string defaultProfileId,
            bool includeVolatile = true,
            bool includeGit = true)
        {
            _onCheck();
            return new WorkspaceHealthResult([]);
        }
    }
}
