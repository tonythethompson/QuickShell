using QuickShell.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Services;

internal sealed partial class ShortcutDraftStore : IDraftStore, IDisposable
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SlowOperationThreshold = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(3);

    private readonly IShortcutRepository _shortcuts;
    private readonly IAtomicFileWriter _fileWriter;
    private readonly SemaphoreSlim _sync = new(1, 1);

    public ShortcutDraftStore(IShortcutRepository shortcuts)
        : this(shortcuts, writer: null)
    {
    }

    public ShortcutDraftStore(IShortcutRepository shortcuts, IAtomicFileWriter? writer)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        _shortcuts = shortcuts;
        _fileWriter = writer ?? new AtomicFileWriter();
    }

    private bool _disposed;

    private PersistedShortcutEditDraft? _cached;
    private bool _cacheLoaded;
    private int _writeGeneration;
    private Task _fileIoQueue = Task.CompletedTask;

    public event Action<string>? Cleared;

    public string DraftPath => Path.Combine(_shortcuts.ConfigDirectory, "shortcut-edit-draft.json");

    public bool HasPending =>
        WithLock(() => TryGetPendingLocked(out _));

    public PersistedShortcutEditDraft? Pending =>
        WithLock(() => TryGetPendingLocked(out var draft) ? draft : null);

    public bool TryGetForRestore(string originalName, out PersistedShortcutEditDraft draft)
    {
        draft = null!;

        if (string.IsNullOrWhiteSpace(originalName))
        {
            return false;
        }

        var found = WithLock(() =>
        {
            if (!TryGetPendingLocked(out var pending))
            {
                return null;
            }

            if (pending is null
                || !string.Equals(pending.OriginalName, originalName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return pending;
        });

        if (found is null)
        {
            return false;
        }

        draft = found;
        return true;
    }

    public void SaveIfDirty(
        string editKey,
        ShortcutFormDraftData draft,
        ShortcutFormDraftData baseline,
        bool nameCustomized,
        string? autoFilledName)
    {
        if (string.IsNullOrWhiteSpace(editKey))
        {
            return;
        }

        if (DraftEquals(draft, baseline))
        {
            WithLock(() =>
            {
                if (_cached is not null
                    && string.Equals(_cached.OriginalName, editKey, StringComparison.OrdinalIgnoreCase))
                {
                    ClearLocked();
                }
            });

            return;
        }

        var persisted = new PersistedShortcutEditDraft
        {
            OriginalName = editKey,
            Name = draft.Name,
            Abbreviation = draft.Abbreviation,
            Directory = draft.Directory,
            Command = draft.Command,
            LaunchTarget = draft.LaunchTarget,
            DevServerUrl = draft.DevServerUrl,
            RepoUrl = draft.RepoUrl,
            OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
            OpenCompanionAppOnLaunch = draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = draft.CompanionAppPreset,
            CompanionAppPath = draft.CompanionAppPath,
            CompanionAppArguments = draft.CompanionAppArguments,
            Companions = draft.Companions
                .Select(companion => new PersistedShortcutCompanionDraft
                {
                    Id = companion.Id,
                    Preset = companion.Preset,
                    Path = companion.Path,
                    Arguments = companion.Arguments,
                    OpenOnLaunch = companion.OpenOnLaunch,
                })
                .ToList(),
            NameCustomized = nameCustomized,
            AutoFilledName = autoFilledName,
            RunAsAdmin = draft.RunAsAdmin,
            Launches = draft.Launches
                .Select(launch => new PersistedShortcutLaunchDraft
                {
                    Id = launch.Id,
                    Label = launch.Label,
                    Command = launch.Command,
                    LaunchTarget = launch.LaunchTarget,
                    RunAsAdmin = launch.RunAsAdmin,
                    IsEnabled = launch.IsEnabled,
                    TaskType = launch.TaskType,
                })
                .ToList(),
        };

        WithLock(() =>
        {
            _cached = persisted;
            WriteLocked(persisted);
        });
    }

    public void Clear()
    {
        string? clearedOriginalName = null;
        WithLock(() =>
        {
            clearedOriginalName = _cached?.OriginalName;
            ClearLocked();
        });

        if (!string.IsNullOrWhiteSpace(clearedOriginalName))
        {
            Cleared?.Invoke(clearedOriginalName);
        }
    }

    public ShortcutSaveResult TryCommitPending(Action? onSaved)
    {
        PersistedShortcutEditDraft? pending = null;

        var hasPending = WithLock(() =>
        {
            if (!TryGetPendingLocked(out pending) || pending is null)
            {
                return false;
            }

            return true;
        });

        if (!hasPending || pending is null)
        {
            return ShortcutSaveResult.Fail("No unsaved shortcut edit is pending.");
        }

        var launches = pending.Launches is { Count: > 0 }
            ? pending.Launches.Select(launch => new ShortcutFormLaunchInput
            {
                Id = launch.Id,
                Label = launch.Label,
                Command = launch.Command,
                LaunchTarget = launch.LaunchTarget,
                RunAsAdmin = launch.RunAsAdmin,
                IsEnabled = launch.IsEnabled,
                TaskType = launch.TaskType,
            }).ToList()
            : null;

        var companionApps = pending.Companions is { Count: > 0 }
            ? CompanionAppFormEditor.ToCompanionEntries(
                pending.Companions.Select(companion => new CompanionAppFormRow
                {
                    Id = companion.Id,
                    Preset = companion.Preset,
                    Path = companion.Path,
                    Arguments = companion.Arguments,
                    OpenOnLaunch = companion.OpenOnLaunch,
                }).ToList())
            : null;

        launches ??=
        [
            new ShortcutFormLaunchInput
            {
                Label = pending.Name,
                Command = pending.Command,
                LaunchTarget = pending.LaunchTarget,
                RunAsAdmin = pending.RunAsAdmin,
                IsEnabled = true,
                TaskType = TaskTypeCatalog.None,
            },
        ];

        var result = ShortcutFormSave.TrySave(
            pending.OriginalName,
            pending.Name,
            pending.Abbreviation,
            pending.Directory,
            launches,
            _shortcuts,
            onSaved,
            pending.DevServerUrl,
            pending.RepoUrl,
            pending.OpenDevServerOnLaunch,
            pending.OpenCompanionAppOnLaunch,
            pending.CompanionAppPath,
            pending.CompanionAppArguments,
            companionApps);

        if (result.Success)
        {
            Clear();
        }

        return result;
    }

    private bool TryGetPendingLocked(out PersistedShortcutEditDraft? draft)
    {
        EnsureLoadedLocked();
        draft = _cached;

        if (draft is null)
        {
            return false;
        }

        if (_shortcuts.GetByName(draft.OriginalName) is not { } saved)
        {
            ClearLocked();
            draft = null;
            return false;
        }

        if (DraftMatchesShortcut(draft, saved))
        {
            ClearLocked();
            draft = null;
            return false;
        }

        return true;
    }

    private void EnsureLoadedLocked()
    {
        if (_cacheLoaded)
        {
            return;
        }

        DrainFileIoQueueLocked();

        _cacheLoaded = true;
        _cached = null;

        try
        {
            if (!File.Exists(DraftPath))
            {
                return;
            }

            using var stream = File.OpenRead(DraftPath);
            _cached = JsonSerializer.Deserialize(stream, ShortcutFormDraftJsonContext.Default.PersistedShortcutEditDraft);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _cached = null;
        }
    }

    private void WriteLocked(PersistedShortcutEditDraft draft)
    {
        try
        {
            var json = JsonSerializer.Serialize(draft, ShortcutFormDraftJsonContext.Default.PersistedShortcutEditDraft);
            var generation = _writeGeneration;
            EnqueueFileIoLocked(() => PersistDraftAsync(json, generation));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Best-effort autosave; ignore serialization failures.
        }
    }

    private void ClearLocked()
    {
        _writeGeneration++;
        _cached = null;
        _cacheLoaded = false;

        if (DrainFileIoQueueLocked())
        {
            DeleteDraftFileSync();
        }

        // Else: the in-flight write is stuck past the drain timeout. Its generation no
        // longer matches _writeGeneration (bumped above), so PersistDraftAsync's own
        // post-write check deletes the draft file once that write finally completes —
        // deleting here now would race a write that hasn't happened yet.
    }

    /// <returns>
    /// True if the queue was observed idle (drained, or nothing pending). False if it
    /// timed out with a write still in flight — callers must not assume the file on disk
    /// reflects a quiesced queue.
    /// </returns>
    private bool DrainFileIoQueueLocked()
    {
        var queue = _fileIoQueue;
        try
        {
            var completed = Task.WhenAny(queue, Task.Delay(DrainTimeout)).GetAwaiter().GetResult();
            if (!ReferenceEquals(completed, queue))
            {
                RepositoryDiagnostics.Report("ShortcutDraftStore.DrainFileIoQueueLocked", "drain-timeout", (long)DrainTimeout.TotalMilliseconds);
                return false;
            }

            queue.GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Best effort.
            return true;
        }
    }

    private void EnqueueFileIoLocked(Func<Task> operation)
    {
        _fileIoQueue = _fileIoQueue
            .ContinueWith(_ => operation(), TaskScheduler.Default)
            .Unwrap();
    }

    private Task PersistDraftAsync(string json, int generation)
    {
        if (generation != _writeGeneration)
        {
            return Task.CompletedTask;
        }

        try
        {
            // Sync atomic write on the existing async queue (no async writer API in slice 1).
            _fileWriter.WriteAllTextAtomic(DraftPath, json);

            if (generation != _writeGeneration)
            {
                DeleteDraftFileSync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort autosave; ignore IO failures.
        }

        return Task.CompletedTask;
    }

    private void DeleteDraftFileSync()
    {
        try
        {
            if (File.Exists(DraftPath))
            {
                File.Delete(DraftPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private static bool DraftMatchesShortcut(PersistedShortcutEditDraft draft, TerminalShortcut saved)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(saved);
        CompanionAppNormalization.EnsureCompanionsFromLegacy(saved);

        if (!MetadataMatches(draft, saved))
        {
            return false;
        }

        if (draft.Companions is { Count: > 0 }
            && !CompanionDraftsMatchShortcut(draft.Companions, saved.CompanionApps))
        {
            return false;
        }

        if (draft.Launches is not { Count: > 0 })
        {
            var launchTarget = TerminalCatalog.EncodeLaunchTargetId(saved);
            var first = saved.Launches.OrderBy(entry => entry.Order).FirstOrDefault();
            return string.Equals(Normalize(draft.Command), Normalize(first?.Command), StringComparison.Ordinal)
                && string.Equals(Normalize(draft.LaunchTarget), Normalize(launchTarget), StringComparison.Ordinal)
                && draft.RunAsAdmin == (first?.RunAsAdmin ?? false);
        }

        return LaunchDraftsMatchShortcut(draft.Launches, saved.Launches);
    }

    private static bool MetadataMatches(PersistedShortcutEditDraft draft, TerminalShortcut saved) =>
        string.Equals(Normalize(draft.Name), Normalize(saved.Name), StringComparison.Ordinal)
        && string.Equals(Normalize(draft.Abbreviation), Normalize(saved.Abbreviation), StringComparison.Ordinal)
        && string.Equals(Normalize(draft.Directory), Normalize(saved.Directory), StringComparison.Ordinal)
        && string.Equals(Normalize(draft.DevServerUrl), Normalize(saved.DevServerUrl), StringComparison.Ordinal)
        && string.Equals(Normalize(draft.RepoUrl), Normalize(saved.RepoUrl), StringComparison.Ordinal)
        && draft.OpenDevServerOnLaunch == saved.OpenDevServerOnLaunch
        && draft.OpenCompanionAppOnLaunch == saved.OpenCompanionAppOnLaunch
        && string.Equals(Normalize(draft.CompanionAppPath), Normalize(saved.CompanionAppPath), StringComparison.Ordinal)
        && string.Equals(Normalize(draft.CompanionAppArguments), Normalize(saved.CompanionAppArguments), StringComparison.Ordinal);

    private static bool DraftEquals(ShortcutFormDraftData left, ShortcutFormDraftData right)
    {
        if (!MetadataMatchesDraft(left, right))
        {
            return false;
        }

        if (!CompanionDraftListsEqual(left.Companions, right.Companions))
        {
            return false;
        }

        if (left.Launches.Count == 0 && right.Launches.Count == 0)
        {
            return string.Equals(Normalize(left.Command), Normalize(right.Command), StringComparison.Ordinal)
                && string.Equals(Normalize(left.LaunchTarget), Normalize(right.LaunchTarget), StringComparison.Ordinal)
                && left.RunAsAdmin == right.RunAsAdmin;
        }

        return LaunchDraftListsEqual(left.Launches, right.Launches);
    }

    private static bool MetadataMatchesDraft(ShortcutFormDraftData left, ShortcutFormDraftData right) =>
        string.Equals(Normalize(left.Name), Normalize(right.Name), StringComparison.Ordinal)
        && string.Equals(Normalize(left.Abbreviation), Normalize(right.Abbreviation), StringComparison.Ordinal)
        && string.Equals(Normalize(left.Directory), Normalize(right.Directory), StringComparison.Ordinal)
        && string.Equals(Normalize(left.DevServerUrl), Normalize(right.DevServerUrl), StringComparison.Ordinal)
        && string.Equals(Normalize(left.RepoUrl), Normalize(right.RepoUrl), StringComparison.Ordinal)
        && left.OpenDevServerOnLaunch == right.OpenDevServerOnLaunch
        && left.OpenCompanionAppOnLaunch == right.OpenCompanionAppOnLaunch
        && string.Equals(Normalize(left.CompanionAppPath), Normalize(right.CompanionAppPath), StringComparison.Ordinal)
        && string.Equals(Normalize(left.CompanionAppArguments), Normalize(right.CompanionAppArguments), StringComparison.Ordinal);

    private static bool CompanionDraftListsEqual(
        List<ShortcutFormCompanionDraftData> left,
        List<ShortcutFormCompanionDraftData> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (!string.Equals(Normalize(a.Preset), Normalize(b.Preset), StringComparison.Ordinal)
                || !string.Equals(Normalize(a.Path), Normalize(b.Path), StringComparison.Ordinal)
                || !string.Equals(Normalize(a.Arguments), Normalize(b.Arguments), StringComparison.Ordinal)
                || a.OpenOnLaunch != b.OpenOnLaunch)
            {
                return false;
            }
        }

        return true;
    }

    private static bool LaunchDraftListsEqual(
        List<ShortcutFormLaunchDraftData> left,
        List<ShortcutFormLaunchDraftData> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (!string.Equals(Normalize(a.Label), Normalize(b.Label), StringComparison.Ordinal)
                || !string.Equals(Normalize(a.Command), Normalize(b.Command), StringComparison.Ordinal)
                || !string.Equals(Normalize(a.LaunchTarget), Normalize(b.LaunchTarget), StringComparison.Ordinal)
                || a.RunAsAdmin != b.RunAsAdmin
                || a.IsEnabled != b.IsEnabled
                || !string.Equals(TaskTypeCatalog.Normalize(a.TaskType), TaskTypeCatalog.Normalize(b.TaskType), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LaunchDraftsMatchShortcut(
        List<PersistedShortcutLaunchDraft> draftLaunches,
        List<WorkspaceEntry> savedLaunches)
    {
        var saved = savedLaunches.OrderBy(entry => entry.Order).ToList();
        if (draftLaunches.Count != saved.Count)
        {
            return false;
        }

        for (var i = 0; i < draftLaunches.Count; i++)
        {
            var draft = draftLaunches[i];
            var entry = saved[i];
            var launchTarget = TerminalCatalog.EncodeLaunchTargetId(new TerminalShortcut
            {
                Terminal = entry.Terminal,
                WtProfile = entry.WtProfile,
            });

            if (!string.Equals(Normalize(draft.Label), Normalize(entry.Label), StringComparison.Ordinal)
                || !string.Equals(Normalize(draft.Command), Normalize(entry.Command), StringComparison.Ordinal)
                || !string.Equals(Normalize(draft.LaunchTarget), Normalize(launchTarget), StringComparison.Ordinal)
                || draft.RunAsAdmin != entry.RunAsAdmin
                || draft.IsEnabled != entry.IsEnabled
                || !string.Equals(TaskTypeCatalog.Normalize(draft.TaskType), TaskTypeCatalog.Normalize(entry.TaskType), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CompanionDraftsMatchShortcut(
        List<PersistedShortcutCompanionDraft> draftCompanions,
        List<CompanionAppEntry> savedCompanions)
    {
        var saved = savedCompanions.OrderBy(entry => entry.Order).ToList();
        if (draftCompanions.Count != saved.Count)
        {
            return false;
        }

        for (var i = 0; i < draftCompanions.Count; i++)
        {
            var draft = draftCompanions[i];
            var entry = saved[i];
            if (!string.Equals(Normalize(draft.Path), Normalize(entry.Path), StringComparison.Ordinal)
                || !string.Equals(Normalize(draft.Arguments), Normalize(entry.Arguments), StringComparison.Ordinal)
                || draft.OpenOnLaunch != entry.OpenOnLaunch)
            {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private void WithLock(Action action) =>
        WithLock(() =>
        {
            action();
            return true;
        });

    private T WithLock<T>(Func<T> action)
    {
        if (!_sync.Wait(LockTimeout))
        {
            RepositoryDiagnostics.Report("ShortcutDraftStore.WithLock", "lock-timeout", (long)LockTimeout.TotalMilliseconds);
            throw new TimeoutException("Timed out waiting for the shortcut draft store lock.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            _sync.Release();
            if (stopwatch.Elapsed > SlowOperationThreshold)
            {
                RepositoryDiagnostics.Report("ShortcutDraftStore.WithLock", "slow-operation", stopwatch.ElapsedMilliseconds);
            }
        }
    }

    internal void FlushPendingFileIoForTests()
    {
        WithLock(DrainFileIoQueueLocked);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            WithLock(DrainFileIoQueueLocked);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            // Best effort drain during shutdown.
        }

        _sync.Dispose();
        GC.SuppressFinalize(this);
    }
}
