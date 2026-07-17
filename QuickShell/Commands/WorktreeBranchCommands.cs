using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class SelectWorktreeBranchCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;
    private readonly string _branch;
    private readonly Action _onChanged;

    public SelectWorktreeBranchCommand(
        string shortcutId,
        string branch,
        IQuickShellServices services,
        Action onChanged)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _shortcutId = shortcutId;
        _branch = branch;
        _onChanged = onChanged;
        Id = CommandDescriptor.WorktreeBranchSelect(shortcutId, branch).Id;
        Name = branch;
        Icon = new IconInfo("\uE8AB");
    }

    public override CommandResult Invoke()
    {
        var shortcut = _services.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        var result = _services.GitLaunchGate.SelectTargetBranch(
            shortcut.Directory,
            _branch,
            _services.Settings.BlockDirtyBranchSwitch);

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
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;
    private readonly Action _onChanged;

    public UseCurrentWorktreeBranchCommand(
        string shortcutId,
        Action onChanged,
        IQuickShellServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _shortcutId = shortcutId;
        _onChanged = onChanged;
        Id = CommandDescriptor.WorktreeBranchClear(shortcutId).Id;
        Name = "Use current branch";
        Icon = new IconInfo("\uE894");
    }

    public override CommandResult Invoke()
    {
        var shortcut = _services.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        _services.GitLaunchGate.ClearTargetBranch(shortcut.Directory);
        WorkspaceStatusService.Invalidate(shortcut.Directory);
        _onChanged();
        return QuickShellNavigation.GoBack("Worktree branch target cleared.");
    }
}
