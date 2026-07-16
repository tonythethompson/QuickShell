using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class SelectWorktreeBranchCommand : InvokableCommand
{
    private readonly string _shortcutId;
    private readonly string _branch;
    private readonly QuickShellSettingsManager _settings;
    private readonly Action _onChanged;

    public SelectWorktreeBranchCommand(
        string shortcutId,
        string branch,
        QuickShellSettingsManager settings,
        Action onChanged)
    {
        _shortcutId = shortcutId;
        _branch = branch;
        _settings = settings;
        _onChanged = onChanged;
        Id = CommandDescriptor.WorktreeBranchSelect(shortcutId, branch).Id;
        Name = branch;
        Icon = new IconInfo("\uE8AB");
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellServices.Current.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        var result = WorkspaceGitLaunchGate.SelectTargetBranch(
            shortcut.Directory,
            _branch,
            _settings.BlockDirtyBranchSwitch);

        if (!result.CanProceed)
        {
            return QuickShellNavigation.StayOpen(result.StayOpenMessage ?? "Branch could not be switched.");
        }

        WorkspaceStatusService.Invalidate(shortcut.Directory);
        _onChanged();
        return QuickShellNavigation.GoBack($"Switched to {_branch}.");
    }
}

internal sealed partial class UseCurrentWorktreeBranchCommand : InvokableCommand
{
    private readonly string _shortcutId;
    private readonly Action _onChanged;

    public UseCurrentWorktreeBranchCommand(string shortcutId, Action onChanged)
    {
        _shortcutId = shortcutId;
        _onChanged = onChanged;
        Id = CommandDescriptor.WorktreeBranchClear(shortcutId).Id;
        Name = "Use current branch";
        Icon = new IconInfo("\uE894");
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellServices.Current.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        WorkspaceGitLaunchGate.ClearTargetBranch(shortcut.Directory);
        WorkspaceStatusService.Invalidate(shortcut.Directory);
        _onChanged();
        return QuickShellNavigation.GoBack("Worktree branch target cleared.");
    }
}
