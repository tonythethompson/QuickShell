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
    private readonly string? _originalLocalAppData;
    private readonly StringBuilderTraceListener _trace;
    private readonly ITestOutputHelper _output;

    public StartupPerformanceMeasurementsTests(ITestOutputHelper output)
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "qs-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        // Isolate settings.json / shortcuts.json from the real install.
        _localAppData = Path.Combine(_tempRoot, "localappdata");
        Directory.CreateDirectory(_localAppData);
        _originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _localAppData);

        // Keep the provider's background git prewarm from scanning the real machine.
        GitRepoDiscovery.IncludeDefaultSearchRoots = false;
        GitRepoDiscovery.DefaultRootCandidatesOverride = () => [];
        GitRepoIndex.ResetForTests();

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
        GitRepoDiscovery.IncludeDefaultSearchRoots = false;
        GitRepoDiscovery.DefaultRootCandidatesOverride = () => [scanRoot];
        GitRepoIndex.ResetForTests();

        var discoverCold = Time(() => GitRepoDiscovery.Discover([scanRoot]));
        // Warm discover: reuses prior results within the same process walk.
        var discoverWarm = Time(() => GitRepoDiscovery.Discover([scanRoot]));

        // --- Provider constructor --------------------------------------------
        GitRepoIndex.ResetForTests();
        var ctorMs = Time(() => _ = new QuickShellCommandsProvider()).TotalMilliseconds;
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
        GitRepoDiscovery.IncludeDefaultSearchRoots = true;
        GitRepoDiscovery.DefaultRootCandidatesOverride = null;
        ProjectAnalysisAccessor.Reset();
        GitRepoIndex.ResetForTests();

        var discoverCold = Time(() => GitRepoDiscovery.Discover());
        var discoverWarm = Time(() => GitRepoDiscovery.Discover());

        // Provider ctor against an isolated settings store (real git roots still prewarm).
        GitRepoIndex.ResetForTests();
        var ctorMs = Time(() => _ = new QuickShellCommandsProvider()).TotalMilliseconds;
        var ctorTrace = _trace.Builder.ToString();

        // List reload against a read-only copy of the real shortcuts.json.
        var listReloadMs = MeasureListReloadFromRealShortcuts(out var listGetItemsMs, out var workspaceCount);

        _output.WriteLine("=== QuickShell startup measurements (real machine) ===");
        _output.WriteLine($"Discover scan cold : {discoverCold.TotalMilliseconds:0.###} ms (real profile)");
        _output.WriteLine($"Discover scan warm : {discoverWarm.TotalMilliseconds:0.###} ms (real profile)");
        _output.WriteLine($"Provider ctor      : {ctorMs:0.###} ms");
        _output.WriteLine($"List reload (cold) : {listReloadMs.TotalMilliseconds:0.###} ms ({workspaceCount} workspaces)");
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
        var qsServices = new QuickShellServices(repository, drafts, settings, analysis, lifetime);

        workspaceCount = repository.GetShortcuts().Count;
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
            var reload = Time(() => page.Reload());
            getItemsMs = Time(() => page.GetItems());
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
        var qsServices = new QuickShellServices(repository, drafts, settings, analysis, lifetime);

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
        var reload = Time(() => page.Reload());
        getItemsMs = Time(() => page.GetItems());
        // static locator removed; pages receive services via constructor

        return reload;
    }

    private static TimeSpan Time(Action action)
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
        Trace.Listeners.Remove(_trace);
        Environment.SetEnvironmentVariable("QUICKSHELL_STARTUP_TRACE", null);
        GitRepoDiscovery.IncludeDefaultSearchRoots = true;
        GitRepoDiscovery.DefaultRootCandidatesOverride = null;
        GitRepoIndex.ResetForTests();

        if (_originalLocalAppData is null)
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", null);
        }
        else
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _originalLocalAppData);
        }

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
