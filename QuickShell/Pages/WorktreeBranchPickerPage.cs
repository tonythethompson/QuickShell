using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class WorktreeBranchPickerPage : DynamicListPage
{
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;
    private readonly Action _onChanged;
    private readonly WorkspaceGitStatus? _knownStatus;
    private readonly string? _knownTargetBranch;
    private IListItem[]? _items;

    public WorktreeBranchPickerPage(
        IQuickShellServices services,
        string shortcutId,
        Action onChanged,
        WorkspaceGitStatus? knownStatus = null,
        string? knownTargetBranch = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _shortcutId = shortcutId;
        _onChanged = onChanged;
        _knownStatus = knownStatus;
        _knownTargetBranch = knownTargetBranch;
        Id = CommandDescriptor.WorktreeBranchPicker(shortcutId).Id;
        Title = Strings.Menu_SwitchBranch;
        Name = Strings.Menu_SwitchBranch;
        Icon = new IconInfo("\uE8AB");
        // BuildItems() runs git (status + branch list). Deferred to GetItems() \u2014 every
        // home-list row constructs this page as part of its context menu, so doing that
        // work in the constructor would run git once per visible row on every refresh.
    }

    public override IListItem[] GetItems() => _items ??= BuildItems();

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
                    Title = Strings.BranchPicker_WorkspaceNotFound,
                },
            ];
        }

        var status = _knownStatus;
        if (status is null && !_services.GitOperations.TryGetStatus(shortcut.Directory, out status))
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = Strings.BranchPicker_NotAGitRepository,
                    Subtitle = shortcut.Directory,
                },
            ];
        }

        var target = _knownTargetBranch
            ?? _services.TargetStore.GetTargetForDirectory(shortcut.Directory, _services.GitOperations);
        var branches = _services.GitOperations.ListLocalBranches(shortcut.Directory);
        if (branches.Count == 0)
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = Strings.BranchPicker_NoLocalBranches,
                    Subtitle = WorkspaceGitOperations.FormatBranchContextLabel(status, target),
                },
            ];
        }

        var items = new List<IListItem>
        {
            new ListItem(new NoOpCommand())
            {
                Title = WorkspaceGitOperations.FormatBranchContextLabel(status, target),
                Subtitle = Strings.BranchPicker_SelectLocalBranch,
                Icon = new IconInfo("\uE8AB"),
            },
        };

        if (!string.IsNullOrWhiteSpace(target))
        {
            items.Add(new ListItem(new UseCurrentWorktreeBranchCommand(_shortcutId, _onChanged, _services))
            {
                Title = Strings.Menu_UseCurrentBranch,
                Subtitle = Strings.BranchPicker_ClearTargetPinnedFormat(target),
                Icon = new IconInfo("\uE894"),
            });
        }

        foreach (var branch in branches)
        {
            var isCurrent = WorkspaceGitOperations.IsOnBranch(status, branch);
            items.Add(new ListItem(new SelectWorktreeBranchCommand(_shortcutId, branch, _services, _onChanged))
            {
                Title = branch,
                Subtitle = isCurrent ? Strings.BranchPicker_CurrentBranch : string.Empty,
                Icon = new IconInfo(isCurrent ? "\uE73E" : "\uE8AB"),
            });
        }

        return items.ToArray();
    }
}
