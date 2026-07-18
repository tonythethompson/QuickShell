using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(RowPresentationIsolation.Name)]
public sealed class WorkspaceRowEnrichmentCoordinatorTests : IDisposable
{
    private readonly ExtensionCallbackQueue _queue = new();
    private readonly List<string> _resolvedIds = [];

    public WorkspaceRowEnrichmentCoordinatorTests()
    {
        RowPresentationDiagnostics.ResetForTests();
    }

    public void Dispose()
    {
        RowPresentationDiagnostics.ResetForTests();
    }

    private WorkspaceRowEnrichmentCoordinator CreateCoordinator(
        Func<TerminalShortcut, string?>? resolveIcon = null) =>
        new(
            _queue,
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
        coordinator.SetRepositoryVersion(1);

        var items = new List<ListItem>();
        for (var i = 0; i < 3; i++)
        {
            var item = CreateItem();
            items.Add(item);
            coordinator.ScheduleIconUpgrade(CreateShortcut("ws-" + i), 1, item);
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

        Assert.Equal(3, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentQueued));
        Assert.Equal(1, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied));
    }

    [Fact]
    public void ScheduleIconUpgrade_DeduplicatesByWorkspaceAndVersion()
    {
        using var coordinator = CreateCoordinator();
        coordinator.SetRepositoryVersion(1);
        var shortcut = CreateShortcut();

        coordinator.ScheduleIconUpgrade(shortcut, 1, CreateItem());
        coordinator.ScheduleIconUpgrade(shortcut, 1, CreateItem());
        coordinator.Flush();

        Assert.Single(_resolvedIds);
        Assert.Equal(1, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentQueued));
    }

    [Fact]
    public void StaleRepositoryVersion_CannotOverwriteNewerRows()
    {
        using var coordinator = CreateCoordinator();
        coordinator.SetRepositoryVersion(1);

        var staleItem = CreateItem();
        var staleIcon = staleItem.Icon;
        coordinator.ScheduleIconUpgrade(CreateShortcut(), 1, staleItem);
        coordinator.Flush();

        // Repository moved on before the UI thread drained the queue.
        coordinator.SetRepositoryVersion(2);
        _queue.Drain();

        Assert.Same(staleIcon, staleItem.Icon);
        Assert.Equal(1, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentDiscardedStale));
        Assert.Equal(0, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied));
    }

    [Fact]
    public void SetRepositoryVersion_DropsPendingWorkForOlderVersion()
    {
        using var coordinator = CreateCoordinator();
        coordinator.SetRepositoryVersion(1);
        coordinator.ScheduleIconUpgrade(CreateShortcut(), 1, CreateItem());

        coordinator.SetRepositoryVersion(2);
        coordinator.Flush();
        _queue.Drain();

        Assert.Empty(_resolvedIds);
        Assert.Equal(1, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentCancelled));
    }

    [Fact]
    public void Dispose_DiscardsPendingAndInFlightWork()
    {
        var coordinator = CreateCoordinator();
        coordinator.SetRepositoryVersion(1);

        var flushedItem = CreateItem();
        var flushedIcon = flushedItem.Icon;
        coordinator.ScheduleIconUpgrade(CreateShortcut("ws-flushed"), 1, flushedItem);
        coordinator.Flush();

        coordinator.ScheduleIconUpgrade(CreateShortcut("ws-pending"), 1, CreateItem());
        coordinator.Dispose();
        _queue.Drain();

        // The already-resolved batch is discarded at apply time; the un-flushed row is cancelled.
        Assert.Same(flushedIcon, flushedItem.Icon);
        Assert.Equal(1, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentDiscardedStale));
        Assert.Equal(1, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentCancelled));
    }

    [Fact]
    public void OneFailingRow_DoesNotBlockOtherRows()
    {
        using var coordinator = CreateCoordinator(shortcut =>
            shortcut.Id == "ws-bad"
                ? throw new InvalidOperationException("icon source unreadable")
                : "");
        coordinator.SetRepositoryVersion(1);

        var good = CreateItem();
        var goodIcon = good.Icon;
        var bad = CreateItem();
        coordinator.ScheduleIconUpgrade(CreateShortcut("ws-bad"), 1, bad);
        coordinator.ScheduleIconUpgrade(CreateShortcut("ws-good"), 1, good);

        coordinator.Flush();
        _queue.Drain();

        Assert.NotSame(goodIcon, good.Icon);
        Assert.Equal(1, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied));
    }

    [Fact]
    public void RowsThatNeverUpgrade_AreNotQueued()
    {
        using var coordinator = CreateCoordinator();
        coordinator.SetRepositoryVersion(1);

        var admin = CreateShortcut("ws-admin");
        admin.RunAsAdmin = true;
        var broken = CreateShortcut("ws-broken");
        broken.Directory = string.Empty;

        coordinator.ScheduleIconUpgrade(admin, 1, CreateItem());
        coordinator.ScheduleIconUpgrade(broken, 1, CreateItem());
        coordinator.Flush();

        Assert.Empty(_resolvedIds);
        Assert.Equal(0, RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentQueued));
    }
}
