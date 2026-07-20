using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Run;

internal static class RunWorkspaceSubtitle
{
    public static string Build(
        TerminalShortcut shortcut,
        QuickShellSettingsReader settings,
        IWorkspaceHealthChecker healthChecker,
        IWorkspaceGitOperations gitOperations,
        IWorktreeBranchTargetStore targetStore,
        ITerminalCatalog catalog,
        bool listMode = false)
    {
        var subtitle = ShortcutHealth.BuildListSubtitle(shortcut, catalog);
        var status = TryBuildStatusSuffix(shortcut, settings, healthChecker, gitOperations, targetStore, listMode);
        return string.IsNullOrWhiteSpace(status) ? subtitle : $"{subtitle} · {status}";
    }

    private static string? TryBuildStatusSuffix(
        TerminalShortcut shortcut,
        QuickShellSettingsReader settings,
        IWorkspaceHealthChecker healthChecker,
        IWorkspaceGitOperations gitOperations,
        IWorktreeBranchTargetStore targetStore,
        bool listMode)
    {
        if (ShortcutHealth.WouldNeedRepair(shortcut))
        {
            return null;
        }

        try
        {
            WorkspaceStatusSnapshot snapshot;
            if (listMode)
            {
                if (!WorkspaceStatusService.TryGetCached(
                        shortcut,
                        settings.TerminalApplicationId,
                        settings.DefaultProfileId,
                        healthChecker,
                        gitOperations,
                        out snapshot))
                {
                    return null;
                }
            }
            else
            {
                snapshot = WorkspaceStatusService.CaptureForList(
                    shortcut,
                    settings.TerminalApplicationId,
                    settings.DefaultProfileId,
                    healthChecker,
                    gitOperations,
                    targetStore);
            }

            if (snapshot.Activity == WorkspaceActivityState.Running)
            {
                return WorkspaceStatusLabels.RunningBadgeSummary;
            }

            return snapshot.Attention switch
            {
                WorkspaceAttentionState.Blocking => "Needs attention",
                WorkspaceAttentionState.Warning => "Health warning",
                WorkspaceAttentionState.Branch when snapshot.HasTargetMismatch =>
                    $"Branch {snapshot.Git?.Branch} ≠ {snapshot.TargetBranch}",
                WorkspaceAttentionState.Branch when snapshot.Git?.IsDirty == true => "Dirty working tree",
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}
