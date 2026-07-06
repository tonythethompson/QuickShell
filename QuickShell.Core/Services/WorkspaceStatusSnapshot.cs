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
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

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
            DateTimeOffset.UtcNow,
            IsStale: false);
        Cache[key] = new CacheEntry(snapshot);
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

        foreach (var key in Cache.Keys.ToArray())
        {
            if (string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Cache.TryRemove(key, out _);
            }
        }
    }

    internal static void ResetCacheForTests() => Cache.Clear();

    private static bool TryGetFresh(string key, out WorkspaceStatusSnapshot snapshot)
    {
        snapshot = null!;
        if (!Cache.TryGetValue(key, out var cached)
            || DateTimeOffset.UtcNow - cached.Snapshot.RefreshedAt > CacheLifetime)
        {
            return false;
        }

        snapshot = cached.Snapshot;
        return true;
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
