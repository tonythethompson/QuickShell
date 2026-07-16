using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class WorktreeBranchPickerPage : DynamicListPage
{
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;
    private readonly QuickShellSettingsManager _settings;
    private readonly Action _onChanged;
    private readonly WorkspaceGitStatus? _knownStatus;
    private readonly string? _knownTargetBranch;
    private IListItem[] _items = [];

    public WorktreeBranchPickerPage(
        string shortcutId,
        QuickShellSettingsManager settings,
        Action onChanged,
        WorkspaceGitStatus? knownStatus = null,
        string? knownTargetBranch = null,
        IQuickShellServices? services = null)
    {
        _services = services ?? throw new InvalidOperationException("IQuickShellServices is required.");
        _shortcutId = shortcutId;
        _settings = settings;
        _onChanged = onChanged;
        _knownStatus = knownStatus;
        _knownTargetBranch = knownTargetBranch;
        Id = ShortcutCommandIds.WorktreeBranchPicker(shortcutId);
        Title = "Switch branch";
        Name = "Switch branch";
        Icon = new IconInfo("\uE8AB");
        _items = BuildItems();
    }

    public override IListItem[] GetItems() => _items;

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
    }

    private IListItem[] BuildItems()
    {
        var shortcut = _services.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Workspace not found",
                },
            ];
        }

        var status = _knownStatus;
        if (status is null && !WorkspaceGitOperations.TryGetStatus(shortcut.Directory, out status))
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Not a git repository",
                    Subtitle = shortcut.Directory,
                },
            ];
        }

        var target = _knownTargetBranch ?? WorktreeBranchTargetStore.GetTargetForDirectory(shortcut.Directory);
        var branches = WorkspaceGitOperations.ListLocalBranches(shortcut.Directory);
        if (branches.Count == 0)
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "No local branches found",
                    Subtitle = WorkspaceGitOperations.FormatBranchContextLabel(status, target),
                },
            ];
        }

        var items = new List<IListItem>
        {
            new ListItem(new NoOpCommand())
            {
                Title = WorkspaceGitOperations.FormatBranchContextLabel(status, target),
                Subtitle = "Select a local branch",
                Icon = new IconInfo("\uE8AB"),
            },
        };

        foreach (var branch in branches)
        {
            var isCurrent = WorkspaceGitOperations.IsOnBranch(status, branch);
            items.Add(new ListItem(new SelectWorktreeBranchCommand(_shortcutId, branch, _settings, _onChanged, _services))
            {
                Title = branch,
                Subtitle = isCurrent ? "Current branch" : string.Empty,
                Icon = new IconInfo(isCurrent ? "\uE73E" : "\uE8AB"),
            });
        }

        return items.ToArray();
    }
}
