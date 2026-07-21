using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceRowEnrichmentCoordinatorTests
{
    private readonly ExtensionCallbackQueue _queue = new();
    private readonly RowPresentationDiagnostics _diagnostics = new();
    private readonly List<string> _resolvedIds = [];

    private WorkspaceRowEnrichmentCoordinator CreateCoordinator(
        Func<TerminalShortcut, string?>? resolveIcon = null) =>
        new(
            _queue,
            new TerminalListIconCache(
                new WtProfilesService([]),
                TestQuickShellServicesFactory.CreateGlyphs(),
                new AppDataPaths(Path.Combine(Path.GetTempPath(), "qs-test-appdata-" + Guid.NewGuid().ToString("N")))),
            _diagnostics,
            resolveIcon ?? (shortcut =>
            {
                _resolvedIds.Add(shortcut.Id);
                return "";
            }),
            // Inline scheduler: the batch resolves synchronously inside Flush so tests
            // control exactly when the UI apply happens (queue.Drain()).
            backgroundScheduler: work => work());

    private static TerminalShortcut CreateShortcut(string id = "ws-1") =>
        new()
        {
            Id = id,
            Name = "Alpha " + id,
            Directory = Path.GetTempPath(),
            Command = "echo hi",
        };

    private static ListItem CreateItem() =>
        new(new NoOpCommand()) { Icon = new IconInfo("") };

    [Fact]
    public void Flush_AppliesIconsOnlyThroughCallbackQueue_AsOneBatch()
    {
        using var coordinator = CreateCoordinator();
        var generation = coordinator.BeginRefresh(1, "wt|profile-a");

        var items = new List<ListItem>();
        for (var i = 0; i < 3; i++)
        {
            var item = CreateItem();
            items.Add(item);
            coordinator.ScheduleIconUpgrade(CreateShortcut("ws-" + i), generation, item);
        }

        var initialIcons = items.Select(item => item.Icon).ToArray();
        coordinator.Flush();

        // Resolved, but not applied: the list as first published is untouched.
        Assert.Equal(3, _resolvedIds.Count);
        Assert.Equal(initialIcons, items.Select(item => item.Icon).ToArray());

        _queue.Drain();

        for (var i = 0; i < items.Count; i++)
        {
            Assert.NotSame(initialIcons[i], items[i].Icon);
        }

        Assert.Equal(3, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentQueued));
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied));
    }

    [Fact]
    public void ScheduleIconUpgrade_DeduplicatesResolutionAndAppliesEveryMaterializedItem()
    {
        using var coordinator = CreateCoordinator();
        var generation = coordinator.BeginRefresh(1, "wt|profile-a");
        var shortcut = CreateShortcut();
        var first = CreateItem();
        var second = CreateItem();
        var firstIcon = first.Icon;
        var secondIcon = second.Icon;

        coordinator.ScheduleIconUpgrade(shortcut, generation, first);
        coordinator.Flush();
        coordinator.ScheduleIconUpgrade(shortcut, generation, second);
        _queue.Drain();

        Assert.Single(_resolvedIds);
        Assert.NotSame(firstIcon, first.Icon);
        Assert.NotSame(secondIcon, second.Icon);
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentQueued));
    }

    [Fact]
    public void ScheduleIconUpgrade_AppliesResolvedIconToItemAddedAfterCallback()
    {
        using var coordinator = CreateCoordinator();
        var generation = coordinator.BeginRefresh(1, "wt|profile-a");
        var shortcut = CreateShortcut();

        coordinator.ScheduleIconUpgrade(shortcut, generation, CreateItem());
        coordinator.Flush();
        _queue.Drain();

        var later = CreateItem();
        var fallbackIcon = later.Icon;
        coordinator.ScheduleIconUpgrade(shortcut, generation, later);
        _queue.Drain();

        Assert.Single(_resolvedIds);
        Assert.NotSame(fallbackIcon, later.Icon);
    }

    [Fact]
    public void StaleRepositoryVersion_CannotOverwriteNewerRows()
    {
        using var coordinator = CreateCoordinator();
        var generation = coordinator.BeginRefresh(1, "wt|profile-a");

        var staleItem = CreateItem();
        var staleIcon = staleItem.Icon;
        coordinator.ScheduleIconUpgrade(CreateShortcut(), generation, staleItem);
        coordinator.Flush();

        // Repository moved on before the UI thread drained the queue.
        coordinator.BeginRefresh(2, "wt|profile-a");
        _queue.Drain();

        Assert.Same(staleIcon, staleItem.Icon);
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentDiscardedStale));
        Assert.Equal(0, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied));
    }

    [Fact]
    public void BeginRefresh_DropsPendingWorkForOlderGeneration()
    {
        using var coordinator = CreateCoordinator();
        var generation = coordinator.BeginRefresh(1, "wt|profile-a");
        coordinator.ScheduleIconUpgrade(CreateShortcut(), generation, CreateItem());

        coordinator.BeginRefresh(2, "wt|profile-a");
        coordinator.Flush();
        _queue.Drain();

        Assert.Empty(_resolvedIds);
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentCancelled));
    }

    [Fact]
    public void SettingsChangeAtSameRepositoryVersion_DiscardsOldGeneration()
    {
        using var coordinator = CreateCoordinator();
        var oldGeneration = coordinator.BeginRefresh(1, "wt|profile-a");
        var staleItem = CreateItem();
        var staleIcon = staleItem.Icon;
        coordinator.ScheduleIconUpgrade(CreateShortcut(), oldGeneration, staleItem);
        coordinator.Flush();

        var newGeneration = coordinator.BeginRefresh(1, "wt|profile-b");
        var currentItem = CreateItem();
        var currentIcon = currentItem.Icon;
        coordinator.ScheduleIconUpgrade(CreateShortcut(), newGeneration, currentItem);
        coordinator.Flush();
        _queue.Drain();

        Assert.Same(staleIcon, staleItem.Icon);
        Assert.NotSame(currentIcon, currentItem.Icon);
        Assert.Equal(2, _resolvedIds.Count);
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentDiscardedStale));
    }

    [Fact]
    public void SameVersionRefresh_EnrichesNewlyMaterializedItem()
    {
        using var coordinator = CreateCoordinator();
        var firstGeneration = coordinator.BeginRefresh(1, "wt|profile-a");
        var staleItem = CreateItem();
        var staleIcon = staleItem.Icon;
        coordinator.ScheduleIconUpgrade(CreateShortcut(), firstGeneration, staleItem);
        coordinator.Flush();

        var nextGeneration = coordinator.BeginRefresh(1, "wt|profile-a");
        var currentItem = CreateItem();
        var currentIcon = currentItem.Icon;
        coordinator.ScheduleIconUpgrade(CreateShortcut(), nextGeneration, currentItem);
        coordinator.Flush();
        _queue.Drain();

        Assert.Same(staleIcon, staleItem.Icon);
        Assert.NotSame(currentIcon, currentItem.Icon);
        Assert.Equal(2, _resolvedIds.Count);
    }

    [Fact]
    public void Dispose_DiscardsPendingAndInFlightWork()
    {
        // A resolvable icon so Flush() actually queues an apply callback — the empty-icon
        // default resolver used elsewhere in this file never reaches the apply path at all,
        // which would make this test pass trivially without exercising disposal.
        using var coordinator = CreateCoordinator(_ => "icon");
        var generation = coordinator.BeginRefresh(1, "wt|profile-a");

        var flushedItem = CreateItem();
        var flushedIcon = flushedItem.Icon;
        coordinator.ScheduleIconUpgrade(CreateShortcut("ws-flushed"), generation, flushedItem);
        coordinator.Flush();

        coordinator.ScheduleIconUpgrade(CreateShortcut("ws-pending"), generation, CreateItem());

        // Dispose before the queued apply callback runs: the resolved-but-unapplied "flushed"
        // batch must be discarded at apply time, and the never-flushed "pending" row must be
        // cancelled outright.
        coordinator.Dispose();
        _queue.Drain();

        Assert.Same(flushedIcon, flushedItem.Icon);
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentDiscardedStale));
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentCancelled));
    }

    [Fact]
    public void OneFailingRow_DoesNotBlockOtherRows()
    {
        using var coordinator = CreateCoordinator(shortcut =>
            shortcut.Id == "ws-bad"
                ? throw new InvalidOperationException("icon source unreadable")
                : "");
        var generation = coordinator.BeginRefresh(1, "wt|profile-a");

        var good = CreateItem();
        var goodIcon = good.Icon;
        var bad = CreateItem();
        coordinator.ScheduleIconUpgrade(CreateShortcut("ws-bad"), generation, bad);
        coordinator.ScheduleIconUpgrade(CreateShortcut("ws-good"), generation, good);

        coordinator.Flush();
        _queue.Drain();

        Assert.NotSame(goodIcon, good.Icon);
        Assert.Equal(1, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied));
    }

    [Fact]
    public void RowsThatNeverUpgrade_AreNotQueued()
    {
        using var coordinator = CreateCoordinator();
        var generation = coordinator.BeginRefresh(1, "wt|profile-a");

        var admin = CreateShortcut("ws-admin");
        admin.RunAsAdmin = true;
        var broken = CreateShortcut("ws-broken");
        broken.Directory = string.Empty;

        coordinator.ScheduleIconUpgrade(admin, generation, CreateItem());
        coordinator.ScheduleIconUpgrade(broken, generation, CreateItem());
        coordinator.Flush();

        Assert.Empty(_resolvedIds);
        Assert.Equal(0, _diagnostics.GetCount(RowPresentationDiagnostics.EnrichmentQueued));
    }
}
