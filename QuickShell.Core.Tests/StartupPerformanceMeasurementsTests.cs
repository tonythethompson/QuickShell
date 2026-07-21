using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using QuickShell.Abstractions;
using QuickShell.Classification;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace QuickShell.Core.Tests;

/// <summary>
/// Micro-benchmarks for cold startup paths: provider construction, home-list reload,
/// and git-repo discover scan. Numbers are machine/repo-size dependent; the test prints
/// them via the output helper and asserts only that the paths complete.
/// </summary>
[Collection(StartupPerfIsolation.Name)]
public sealed class StartupPerformanceMeasurementsTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _localAppData;
    private readonly StringBuilderTraceListener _trace;
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly GitRepoDiscovery.TestScope _gitScope;
    private readonly AppDataRoot.TestScope _appDataScope;

    public StartupPerformanceMeasurementsTests(ITestOutputHelper output)
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "qs-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        // Isolate settings.json / shortcuts.json from the real install.
        _localAppData = Path.Combine(_tempRoot, "localappdata");
        Directory.CreateDirectory(_localAppData);
        _appDataScope = new AppDataRoot.TestScope(_localAppData);

        // Keep the provider's background git prewarm from scanning the real machine.
        _gitScope = new GitRepoDiscovery.TestScope(includeDefaultSearchRoots: false, defaultRootCandidates: []);

        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();

        // Capture the provider ctor's nested StartupPerformanceTrace output.
        Environment.SetEnvironmentVariable("QUICKSHELL_STARTUP_TRACE", "1");
        _output = output;
        _trace = new StringBuilderTraceListener(output);
        Trace.Listeners.Add(_trace);
    }

    [Fact]
    public void Measure_ProviderCtor_ListReload_DiscoverScan()
    {
        // --- Discover scan (cold) on a controlled temp tree -------------------
        var scanRoot = Path.Combine(_tempRoot, "repos");
        BuildGitRepoTree(scanRoot, repoCount: 25);
        using var methodScope = new GitRepoDiscovery.TestScope(includeDefaultSearchRoots: false, defaultRootCandidates: [scanRoot]);

        var discoverCold = TimeCold(() => GitRepoDiscovery.Discover(_projectAnalysis, [scanRoot]));
        // Warm discover: reuses prior results within the same process walk.
        var discoverWarm = TimeWarm(() => GitRepoDiscovery.Discover(_projectAnalysis, [scanRoot]));

        // --- Provider constructor --------------------------------------------
        var ctorMs = TimeCold(() => _ = new QuickShellCommandsProvider()).TotalMilliseconds;
        var ctorTrace = _trace.Builder.ToString();

        // --- Home list reload (cold build + warm read) ------------------------
        var listReloadMs = MeasureListReload(out var listGetItemsMs, workspaceCount: 50);

        _output.WriteLine("=== QuickShell startup measurements (synthetic) ===");
        _output.WriteLine($"Discover scan cold : {discoverCold.TotalMilliseconds:0.###} ms");
        _output.WriteLine($"Discover scan warm : {discoverWarm.TotalMilliseconds:0.###} ms");
        _output.WriteLine($"Provider ctor      : {ctorMs:0.###} ms");
        _output.WriteLine($"List reload (cold) : {listReloadMs.TotalMilliseconds:0.###} ms");
        _output.WriteLine($"List GetItems warm : {listGetItemsMs.TotalMilliseconds:0.###} ms");
        if (!string.IsNullOrWhiteSpace(ctorTrace))
        {
            _output.WriteLine("Provider ctor breakdown (QUICKSHELL_STARTUP_TRACE):");
            _output.WriteLine(ctorTrace.TrimEnd());
        }

        Assert.True(discoverCold.TotalMilliseconds >= 0);
        Assert.True(ctorMs >= 0);
        Assert.True(listReloadMs.TotalMilliseconds >= 0);
    }

    /// <summary>
    /// Representative numbers for this machine: scans the real user profile / drives for git
    /// repos and loads the actual saved workspaces from a read-only copy of shortcuts.json.
    /// Nothing on disk is mutated.
    /// </summary>
    [Fact]
    public void Measure_RealMachine_DiscoverScan_And_ListReload()
    {
        // Use the real search roots (user profile common folders + all drives).
        using var methodScope = new GitRepoDiscovery.TestScope(includeDefaultSearchRoots: true, defaultRootCandidates: null);

        var discoverCold = TimeCold(() => GitRepoDiscovery.Discover(_projectAnalysis));
        var discoverWarm = TimeWarm(() => GitRepoDiscovery.Discover(_projectAnalysis));

        // Provider ctor against an isolated settings store (real git roots still prewarm).
        var ctorMs = TimeCold(() => _ = new QuickShellCommandsProvider()).TotalMilliseconds;
        var ctorTrace = _trace.Builder.ToString();

        // List reload against a read-only copy of the real shortcuts.json.
        var listReloadMs = MeasureListReloadFromRealShortcuts(out var listGetItemsMs, out var workspaceCount);

        _output.WriteLine("=== QuickShell startup measurements (real machine) ===");
        _output.WriteLine($"Discover scan cold : {discoverCold.TotalMilliseconds:0.###} ms (real profile)");
        _output.WriteLine($"Discover scan warm : {discoverWarm.TotalMilliseconds:0.###} ms (real profile)");
        _output.WriteLine($"Provider ctor      : {ctorMs:0.###} ms");
        _output.WriteLine($"List reload (cold) : {listReloadMs.TotalMilliseconds:0.###} ms ({workspaceCount} workspaces)");
        _output.WriteLine($"List GetItems warm : {listGetItemsMs.TotalMilliseconds:0.###} ms");
        WriteRealMachineWorkspaceCountArtifact(workspaceCount);
        if (!string.IsNullOrWhiteSpace(ctorTrace))
        {
            _output.WriteLine("Provider ctor breakdown (QUICKSHELL_STARTUP_TRACE):");
            _output.WriteLine(ctorTrace.TrimEnd());
        }

        Assert.True(discoverCold.TotalMilliseconds >= 0);
        Assert.True(ctorMs >= 0);
        Assert.True(listReloadMs.TotalMilliseconds >= 0);
        Assert.True(workspaceCount >= 0);
    }

    [Fact]
    public void Measure_WarmupStartsAfterFirstList()
    {
        // Build a provider exactly as CmdPal does. No warmup should run yet.
        using var provider = new QuickShellCommandsProvider();
        var traceBeforeFirstList = _trace.Builder.ToString();

        // Get the top-level page and force the first list; this is the signal.
        var commands = provider.TopLevelCommands();
        var commandItem = commands[0];
        var page = (QuickShellPage)commandItem.Command;
        var getItemsMs = TimeCold(() => page.GetItems()).TotalMilliseconds;

        // Wait for the staged coordinator to finish.
        var coordinatorField = typeof(QuickShellCommandsProvider).GetField(
            "_warmupCoordinator",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(coordinatorField);
        var coordinator = coordinatorField.GetValue(provider)!;
        var isCompletedProperty = coordinator.GetType().GetProperty("IsCompleted");
        Assert.NotNull(isCompletedProperty);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!(bool)isCompletedProperty.GetValue(coordinator)! && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        var traceAfterWarmup = _trace.Builder.ToString();
        _output.WriteLine("=== Staged warmup trace ===");
        _output.WriteLine($"GetItems (first list) : {getItemsMs:0.###} ms");
        _output.WriteLine(traceAfterWarmup.TrimEnd());

        Assert.DoesNotContain("Warmup stage", traceBeforeFirstList);
        Assert.Contains("Warmup stage", traceAfterWarmup);
    }

    private TimeSpan MeasureListReloadFromRealShortcuts(out TimeSpan getItemsMs, out int workspaceCount)
    {
        var configDir = Path.Combine(_tempRoot, "real-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);

        // Copy the real shortcuts.json read-only into the temp config dir so the list is
        // built from the user's actual saved workspaces without mutating the real file.
        var realShortcuts = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickShell",
            "shortcuts.json");
        var copied = false;
        if (File.Exists(realShortcuts))
        {
            File.Copy(realShortcuts, Path.Combine(configDir, "shortcuts.json"), overwrite: true);
            copied = true;
        }

        var services = new ServiceCollection();
        services.AddQuickShellCore(configDir);
        using var provider = services.BuildServiceProvider();
        var repository = (ShortcutRepository)provider.GetRequiredService<IShortcutRepository>();
        var drafts = (ShortcutDraftStore)provider.GetRequiredService<IDraftStore>();
        var analysis = provider.GetRequiredService<IProjectAnalysisService>();
        var settings = new QuickShellSettingsManager();
        var lifetime = provider.GetRequiredService<IQuickShellLifetime>();
        var qsServices = TestQuickShellServicesFactory.CreateFromProvider(
            provider,
            repository,
            drafts,
            settings,
            analysis,
            lifetime);

        var originalWorkspaceCount = repository.GetShortcuts().Count;
        workspaceCount = originalWorkspaceCount;
        if (workspaceCount == 0)
        {
            // No real workspaces saved; fall back to a synthetic 50 so the reload path is exercised.
            for (var i = 0; i < 50; i++)
            {
                repository.Upsert(new TerminalShortcut
                {
                    Id = "ws-" + i,
                    Name = "Workspace " + i,
                    Directory = configDir,
                    Command = "echo " + i,
                });
            }

            workspaceCount = repository.GetShortcuts().Count;
        }

        var hostServices = new QuickShellHostServices(qsServices);
        var pageContext = new QuickShellPageContext(hostServices, new CreateShortcutCommand(() => { }, qsServices), () => { });
        using var page = new QuickShellPage(pageContext);
        try
        {
            var reload = TimeCold(() => page.Reload());
            getItemsMs = TimeWarm(() => page.GetItems());
            return reload;
        }
        finally
        {
            // static locator removed; pages receive services via constructor

            if (copied)
            {
                try
                {
                    File.Delete(Path.Combine(configDir, "shortcuts.json"));
                }
                catch
                {
                    // Best effort.
                }
            }
        }
    }

    private TimeSpan MeasureListReload(out TimeSpan getItemsMs, int workspaceCount)
    {
        var configDir = Path.Combine(_tempRoot, "list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);

        var services = new ServiceCollection();
        services.AddQuickShellCore(configDir);
        using var provider = services.BuildServiceProvider();
        var repository = (ShortcutRepository)provider.GetRequiredService<IShortcutRepository>();
        var drafts = (ShortcutDraftStore)provider.GetRequiredService<IDraftStore>();
        var analysis = provider.GetRequiredService<IProjectAnalysisService>();
        var settings = new QuickShellSettingsManager();
        var lifetime = provider.GetRequiredService<IQuickShellLifetime>();
        var qsServices = TestQuickShellServicesFactory.CreateFromProvider(
            provider,
            repository,
            drafts,
            settings,
            analysis,
            lifetime);

        for (var i = 0; i < workspaceCount; i++)
        {
            repository.Upsert(new TerminalShortcut
            {
                Id = "ws-" + i,
                Name = "Workspace " + i,
                Directory = configDir,
                Command = "echo " + i,
            });
        }

        // Reproduce exactly how the provider builds its home page.
        var hostServices = new QuickShellHostServices(qsServices);
        var pageContext = new QuickShellPageContext(hostServices, new CreateShortcutCommand(() => { }, qsServices), () => { });
        using var page = new QuickShellPage(pageContext);
        var reload = TimeCold(() => page.Reload());
        getItemsMs = TimeWarm(() => page.GetItems());
        // static locator removed; pages receive services via constructor

        return reload;
    }

    // Measures the first execution, including cache population and first-call costs.
    private static TimeSpan TimeCold(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed;
    }

    // Performs one warm-up invocation, then measures the following execution.
    private static TimeSpan TimeWarm(Action action)
    {
        // Warm up once so JIT / first-call costs don't dominate the reported number.
        action();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed;
    }

    private static void WriteRealMachineWorkspaceCountArtifact(int workspaceCount)
    {
        var directory = Environment.GetEnvironmentVariable("QUICKSHELL_PERF_OUTPUT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            var probe = new DirectoryInfo(AppContext.BaseDirectory);
            while (probe is not null && !File.Exists(Path.Join(probe.FullName, "QuickShell.sln")))
            {
                probe = probe.Parent;
            }

            directory = Path.Join(probe?.FullName ?? AppContext.BaseDirectory, "artifacts", "perf");
        }

        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "real-machine-workspace-count.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "workspaceCount": {{workspaceCount}},
              "capturedAtUtc": "{{DateTimeOffset.UtcNow:O}}"
            }
            """);
    }

    private static void BuildGitRepoTree(string root, int repoCount)
    {
        Directory.CreateDirectory(root);
        for (var i = 0; i < repoCount; i++)
        {
            // Deeper nesting to exercise the scanner's depth/parallelism.
            var dir = Path.Combine(root, "group-" + (i % 5), "project-" + i);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, ".git"));
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
        Trace.Listeners.Remove(_trace);
        Environment.SetEnvironmentVariable("QUICKSHELL_STARTUP_TRACE", null);
        _gitScope.Dispose();
        _appDataScope.Dispose();

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
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StartupPerfIsolation
{
    public const string Name = "StartupPerf";
}

/// <summary>Forwards Trace writes to the xUnit output helper so benchmark output is visible.</summary>
public sealed class StringBuilderTraceListener : TraceListener
{
    private readonly ITestOutputHelper _output;
    private readonly StringBuilder _builder = new();

    public StringBuilderTraceListener(ITestOutputHelper output) => _output = output;

    public StringBuilder Builder => _builder;

    public override void Write(string? message) => _builder.Append(message);

    public override void WriteLine(string? message)
    {
        _builder.AppendLine(message);
        if (_output is not null && message is not null)
        {
            _output.WriteLine(message);
        }
    }
}
