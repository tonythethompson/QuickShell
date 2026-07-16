using System.Collections.Concurrent;
using QuickShell.Models;

namespace QuickShell.Services;

internal enum WorkspaceAttentionState
{
    None,
    Branch,
    Warning,
    Blocking,
}

internal enum WorkspaceActivityState
{
    None,
    Running,
}

internal sealed record WorkspaceStatusSnapshot(
    WorkspaceHealthResult Health,
    WorkspaceGitStatus? Git,
    string? TargetBranch,
    DateTimeOffset RefreshedAt,
    bool IsStale)
{
    public bool HasTargetMismatch =>
        Git is not null
        && !string.IsNullOrWhiteSpace(TargetBranch)
        && !WorkspaceGitOperations.IsOnBranch(Git, TargetBranch);

    public WorkspaceAttentionState Attention =>
        Health.HasBlockingErrors ? WorkspaceAttentionState.Blocking
        : Health.WarningFindings.Count > 0 ? WorkspaceAttentionState.Warning
        : HasTargetMismatch || Git?.IsDirty == true ? WorkspaceAttentionState.Branch
        : WorkspaceAttentionState.None;

    public WorkspaceActivityState Activity =>
        Health.HasRunningSignal ? WorkspaceActivityState.Running : WorkspaceActivityState.None;

    public string AttentionSummary => Attention switch
    {
        WorkspaceAttentionState.Blocking => "Workspace needs attention",
        WorkspaceAttentionState.Warning => "Workspace health warning",
        WorkspaceAttentionState.Branch when HasTargetMismatch => "Configured branch differs from HEAD",
        WorkspaceAttentionState.Branch when Git?.IsDirty == true => "Working tree has uncommitted changes",
        _ => "No current issues",
    };

    public string AttentionEvidence => Attention switch
    {
        WorkspaceAttentionState.Blocking => WorkspaceHealthCheck.FormatFindingsEvidence(Health.BlockingFindings),
        WorkspaceAttentionState.Warning => WorkspaceHealthCheck.FormatFindingsEvidence(Health.WarningFindings),
        WorkspaceAttentionState.Branch when HasTargetMismatch =>
            $"Branch '{Git!.Branch}' differs from configured target '{TargetBranch}'.",
        WorkspaceAttentionState.Branch when Git?.IsDirty == true => "Working tree has uncommitted changes.",
        _ => "No current issues",
    };

    public string ActivitySummary =>
        Activity == WorkspaceActivityState.Running
            ? string.Join(" · ", Health.Findings
                .Where(finding => finding.IsRunningSignal)
                .Select(finding => finding.Detail ?? finding.Title))
            : "No runtime signal detected";
}

internal static class WorkspaceStatusLabels
{
    public const string RunningBadgeSummary = "Workspace appears to be running";
}

internal static class WorkspaceStatusService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Hard cap on unique directory/terminal/profile entries so long sessions cannot grow unbounded.
    /// </summary>
    internal const int MaxCacheEntries = 64;

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> InsertionOrder = new();
    private static readonly object EvictionLock = new();

    /// <summary>Test seam for stale-entry pruning without waiting on wall clock.</summary>
    internal static Func<DateTimeOffset>? UtcNowOverride { get; set; }

    internal static int CacheCountForTests
    {
        get
        {
            lock (EvictionLock)
            {
                return Cache.Count;
            }
        }
    }

    private static DateTimeOffset UtcNow => UtcNowOverride?.Invoke() ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// Snapshot for status UI / Run detail rows. Uses volatile checks (ports/processes)
    /// but skips git in <see cref="Capture"/> defaults below when configured that way.
    /// Do <b>not</b> call this from typing/search list rebuilds — use
    /// <see cref="TryGetCached"/> only (CmdPal tags and Run listMode already do).
    /// </summary>
    public static WorkspaceStatusSnapshot CaptureForList(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId) =>
        Capture(shortcut, terminalApplicationId, defaultProfileId, forceRefresh: false);

    public static bool TryGetCached(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        out WorkspaceStatusSnapshot snapshot)
    {
        var key = BuildCacheKey(shortcut.Directory, terminalApplicationId, defaultProfileId);
        return TryGetFresh(key, out snapshot);
    }

    public static WorkspaceStatusSnapshot Capture(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        bool forceRefresh = false)
    {
        var key = BuildCacheKey(shortcut.Directory, terminalApplicationId, defaultProfileId);
        if (!forceRefresh && TryGetFresh(key, out var cached))
        {
            return cached;
        }

        var health = WorkspaceHealthCheck.Check(
            shortcut,
            terminalApplicationId,
            defaultProfileId,
            includeVolatile: true,
            includeGit: false);
        var git = WorkspaceGitOperations.TryGetStatus(shortcut.Directory, out var current)
            ? current
            : null;
        var target = git is null
            ? null
            : WorktreeBranchTargetStore.GetTargetForDirectory(shortcut.Directory);
        var snapshot = new WorkspaceStatusSnapshot(
            health,
            git,
            target,
            UtcNow,
            IsStale: false);
        Cache[key] = new CacheEntry(snapshot);
        TrackInsertion(key);
        return snapshot;
    }

    public static void Invalidate(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var normalized = WorkspacePath.TryNormalizeLexical(directory, out var path, out _)
            ? path
            : directory.Trim();
        var prefix = normalized + "\u001F";

        List<string>? removed = null;
        foreach (var key in Cache.Keys.ToArray())
        {
            if (string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (Cache.TryRemove(key, out _))
                {
                    removed ??= [];
                    removed.Add(key);
                }
            }
        }

        if (removed is null)
        {
            return;
        }

        lock (EvictionLock)
        {
            foreach (var key in removed)
            {
                RemoveFromQueue(key);
            }
        }
    }

    internal static void ResetCacheForTests()
    {
        lock (EvictionLock)
        {
            Cache.Clear();
            InsertionOrder.Clear();
        }

        UtcNowOverride = null;
    }

    private static bool TryGetFresh(string key, out WorkspaceStatusSnapshot snapshot)
    {
        snapshot = null!;
        if (!Cache.TryGetValue(key, out var cached))
        {
            return false;
        }

        if (UtcNow - cached.Snapshot.RefreshedAt > CacheLifetime)
        {
            // Drop expired entries immediately so they do not linger until the next insert prune.
            Cache.TryRemove(key, out _);
            return false;
        }

        snapshot = cached.Snapshot;
        return true;
    }

    private static void TrackInsertion(string key)
    {
        lock (EvictionLock)
        {
            RemoveFromQueue(key);
            InsertionOrder.Enqueue(key);
            PruneStaleAndOverCapacity();
        }
    }

    /// <summary>
    /// Opportunistic stale trim from the front of the insertion queue, then FIFO
    /// eviction until under <see cref="MaxCacheEntries"/>.
    /// </summary>
    private static void PruneStaleAndOverCapacity()
    {
        var now = UtcNow;

        while (InsertionOrder.TryPeek(out var oldest))
        {
            if (!Cache.TryGetValue(oldest, out var entry))
            {
                // Ghost key (removed by Invalidate / TryGetFresh) — drop from order tracking.
                InsertionOrder.Dequeue();
                continue;
            }

            if (now - entry.Snapshot.RefreshedAt > CacheLifetime)
            {
                InsertionOrder.Dequeue();
                Cache.TryRemove(oldest, out _);
                continue;
            }

            // Front is still fresh; later entries are newer.
            break;
        }

        while (Cache.Count > MaxCacheEntries && InsertionOrder.TryDequeue(out var victim))
        {
            Cache.TryRemove(victim, out _);
        }

        // Safety: if the order queue lagged behind the dictionary, trim arbitrary extras.
        if (Cache.Count <= MaxCacheEntries)
        {
            return;
        }

        foreach (var orphan in Cache.Keys.ToArray())
        {
            if (Cache.Count <= MaxCacheEntries)
            {
                break;
            }

            Cache.TryRemove(orphan, out _);
        }
    }

    private static void RemoveFromQueue(string key)
    {
        if (InsertionOrder.Count == 0)
        {
            return;
        }

        var retained = new Queue<string>(InsertionOrder.Count);
        while (InsertionOrder.TryDequeue(out var entry))
        {
            if (!string.Equals(entry, key, StringComparison.Ordinal))
            {
                retained.Enqueue(entry);
            }
        }

        while (retained.TryDequeue(out var entry))
        {
            InsertionOrder.Enqueue(entry);
        }
    }

    private static string BuildCacheKey(
        string directory,
        string terminalApplicationId,
        string defaultProfileId)
    {
        var normalized = WorkspacePath.TryNormalizeLexical(directory, out var path, out _)
            ? path
            : directory.Trim();
        return string.Join("\u001F", normalized, terminalApplicationId, defaultProfileId);
    }

    private sealed record CacheEntry(WorkspaceStatusSnapshot Snapshot);
}
