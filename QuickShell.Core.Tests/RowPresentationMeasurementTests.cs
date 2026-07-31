using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using System.Diagnostics;
using Xunit.Abstractions;

namespace QuickShell.Core.Tests;

/// <summary>
/// Row construction measurements at 10 / 100 / <see cref="ShortcutValidation.MaxShortcutCount"/>
/// workspaces: cold vs warm home-list and search-result construction, allocations, and
/// presentation cache / enrichment operation counts. Wall-clock numbers are machine
/// dependent and printed only; the assertions are the deterministic shape contracts
/// (cache reuse on warm passes, no enrichment applied during construction).
/// </summary>
public sealed class RowPresentationMeasurementTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempRoot;

    public RowPresentationMeasurementTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Join(Path.GetTempPath(), "qs-row-perf-" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(ShortcutValidation.MaxShortcutCount)]
    public void Measure_HomeAndSearchRowConstruction(int workspaceCount)
    {
        var configDir = Path.Join(_tempRoot, "cfg-" + workspaceCount);
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
            provider, repository, drafts, settings, analysis, lifetime);
        var context = new QuickShellPageContext(
            new QuickShellHostServices(qsServices),
            new CreateShortcutCommand(() => { }, qsServices),
            () => { });

        for (var i = 0; i < workspaceCount; i++)
        {
            repository.Upsert(new TerminalShortcut
            {
                Id = "ws-" + i,
                Name = "Workspace " + i,
                Directory = configDir,
                Command = "echo " + i,
                IsPinned = i % 10 == 0,
                PinOrder = i % 10 == 0 ? i : null,
            });
        }

        // --- Home list: cold build (first paint) then warm rebuild -------------------
        using var page = new QuickShellPage(context);
        var (homeColdMs, homeColdBytes) = MeasureOnce(() => _ = page.GetItems());
        var coldBuilds = qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheBuild);
        var coldQueued = qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentQueued);
        var appliedDuringCold = qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied);

        var (homeWarmMs, homeWarmBytes) = MeasureOnce(page.Reload);
        var warmBuilds = qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheBuild) - coldBuilds;
        var warmHits = qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheHit);

        // --- Search results (fallback page): cold then warm --------------------------
        var snapshot = repository.GetSnapshot();
        var results = snapshot.SearchForRootPalette("Workspace").ToArray();
        using var fallback = new QuickShellFallbackPage(context);
        var (searchColdMs, searchColdBytes) = MeasureOnce(
            () => fallback.SetWorkspaceResults("Workspace", results, snapshot.Version));
        var (searchWarmMs, searchWarmBytes) = MeasureOnce(
            () => fallback.SetWorkspaceResults("Workspace", results, snapshot.Version));

        _output.WriteLine($"=== Row construction, {workspaceCount} workspaces ===");
        _output.WriteLine($"Home cold   : {homeColdMs:0.###} ms, {homeColdBytes / 1024.0:0.#} KiB allocated");
        _output.WriteLine($"Home warm   : {homeWarmMs:0.###} ms, {homeWarmBytes / 1024.0:0.#} KiB allocated");
        _output.WriteLine($"Search cold : {searchColdMs:0.###} ms, {searchColdBytes / 1024.0:0.#} KiB allocated");
        _output.WriteLine($"Search warm : {searchWarmMs:0.###} ms, {searchWarmBytes / 1024.0:0.#} KiB allocated");
        _output.WriteLine($"row-cache:build={qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheBuild)} " +
                          $"row-cache:hit={qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheHit)} " +
                          $"row-cache:miss={qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheMiss)}");
        _output.WriteLine($"row-enrichment:queued={qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentQueued)} " +
                          $"batch-applied={qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied)}");

        // Deterministic shape contracts (the wall-clock numbers above are informational).
        Assert.True(coldBuilds >= workspaceCount, "cold home paint should build one presentation per row");
        Assert.Equal(0, warmBuilds);
        Assert.True(warmHits > 0, "warm home rebuild should hit the presentation cache");
        Assert.Equal(0, appliedDuringCold);
        Assert.True(coldQueued > 0, "icon enrichment should be queued, not run inline");
        Assert.Equal(
            0,
            qsServices.RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied));

        repository.Dispose();
    }

    private static (double Milliseconds, long AllocatedBytes) MeasureOnce(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        var after = GC.GetAllocatedBytesForCurrentThread();
        return (stopwatch.Elapsed.TotalMilliseconds, after - before);
    }
}
