using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Abstractions;
using QuickShell.Models;
using System;
using System.Threading.Tasks;

namespace QuickShell.Services;

/// <summary>
/// Defers optional row enrichment (terminal profile icon upgrades) off the first-paint
/// path. Rows are published with a stable fallback glyph; enrichment is scheduled here,
/// deduplicated by workspace id per refresh, resolved in one background
/// batch, and applied through <see cref="IExtensionCallbackQueue"/> so the host thread
/// owns the UI mutation. Results for an older refresh, or arriving after
/// disposal, are discarded.
/// </summary>
internal sealed partial class WorkspaceRowEnrichmentCoordinator : IDisposable
{
    private readonly IExtensionCallbackQueue _callbackQueue;
    private readonly Func<TerminalShortcut, string?> _resolveIcon;
    private readonly Action<Action> _runInBackground;
    private readonly object _sync = new();
    private readonly Dictionary<string, EnrichmentWork> _workByWorkspaceId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EnrichmentWork> _pending = [];
    private RefreshIdentity _currentRefresh;
    private long _refreshSequence;
    private bool _disposed;

    private readonly record struct RefreshIdentity(
        long Sequence,
        long RepositoryVersion,
        string SettingsFingerprint);

    private sealed class EnrichmentWork(
        RefreshIdentity refresh,
        TerminalShortcut shortcut,
        ListItem firstItem)
    {
        public RefreshIdentity Refresh { get; } = refresh;

        public TerminalShortcut Shortcut { get; } = shortcut;

        public List<ListItem> Items { get; } = [firstItem];

        public string? ResolvedIcon { get; set; }

        public bool ResolutionCompleted { get; set; }

        public bool ApplyQueued { get; set; }

        public int AppliedItemCount { get; set; }
    }

    public WorkspaceRowEnrichmentCoordinator(
        IExtensionCallbackQueue callbackQueue,
        Func<TerminalShortcut, string?>? resolveIcon = null,
        Action<Action>? backgroundScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(callbackQueue);
        _callbackQueue = callbackQueue;
        _resolveIcon = resolveIcon ?? ResolveUpgradedIcon;
        _runInBackground = backgroundScheduler ?? (work => _ = Task.Run(work));
    }

    /// <summary>
    /// Starts a new row-materialization generation. Repository version and settings are
    /// captured in the identity so callbacks from any older refresh are discarded.
    /// </summary>
    public long BeginRefresh(long repositoryVersion, string settingsFingerprint)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return 0;
            }

            _refreshSequence++;
            _currentRefresh = new RefreshIdentity(
                _refreshSequence,
                repositoryVersion,
                settingsFingerprint ?? string.Empty);

            if (_pending.Count > 0)
            {
                for (var i = 0; i < _pending.Count; i++)
                {
                    RowPresentationDiagnostics.Record(RowPresentationDiagnostics.EnrichmentCancelled);
                }

                _pending.Clear();
            }

            _workByWorkspaceId.Clear();
            return _refreshSequence;
        }
    }

    /// <summary>
    /// Queues an icon upgrade for a freshly built row. Rows that would never upgrade
    /// (admin launch, needs repair) are skipped. Duplicate rows share one resolution while
    /// each materialized item remains an apply target.
    /// </summary>
    public void ScheduleIconUpgrade(
        TerminalShortcut shortcut,
        long refreshGeneration,
        ListItem item)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(item);

        if (shortcut.RunAsAdmin
            || string.IsNullOrWhiteSpace(shortcut.Id)
            || ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false))
        {
            return;
        }

        EnrichmentWork? completedWork = null;
        var queuedResolution = false;
        lock (_sync)
        {
            if (_disposed || refreshGeneration != _currentRefresh.Sequence)
            {
                return;
            }

            if (_workByWorkspaceId.TryGetValue(shortcut.Id, out var existing))
            {
                existing.Items.Add(item);
                if (existing.ResolutionCompleted
                    && existing.ResolvedIcon is not null
                    && !existing.ApplyQueued)
                {
                    existing.ApplyQueued = true;
                    completedWork = existing;
                }
            }
            else
            {
                var work = new EnrichmentWork(_currentRefresh, shortcut, item);
                _workByWorkspaceId.Add(shortcut.Id, work);
                _pending.Add(work);
                queuedResolution = true;
            }
        }

        if (completedWork is not null)
        {
            QueueApply([completedWork]);
        }
        else if (queuedResolution)
        {
            RowPresentationDiagnostics.Record(RowPresentationDiagnostics.EnrichmentQueued);
        }
    }

    /// <summary>
    /// Starts one background batch for everything scheduled since the last flush.
    /// Call after the list has been published so first paint never waits on enrichment.
    /// </summary>
    public void Flush()
    {
        EnrichmentWork[] batch;
        lock (_sync)
        {
            if (_disposed || _pending.Count == 0)
            {
                return;
            }

            batch = _pending.ToArray();
            _pending.Clear();
        }

        _runInBackground(() => RunBatch(batch));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (var i = 0; i < _pending.Count; i++)
            {
                RowPresentationDiagnostics.Record(RowPresentationDiagnostics.EnrichmentCancelled);
            }

            _pending.Clear();
            _workByWorkspaceId.Clear();
        }
    }

    private void RunBatch(EnrichmentWork[] batch)
    {
        var resolved = new List<(EnrichmentWork Work, string? Icon)>(batch.Length);
        foreach (var work in batch)
        {
            try
            {
                resolved.Add((work, _resolveIcon(work.Shortcut)));
            }
            catch (InvalidOperationException)
            {
                // One row's enrichment failing must not stop the rest of the batch.
                resolved.Add((work, null));
            }
            catch (ArgumentException)
            {
                // One row's enrichment failing must not stop the rest of the batch.
                resolved.Add((work, null));
            }
        }

        var readyToApply = new List<EnrichmentWork>(resolved.Count);
        lock (_sync)
        {
            foreach (var (work, icon) in resolved)
            {
                work.ResolutionCompleted = true;
                work.ResolvedIcon = string.IsNullOrWhiteSpace(icon) ? null : icon;
                if (work.ResolvedIcon is not null && !work.ApplyQueued)
                {
                    work.ApplyQueued = true;
                    readyToApply.Add(work);
                }
            }
        }

        if (readyToApply.Count > 0)
        {
            QueueApply(readyToApply);
        }
    }

    private void QueueApply(IReadOnlyList<EnrichmentWork> workItems)
    {
        _callbackQueue.Enqueue(() =>
        {
            var appliedAny = false;
            lock (_sync)
            {
                foreach (var work in workItems)
                {
                    work.ApplyQueued = false;
                    if (_disposed || work.Refresh != _currentRefresh)
                    {
                        var staleCount = work.Items.Count - work.AppliedItemCount;
                        for (var i = 0; i < staleCount; i++)
                        {
                            RowPresentationDiagnostics.Record(RowPresentationDiagnostics.EnrichmentDiscardedStale);
                        }

                        continue;
                    }

                    if (work.ResolvedIcon is null)
                    {
                        continue;
                    }

                    while (work.AppliedItemCount < work.Items.Count)
                    {
                        work.Items[work.AppliedItemCount].Icon = new IconInfo(work.ResolvedIcon);
                        work.AppliedItemCount++;
                        appliedAny = true;
                    }
                }
            }

            if (appliedAny)
            {
                RowPresentationDiagnostics.Record(RowPresentationDiagnostics.EnrichmentBatchApplied);
            }
        });
    }

    private static string? ResolveUpgradedIcon(TerminalShortcut shortcut)
    {
        TerminalListIconCache.PrewarmProfiles();
        return TerminalListIconCache.TryResolveUpgradedListIcon(shortcut);
    }
}
