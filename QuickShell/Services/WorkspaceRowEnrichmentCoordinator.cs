using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Abstractions;
using QuickShell.Models;
using System.Threading.Tasks;

namespace QuickShell.Services;

/// <summary>
/// Defers optional row enrichment (terminal profile icon upgrades) off the first-paint
/// path. Rows are published with a stable fallback glyph; enrichment is scheduled here,
/// deduplicated by workspace id + repository version + kind, resolved in one background
/// batch, and applied through <see cref="IExtensionCallbackQueue"/> so the host thread
/// owns the UI mutation. Results for an older repository version, or arriving after
/// disposal, are discarded.
/// </summary>
internal sealed partial class WorkspaceRowEnrichmentCoordinator : IDisposable
{
    private readonly IExtensionCallbackQueue _callbackQueue;
    private readonly Func<TerminalShortcut, string?> _resolveIcon;
    private readonly Action<Action> _runInBackground;
    private readonly object _sync = new();
    private readonly HashSet<string> _scheduledKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingRow> _pending = [];
    private long _currentVersion = long.MinValue;
    private bool _disposed;

    private sealed record PendingRow(string WorkspaceId, long RepositoryVersion, TerminalShortcut Shortcut, ListItem Item);

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
    /// Observes the repository version for the refresh in progress. Pending work for an
    /// older version is dropped; dedup keys from older versions are forgotten.
    /// </summary>
    public void SetRepositoryVersion(long repositoryVersion)
    {
        lock (_sync)
        {
            if (repositoryVersion == _currentVersion)
            {
                return;
            }

            _currentVersion = repositoryVersion;
            _scheduledKeys.Clear();
            if (_pending.Count > 0)
            {
                for (var i = 0; i < _pending.Count; i++)
                {
                    RowPresentationDiagnostics.Record(RowPresentationDiagnostics.EnrichmentCancelled);
                }

                _pending.Clear();
            }
        }
    }

    /// <summary>
    /// Queues an icon upgrade for a freshly built row. Rows that would never upgrade
    /// (admin launch, needs repair) are skipped; duplicates per (id, version) are ignored.
    /// </summary>
    public void ScheduleIconUpgrade(TerminalShortcut shortcut, long repositoryVersion, ListItem item)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(item);

        if (shortcut.RunAsAdmin
            || string.IsNullOrWhiteSpace(shortcut.Id)
            || ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false))
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed || repositoryVersion != _currentVersion)
            {
                return;
            }

            var key = $"{shortcut.Id}|{repositoryVersion}|icon";
            if (!_scheduledKeys.Add(key))
            {
                return;
            }

            _pending.Add(new PendingRow(shortcut.Id, repositoryVersion, shortcut, item));
        }

        RowPresentationDiagnostics.Record(RowPresentationDiagnostics.EnrichmentQueued);
    }

    /// <summary>
    /// Starts one background batch for everything scheduled since the last flush.
    /// Call after the list has been published so first paint never waits on enrichment.
    /// </summary>
    public void Flush()
    {
        PendingRow[] batch;
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
            _scheduledKeys.Clear();
        }
    }

    private void RunBatch(PendingRow[] batch)
    {
        var resolved = new List<(PendingRow Row, string Icon)>(batch.Length);
        foreach (var row in batch)
        {
            try
            {
                var icon = _resolveIcon(row.Shortcut);
                if (!string.IsNullOrWhiteSpace(icon))
                {
                    resolved.Add((row, icon));
                }
            }
            catch
            {
                // One row's enrichment failing must not stop the rest of the batch.
            }
        }

        if (resolved.Count == 0)
        {
            return;
        }

        _callbackQueue.Enqueue(() =>
        {
            var appliedAny = false;
            lock (_sync)
            {
                if (_disposed)
                {
                    for (var i = 0; i < resolved.Count; i++)
                    {
                        RowPresentationDiagnostics.Record(RowPresentationDiagnostics.EnrichmentDiscardedStale);
                    }

                    return;
                }
            }

            foreach (var (row, icon) in resolved)
            {
                long currentVersion;
                lock (_sync)
                {
                    currentVersion = _currentVersion;
                }

                if (row.RepositoryVersion != currentVersion)
                {
                    RowPresentationDiagnostics.Record(RowPresentationDiagnostics.EnrichmentDiscardedStale);
                    continue;
                }

                row.Item.Icon = new IconInfo(icon);
                appliedAny = true;
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
