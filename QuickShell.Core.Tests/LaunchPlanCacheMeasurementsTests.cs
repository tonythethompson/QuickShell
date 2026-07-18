using QuickShell.Models;
using QuickShell.Services;
using System.Diagnostics;

namespace QuickShell.Core.Tests;

[Collection(TerminalLauncherOverrideIsolation.Name)]
public sealed class LaunchPlanCacheMeasurementsTests : IDisposable
{
    public LaunchPlanCacheMeasurementsTests()
    {
        LaunchExecutorTestEnvironment.Apply();
    }

    public void Dispose()
    {
        LaunchExecutorTestEnvironment.Reset();
    }

    [Fact]
    public void ReportColdAndWarmPlanPreparation()
    {
        var output = new List<string>();

        output.Add(MeasureScenario("one launch entry", OneEntryShortcut()));
        output.Add(MeasureScenario("five independent launch entries", FiveIndependentEntriesShortcut(), new ShortcutLaunchOptions(SeparateWindowsForMultiLaunch: true)));
        output.Add(MeasureScenario("five Windows Terminal tabs", FiveWindowsTerminalTabsShortcut()));
        output.Add(MeasureScenario("mixed terminal targets", MixedTerminalTargetsShortcut(), new ShortcutLaunchOptions(SeparateWindowsForMultiLaunch: true)));

        Console.WriteLine(string.Join(Environment.NewLine, output));
    }

    private static string MeasureScenario(string name, TerminalShortcut shortcut, ShortcutLaunchOptions options = default)
    {
        var repo = new FakeShortcutRepository([shortcut]) { Version = 1 };
        var bundle = LaunchTestServices.CreateBundle(repository: repo);
        var executor = bundle.Executor;

        // Cold: first call builds the plan and caches it.
        var (coldMs, coldAlloc) = Measure(() => executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile, options));

        // Warm: subsequent calls reuse the cached plan.
        const int warmIterations = 20;
        var warm = new List<(double Ms, long Alloc)>();
        for (var i = 0; i < warmIterations; i++)
        {
            warm.Add(Measure(() => executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile, options)));
        }

        var warmMs = warm.Select(w => w.Ms).OrderBy(x => x).ToList();
        var warmAlloc = warm.Select(w => w.Alloc).OrderBy(x => x).ToList();

        return $"""
            {name}:
              cold: {coldMs:0.###} ms, {coldAlloc:N0} bytes
              warm median: {Percentile(warmMs, 0.5):0.###} ms
              warm p95: {Percentile(warmMs, 0.95):0.###} ms
              warm alloc median: {Percentile(warmAlloc, 0.5):N0} bytes
              warm alloc p95: {Percentile(warmAlloc, 0.95):N0} bytes
            """;
    }

    private static (double Ms, long Alloc) Measure(Func<ShortcutLaunchResult> action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = action();
        stopwatch.Stop();
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Force consumption of the result so the compiler does not optimize away the call.
        _ = result.Dismiss;

        return (stopwatch.Elapsed.TotalMilliseconds, after - before);
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        var index = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        var weight = index - lower;
        return (sorted[lower] * (1 - weight)) + (sorted[upper] * weight);
    }

    private static double Percentile(List<long> sorted, double p)
    {
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        var index = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        var weight = index - lower;
        return (sorted[lower] * (1 - weight)) + (sorted[upper] * weight);
    }

    private static TerminalShortcut OneEntryShortcut() =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "One",
            Directory = Environment.CurrentDirectory,
            Launches =
            [
                new WorkspaceEntry { Id = "main", Label = "Main", Terminal = "wt", Command = "echo ready", IsEnabled = true, Order = 0 },
            ],
        };

    private static TerminalShortcut FiveIndependentEntriesShortcut() =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Separate",
            Directory = Environment.CurrentDirectory,
            Launches = Enumerable.Range(0, 5)
                .Select(i => new WorkspaceEntry
                {
                    Id = $"entry-{i}",
                    Label = $"Entry {i}",
                    Terminal = "wt",
                    Command = $"echo {i}",
                    IsEnabled = true,
                    Order = i,
                })
                .ToList(),
        };

    private static TerminalShortcut FiveWindowsTerminalTabsShortcut() =>
        new()
    {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Tabs",
            Directory = Environment.CurrentDirectory,
            Launches = Enumerable.Range(0, 5)
                .Select(i => new WorkspaceEntry
                {
                    Id = $"tab-{i}",
                    Label = $"Tab {i}",
                    Terminal = "wt",
                    Command = $"echo {i}",
                    IsEnabled = true,
                    Order = i,
                })
                .ToList(),
        };

    private static TerminalShortcut MixedTerminalTargetsShortcut() =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Mixed",
            Directory = Environment.CurrentDirectory,
            Launches =
            [
                new WorkspaceEntry { Id = "wt", Label = "WT", Terminal = "wt", Command = "echo wt", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = "cmd", Label = "Cmd", Terminal = "cmd", Command = "echo cmd", IsEnabled = true, Order = 1 },
                new WorkspaceEntry { Id = "ps", Label = "PowerShell", Terminal = "powershell", Command = "echo ps", IsEnabled = true, Order = 2 },
                new WorkspaceEntry { Id = "wt2", Label = "WT2", Terminal = "wt", Command = "echo wt2", IsEnabled = true, Order = 3 },
                new WorkspaceEntry { Id = "cmd2", Label = "Cmd2", Terminal = "cmd", Command = "echo cmd2", IsEnabled = true, Order = 4 },
            ],
        };
}
