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
    private readonly string? _originalLocalAppData;

    // Every IGitRepoIndex created by CreatePageHarness. GitRepoIndex.Search/GetAll/Prewarm
    // start an async refresh Task.Run and return without waiting for it, so a scenario can
    // finish while that background call is still executing under the shared static
    // GitRepoDiscovery override. Draining these before Dispose() resets that override keeps
    // a stray in-flight call from corrupting the next test's own override/counter.
    private readonly List<IGitRepoIndex> _createdGitIndices = [];

    public PerformanceRegressionHarnessTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), "qs-perf-harness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _localAppData = Path.Combine(_tempRoot, "localappdata");
        Directory.CreateDirectory(_localAppData);
        _originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _localAppData);

        GitRepoDiscovery.IncludeDefaultSearchRoots = false;
        GitRepoDiscovery.DefaultRootCandidatesOverride = () => [];
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _originalLocalAppData);
        GitRepoDiscovery.IncludeDefaultSearchRoots = true;
        GitRepoDiscovery.DefaultRootCandidatesOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
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
        MeasureLaunch(report);

        DrainAllGitActivity();

        var directory = report.WriteArtifacts();
        _output.WriteLine($"Wrote {report.Results.Count} benchmark results to {directory}");
        _output.WriteLine(File.ReadAllText(Path.Combine(directory, "quickshell-perf-results.md")));

        Assert.True(report.Results.Count > 0);
        Assert.True(File.Exists(Path.Combine(directory, "quickshell-perf-results.json")));
        Assert.True(File.Exists(Path.Combine(directory, "quickshell-perf-results.md")));
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

        var (context, page) = CreatePageHarness(workspaceCount: 0, out _, out _, out _);
        report.Add(BenchmarkRunner.MeasureOnce(
            "first placeholder GetItems (no workspaces loaded yet)",
            category,
            () => _ = page.GetItems()));

        var (warmupContext, warmupPage) = CreatePageHarness(workspaceCount: 25, out var services, out var settings, out var lifetime);
        _ = warmupPage.GetItems(); // publish the first real list before warmup starts
        report.Add(BenchmarkRunner.MeasureOnce(
            "first real workspace list (25 workspaces)",
            category,
            warmupPage.Reload));

        var warmupCtx = new StartupWarmupContext(services, settings, lifetime);
        var stages = StartupWarmupStages.Create(warmupCtx);
        using var coordinator = new StartupWarmupCoordinator(lifetime, warmupCtx, stages);
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
        var (context, page) = CreatePageHarness(workspaceCount, out _, out _, out _, includeMixedShapes: true);

        var coldStats = BenchmarkRunner.MeasureOnce(
            $"cold home-list construction ({workspaceCount} workspaces)",
            category,
            () => _ = page.GetItems());
        report.Add(coldStats);

        var warmStats = BenchmarkRunner.Measure(
            $"warm home-list construction ({workspaceCount} workspaces)",
            category,
            () => page.Reload(),
            iterations: workspaceCount >= ShortcutValidation.MaxShortcutCount ? 3 : 5);
        report.Add(warmStats);
    }

    // --- Root palette --------------------------------------------------------------------

    private void MeasureRootPalette(BenchmarkReport report)
    {
        const string category = "root-palette";
        var (context, page) = CreatePageHarness(workspaceCount: 200, out var services, out _, out _);
        var snapshot = services.Shortcuts.GetSnapshot();
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

        var index = new RootPaletteSearchIndex(snapshot);

        report.Add(BenchmarkRunner.Measure(
            "abbreviation hit",
            category,
            () => _ = index.Search(abbreviationHit, services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "workspace-name hit",
            category,
            () => _ = index.Search(nameHit, services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "task-action hit",
            category,
            () => _ = index.Search("echo", services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "no local hit (falls through to git search)",
            category,
            () => _ = index.Search("zzz-no-such-workspace-zzz", services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "one-character query (suppressed before git search)",
            category,
            () => _ = index.Search("a", services.GitRepos)));

        report.Add(BenchmarkRunner.Measure(
            "explicit discover query",
            category,
            () => _ = index.Search("discover", services.GitRepos)));

        var indexBuildCold = BenchmarkRunner.MeasureOnce(
            "cold query index build (200 workspaces)",
            category,
            () => _ = new RootPaletteSearchIndex(snapshot));
        report.Add(indexBuildCold);

        report.Add(BenchmarkRunner.Measure(
            "warm cached query index (same revision, reused)",
            category,
            () =>
            {
                var reused = index.Revision == snapshot.Version ? index : new RootPaletteSearchIndex(snapshot);
                _ = reused.Search(nameHit, services.GitRepos);
            }));

        var bumped = new WorkspaceRepositorySnapshot(snapshot.Version + 1, snapshot.Shortcuts, snapshot.Layout);
        report.Add(BenchmarkRunner.MeasureOnce(
            "repository-version invalidation (index rebuild after version bump)",
            category,
            () => _ = new RootPaletteSearchIndex(bumped)));
    }

    // --- Git discovery ---------------------------------------------------------------

    private void MeasureGitDiscovery(BenchmarkReport report)
    {
        const string category = "git-discovery";
        var projectAnalysis = BuildProjectAnalysisService();

        foreach (var repoCount in new[] { 10, 100 })
        {
            var scanRoot = Path.Combine(_tempRoot, "git-scan-" + repoCount);
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

        var maxRoot = Path.Combine(_tempRoot, "git-scan-max");
        BuildGitRepoTree(maxRoot, 50); // GitRepoDiscovery.MaxRepos caps results at 50 regardless of tree size
        report.Add(BenchmarkRunner.MeasureOnce(
            "cold discover (maximum supported entries)",
            category,
            () => _ = GitRepoDiscovery.Discover(projectAnalysis, [maxRoot])));

        var failedRoot = Path.Combine(_tempRoot, "git-scan-missing");
        report.Add(BenchmarkRunner.MeasureOnce(
            "failed refresh (nonexistent root)",
            category,
            () => _ = GitRepoDiscovery.Discover(projectAnalysis, [failedRoot])));
    }

    // --- Launch ------------------------------------------------------------------------

    private void MeasureLaunch(BenchmarkReport report)
    {
        const string category = "launch";
        LaunchTestServices.ApplyTerminalDiscoveryStubs();
        try
        {
            var bundle = LaunchTestServices.CreateBundle();
            var directory = Path.Combine(_tempRoot, "launch-target");
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

    private (QuickShellPageContext Context, QuickShellPage Page) CreatePageHarness(
        int workspaceCount,
        out QuickShellServices services,
        out QuickShellSettingsManager settings,
        out IQuickShellLifetime lifetime,
        bool includeMixedShapes = false)
    {
        var configDir = Path.Combine(_tempRoot, "cfg-" + Guid.NewGuid().ToString("N"));
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
        settings = new QuickShellSettingsManager();
        lifetime = provider.GetRequiredService<IQuickShellLifetime>();
        services = TestQuickShellServicesFactory.Create(repository, drafts, settings, analysis, lifetime);
        _createdGitIndices.Add(services.GitRepos);

        var context = new QuickShellPageContext(
            new QuickShellHostServices(services),
            new CreateShortcutCommand(() => { }, services),
            () => { });
        var page = new QuickShellPage(context);
        return (context, page);
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
        // list construction (see CriticalPathContractTests.FirstListConstruction_WslAndUncPaths).
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

    private static IProjectAnalysisService BuildProjectAnalysisService()
    {
        var collection = new ServiceCollection();
        collection.AddQuickShellCore();
        var provider = collection.BuildServiceProvider();
        return provider.GetRequiredService<IProjectAnalysisService>();
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
            var dir = Path.Combine(root, "group-" + (i % 5), "project-" + i);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, ".git"));
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceHarnessIsolation
{
    public const string Name = "PerformanceHarness";
}
