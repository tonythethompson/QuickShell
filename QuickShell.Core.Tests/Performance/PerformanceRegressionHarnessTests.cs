using Microsoft.Extensions.DependencyInjection;
using QuickShell;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using System.Diagnostics;
using Xunit.Abstractions;

namespace QuickShell.Core.Tests.Performance;

/// <summary>
/// Consolidated performance regression harness: provider activation, first paint,
/// root-palette search, Git-index availability, row construction, and launch planning.
/// Produces a JSON + Markdown artifact under <c>artifacts/perf</c> (override with
/// <c>QUICKSHELL_PERF_OUTPUT_DIR</c>). Wall-clock numbers are machine dependent; the
/// assertions here only confirm each scenario runs and shapes are sane — deterministic
/// contracts live in <see cref="CriticalPathContractTests"/> instead.
///
/// Run in isolation with:
///   dotnet test QuickShell.Core.Tests/QuickShell.Core.Tests.csproj -c Release -p:Platform=x64 --filter Category=PerformanceMeasurement
/// </summary>
[Trait("Category", "PerformanceMeasurement")]
[Collection(PerformanceHarnessIsolation.Name)]
public sealed class PerformanceRegressionHarnessTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempRoot;
    private readonly string _localAppData;

    // Every IGitRepoIndex created by CreatePageHarness. GitRepoIndex.Search/GetAll/Prewarm
    // start an async refresh Task.Run and return without waiting for it, so a scenario can
    // finish while that background call is still executing. Draining these before Dispose()
    // avoids a stray in-flight scan touching _tempRoot after Directory.Delete runs.
    private readonly List<IGitRepoIndex> _createdGitIndices = [];
    private readonly GitRepoDiscovery.TestScope _gitScope;
    private readonly AppDataRoot.TestScope _appDataScope;

    public PerformanceRegressionHarnessTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Join(Path.GetTempPath(), "qs-perf-harness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _localAppData = Path.Join(_tempRoot, "localappdata");
        Directory.CreateDirectory(_localAppData);
        _appDataScope = new AppDataRoot.TestScope(_localAppData);

        _gitScope = new GitRepoDiscovery.TestScope(includeDefaultSearchRoots: false, defaultRootCandidates: []);
    }

    public void Dispose()
    {
        DrainAllGitActivity();
        _appDataScope.Dispose();
        _gitScope.Dispose();
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

    [Fact]
    public void RunHarness_AllScenarios_ProducesJsonAndMarkdownArtifacts()
    {
        var report = new BenchmarkReport();

        MeasureProviderAndStartup(report);
        foreach (var count in new[] { 10, 100, ShortcutValidation.MaxShortcutCount })
        {
            MeasureWorkspaceListConstruction(report, count);
        }

        MeasureRootPalette(report);
        MeasureGitDiscovery(report);
        MeasureTerminalDiscovery(report);
        MeasureLaunch(report);

        DrainAllGitActivity();

        var directory = report.WriteArtifacts();
        _output.WriteLine($"Wrote {report.Results.Count} benchmark results to {directory}");
        _output.WriteLine(File.ReadAllText(Path.Join(directory, "quickshell-perf-results.md")));

        Assert.True(report.Results.Count > 0);
        Assert.True(File.Exists(Path.Join(directory, "quickshell-perf-results.json")));
        Assert.True(File.Exists(Path.Join(directory, "quickshell-perf-results.md")));
    }

    // --- Provider / startup ------------------------------------------------------------

    private void MeasureProviderAndStartup(BenchmarkReport report)
    {
        const string category = "provider-startup";

        QuickShellCommandsProvider? provider = null;
        var ctorStats = BenchmarkRunner.MeasureOnce(
            "provider constructor",
            category,
            () => provider = new QuickShellCommandsProvider());
        provider!.Dispose();
        report.Add(ctorStats);

        using (var placeholderHarness = CreatePageHarness(workspaceCount: 0))
        {
            report.Add(BenchmarkRunner.MeasureOnce(
                "first placeholder GetItems (no workspaces loaded yet)",
                category,
                () => _ = placeholderHarness.Page.GetItems()));
        }

        using var warmupHarness = CreatePageHarness(workspaceCount: 25);
        report.Add(BenchmarkRunner.MeasureOnce(
            "first real workspace list (25 workspaces)",
            category,
            () => _ = warmupHarness.Page.GetItems()));

        var warmupCtx = new StartupWarmupContext(
            warmupHarness.Services,
            warmupHarness.Settings,
            warmupHarness.Lifetime);
        var stages = StartupWarmupStages.Create(warmupCtx);
        using var coordinator = new StartupWarmupCoordinator(
            warmupHarness.Lifetime,
            warmupCtx,
            stages);
        var startStats = BenchmarkRunner.MeasureOnce(
            "staged warmup start (signal only, stages run in background)",
            category,
            () => coordinator.SignalFirstListPublished());
        report.Add(startStats);

        var completionStopwatch = Stopwatch.StartNew();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!coordinator.IsCompleted && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        completionStopwatch.Stop();
        Assert.True(
            coordinator.IsCompleted,
            $"Warmup did not complete. Finished stages: {coordinator.StageResults.Count}/{stages.Count}");
        report.Add(new BenchmarkStats(
            "staged warmup completion (all stages, from signal)",
            category,
            1,
            completionStopwatch.Elapsed.TotalMilliseconds,
            completionStopwatch.Elapsed.TotalMilliseconds,
            completionStopwatch.Elapsed.TotalMilliseconds,
            completionStopwatch.Elapsed.TotalMilliseconds,
            0,
            new Dictionary<string, long> { ["stagesCompleted"] = coordinator.StageResults.Count },
            null));
    }

    // --- Workspace list construction ---------------------------------------------------

    private void MeasureWorkspaceListConstruction(BenchmarkReport report, int workspaceCount)
    {
        const string category = "workspace-list";
        using var harness = CreatePageHarness(workspaceCount, includeMixedShapes: true);

        var coldStats = BenchmarkRunner.MeasureOnce(
            $"cold home-list construction ({workspaceCount} workspaces)",
            category,
            () => _ = harness.Page.GetItems());
        report.Add(coldStats);

        var warmStats = BenchmarkRunner.Measure(
            $"warm home-list construction ({workspaceCount} workspaces)",
            category,
            () => harness.Page.Reload(),
            iterations: workspaceCount >= ShortcutValidation.MaxShortcutCount ? 3 : 5);
        report.Add(warmStats);
    }

    // --- Root palette --------------------------------------------------------------------

    private void MeasureRootPalette(BenchmarkReport report)
    {
        const string category = "root-palette";
        using var harness = CreatePageHarness(workspaceCount: 200);
        var snapshot = harness.Services.Shortcuts.GetSnapshot();
        var abbreviated = new TerminalShortcut
        {
            Id = "ws-abbrev",
            Name = "Abbreviated workspace",
            Abbreviation = "abw",
            Directory = snapshot.Shortcuts[0].Directory,
            Command = "echo abbrev",
        };
        var withAbbreviation = new List<TerminalShortcut>(snapshot.Shortcuts) { abbreviated };
        snapshot = new WorkspaceRepositorySnapshot(snapshot.Version, withAbbreviation, snapshot.Layout);
        var abbreviationHit = abbreviated.Abbreviation;
        var nameHit = snapshot.Shortcuts[0].Name;

        var index = new RootPaletteSearchIndex(snapshot, new TerminalCatalog(new WtProfilesService()));

        report.Add(BenchmarkRunner.Measure(
            "abbreviation hit",
            category,
            () => _ = index.Search(abbreviationHit, harness.Services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "workspace-name hit",
            category,
            () => _ = index.Search(nameHit, harness.Services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "task-action hit",
            category,
            () => _ = index.Search("echo", harness.Services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "no local hit (falls through to git search)",
            category,
            () => _ = index.Search("zzz-no-such-workspace-zzz", harness.Services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "one-character query (suppressed before git search)",
            category,
            () => _ = index.Search("a", harness.Services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "explicit discover query",
            category,
            () => _ = index.Search("discover", harness.Services.GitRepos)));

        var indexBuildCold = BenchmarkRunner.MeasureOnce(
            "cold query index build (200 workspaces)",
            category,
            () => _ = new RootPaletteSearchIndex(snapshot, new TerminalCatalog(new WtProfilesService())));
        report.Add(indexBuildCold);

        report.Add(BenchmarkRunner.Measure(
            "warm cached query index (same revision, reused)",
            category,
            () =>
            {
                var reused = index.Revision == snapshot.Version ? index : new RootPaletteSearchIndex(snapshot, new TerminalCatalog(new WtProfilesService()));
                _ = reused.Search(nameHit, harness.Services.GitRepos);
            }));

        var bumped = new WorkspaceRepositorySnapshot(snapshot.Version + 1, snapshot.Shortcuts, snapshot.Layout);
        report.Add(BenchmarkRunner.MeasureOnce(
            "repository-version invalidation (index rebuild after version bump)",
            category,
            () => _ = new RootPaletteSearchIndex(bumped, new TerminalCatalog(new WtProfilesService()))));
    }

    // --- Git discovery ---------------------------------------------------------------

    private void MeasureGitDiscovery(BenchmarkReport report)
    {
        const string category = "git-discovery";
        var collection = new ServiceCollection();
        collection.AddQuickShellCore();
        using var provider = collection.BuildServiceProvider();
        var projectAnalysis = provider.GetRequiredService<IProjectAnalysisService>();

        foreach (var repoCount in new[] { 10, 100 })
        {
            var scanRoot = Path.Join(_tempRoot, "git-scan-" + repoCount);
            BuildGitRepoTree(scanRoot, repoCount);

            report.Add(BenchmarkRunner.MeasureOnce(
                $"cold discover ({repoCount} repositories)",
                category,
                () => _ = GitRepoDiscovery.Discover(projectAnalysis, [scanRoot])));

            report.Add(BenchmarkRunner.Measure(
                $"warm discover, same roots ({repoCount} repositories)",
                category,
                () => _ = GitRepoDiscovery.Discover(projectAnalysis, [scanRoot])));
        }

        var maxRoot = Path.Join(_tempRoot, "git-scan-max");
        BuildGitRepoTree(maxRoot, 50); // GitRepoDiscovery.MaxRepos caps results at 50 regardless of tree size
        report.Add(BenchmarkRunner.MeasureOnce(
            "cold discover (maximum supported entries)",
            category,
            () => _ = GitRepoDiscovery.Discover(projectAnalysis, [maxRoot])));

        var failedRoot = Path.Join(_tempRoot, "git-scan-missing");
        report.Add(BenchmarkRunner.MeasureOnce(
            "failed refresh (nonexistent root)",
            category,
            () => _ = GitRepoDiscovery.Discover(projectAnalysis, [failedRoot])));
    }

    // --- Terminal discovery ------------------------------------------------------------

    private void MeasureTerminalDiscovery(BenchmarkReport report)
    {
        const string category = "terminal-discovery";
        var services = new ServiceCollection().AddQuickShellCore(Path.Join(_tempRoot, "term-discovery")).BuildServiceProvider();
        try
        {
            var catalog = services.GetRequiredService<ITerminalCatalog>();
            catalog.InvalidateCache();

            report.Add(BenchmarkRunner.MeasureOnce(
                "cold GetLaunchTargets (after InvalidateCache)",
                category,
                () => _ = catalog.GetLaunchTargets()));

            report.Add(BenchmarkRunner.Measure(
                "warm GetLaunchTargets (cached snapshot)",
                category,
                () => _ = catalog.GetLaunchTargets()));

            catalog.InvalidateCache();
            report.Add(BenchmarkRunner.MeasureOnce(
                "cold GetLaunchTargets rebuild (second InvalidateCache)",
                category,
                () => _ = catalog.GetLaunchTargets()));
        }
        finally
        {
            services.Dispose();
        }
    }

    // --- Launch ------------------------------------------------------------------------

    private void MeasureLaunch(BenchmarkReport report)
    {
        const string category = "launch";
        LaunchTestServices.ApplyTerminalDiscoveryStubs();
        try
        {
            var bundle = LaunchTestServices.CreateBundle();
            var directory = Path.Join(_tempRoot, "launch-target");
            Directory.CreateDirectory(directory);

            var single = new TerminalShortcut
            {
                Id = "launch-single",
                Name = "Single",
                Directory = directory,
                Command = "echo hi",
            };

            report.Add(BenchmarkRunner.MeasureOnce(
                "cold launch-plan build (single entry)",
                category,
                () => _ = bundle.Executor.Launch(single, "wt", "wt-default")));

            report.Add(BenchmarkRunner.Measure(
                "warm launch (single entry, repeated resolution)",
                category,
                () => _ = bundle.Executor.Launch(single, "wt", "wt-default")));

            var multi = new TerminalShortcut
            {
                Id = "launch-multi",
                Name = "Multi",
                Directory = directory,
                Launches =
                [
                    new WorkspaceEntry { Id = "l1", Terminal = "wt", Command = "echo one", Order = 0 },
                    new WorkspaceEntry { Id = "l2", Terminal = "wt", Command = "echo two", Order = 1 },
                    new WorkspaceEntry { Id = "l3", Terminal = "wt", Command = "echo three", Order = 2 },
                ],
            };

            report.Add(BenchmarkRunner.Measure(
                "multi-entry, independent processes",
                category,
                () => _ = bundle.Executor.Launch(multi, "wt", "wt-default")));

            var tabGroup = new TerminalShortcut
            {
                Id = "launch-tabs",
                Name = "Tabs",
                Directory = directory,
                Launches =
                [
                    new WorkspaceEntry { Id = "t1", Terminal = "wt", Command = "echo one", Order = 0 },
                    new WorkspaceEntry { Id = "t2", Terminal = "wt", Command = "echo two", Order = 1 },
                ],
            };

            report.Add(BenchmarkRunner.Measure(
                "Windows Terminal tab group (compatible entries)",
                category,
                () => _ = bundle.Executor.Launch(tabGroup, "wt", "wt-default")));

            report.Add(BenchmarkRunner.Measure(
                "settings invalidation (alternating terminal application id)",
                category,
                () =>
                {
                    _ = bundle.Executor.Launch(single, "wt", "wt-default");
                    _ = bundle.Executor.Launch(single, "cmd", "cmd-default");
                }));
        }
        finally
        {
            LaunchTestServices.ResetTerminalDiscoveryStubs();
        }
    }

    // --- Shared fixtures -----------------------------------------------------------------

    private PageHarness CreatePageHarness(
        int workspaceCount,
        bool includeMixedShapes = false)
    {
        var configDir = Path.Join(_tempRoot, "cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);

        // FakeShortcutRepository (not the on-disk repository) so mixed-shape rows — WSL, UNC,
        // and structurally invalid entries — can be seeded without real directory validation,
        // and so cold/warm timing isn't skewed by file-system caching.
        var repository = new FakeShortcutRepository(BuildWorkspaces(configDir, workspaceCount, includeMixedShapes), configDir);
        var collection = new ServiceCollection();
        collection.AddQuickShellCore();
        var provider = collection.BuildServiceProvider();
        var drafts = provider.GetRequiredService<IDraftStore>();
        var analysis = provider.GetRequiredService<IProjectAnalysisService>();
        var settings = new QuickShellSettingsManager();
        var lifetime = provider.GetRequiredService<IQuickShellLifetime>();
        var services = TestQuickShellServicesFactory.Create(repository, drafts, settings, analysis, lifetime);
        _createdGitIndices.Add(services.GitRepos);

        var context = new QuickShellPageContext(
            new QuickShellHostServices(services),
            new CreateShortcutCommand(() => { }, services),
            () => { });
        var page = new QuickShellPage(context);
        return new PageHarness(provider, page, services, settings, lifetime);
    }

    private static List<TerminalShortcut> BuildWorkspaces(string configDir, int count, bool includeMixedShapes)
    {
        var shortcuts = new List<TerminalShortcut>(count + 3);
        for (var i = 0; i < count; i++)
        {
            shortcuts.Add(new TerminalShortcut
            {
                Id = "ws-" + i,
                Name = "Workspace " + i,
                Directory = configDir,
                Command = "echo " + i,
                IsPinned = i % 10 == 0,
                PinOrder = i % 10 == 0 ? i : null,
            });
        }

        if (!includeMixedShapes || count == 0)
        {
            return shortcuts;
        }

        // WSL/UNC-style rows are structurally present but never probed for existence during
        // list construction (see
        // CriticalPathContractTests.FirstListConstruction_WslAndUncPaths_DoesNotSynchronouslyProbeDirectoryExistence).
        shortcuts.Add(new TerminalShortcut
        {
            Id = "ws-wsl",
            Name = "WSL workspace",
            Directory = @"\\wsl$\Ubuntu-QuickShellHarness\home\dev",
            Command = "echo wsl",
        });
        shortcuts.Add(new TerminalShortcut
        {
            Id = "ws-unc",
            Name = "UNC workspace",
            Directory = @"\\no-such-server-quickshell-harness\share\repo",
            Command = "echo unc",
        });
        shortcuts.Add(new TerminalShortcut
        {
            Id = "ws-invalid",
            Name = "Invalid workspace",
            Directory = string.Empty,
        });
        return shortcuts;
    }

    private void DrainAllGitActivity()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        foreach (var index in _createdGitIndices)
        {
            while (index.IsRefreshInFlight && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
        }
    }

    private static void BuildGitRepoTree(string root, int repoCount)
    {
        Directory.CreateDirectory(root);
        for (var i = 0; i < repoCount; i++)
        {
            var dir = Path.Join(root, "group-" + (i % 5), "project-" + i);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Join(dir, ".git"));
        }
    }

    private sealed class PageHarness : IDisposable
    {
        private readonly ServiceProvider _provider;

        public PageHarness(
            ServiceProvider provider,
            QuickShellPage page,
            QuickShellServices services,
            QuickShellSettingsManager settings,
            IQuickShellLifetime lifetime)
        {
            _provider = provider;
            Page = page;
            Services = services;
            Settings = settings;
            Lifetime = lifetime;
        }

        public QuickShellPage Page { get; }

        public QuickShellServices Services { get; }

        public QuickShellSettingsManager Settings { get; }

        public IQuickShellLifetime Lifetime { get; }

        public void Dispose()
        {
            Page.Dispose();
            _provider.Dispose();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceHarnessIsolation
{
    public const string Name = "PerformanceHarness";
}
