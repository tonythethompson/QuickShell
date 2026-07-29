using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Pages;

internal sealed partial class WorktreeBranchPickerPage : DynamicListPage
{
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;
    private readonly Action _onChanged;
    private readonly WorkspaceGitStatus? _knownStatus;
    private readonly string? _knownTargetBranch;
    private IListItem[]? _items;
    /// <summary>Single-slot handoff from the background load to this page's fetch thread.</summary>
    private IListItem[]? _pendingItems;
    private int _loadStarted;

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

    public override IListItem[] GetItems()
    {
        // Deliberately does not drain IExtensionCallbackQueue: that queue is process-wide, so
        // draining it here would run other pages' callbacks (including a full home-list
        // rebuild) on this page's fetch thread. The background load hands off through
        // _pendingItems instead, which only this page owns.
        if (Interlocked.Exchange(ref _pendingItems, null) is { } delivered)
        {
            _items = delivered;
            IsLoading = false;
        }

        if (_items is { } published)
        {
            return published;
        }

        // BuildItems() runs git status + for-each-ref, ~0.5s on a real repository. Returning
        // a placeholder and loading in the background keeps navigation into this page
        // instant; the host refetches when the background load publishes.
        StartLoad();
        IsLoading = true;
        return
        [
            new ListItem(new NoOpCommand())
            {
                Title = Strings.BranchPicker_SelectLocalBranch,
                Subtitle = Strings.Menu_SwitchBranch,
                Icon = Icon,
            },
        ];
    }

    private void StartLoad()
    {
        if (Interlocked.Exchange(ref _loadStarted, 1) == 1)
        {
            return;
        }

        var cancellationToken = _services.Lifetime.CancellationToken;
        _ = Task.Run(
            () =>
            {
                IListItem[] built;
                var failed = false;
                try
                {
                    built = BuildItems();
                }
                catch (Exception ex)
                {
                    // Keep catch-all so unexpected failures do not leave the loading
                    // placeholder forever, but surface the message and allow retry.
                    failed = true;
                    built =
                    [
                        new ListItem(new NoOpCommand())
                        {
                            Title = Strings.BranchPicker_NotAGitRepository,
                            Subtitle = ex.Message,
                        },
                    ];
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Hand off before clearing _loadStarted so GetItems cannot start a second
                // BuildItems while the fallback is still unpublished.
                Interlocked.Exchange(ref _pendingItems, built);
                if (failed)
                {
                    Interlocked.Exchange(ref _loadStarted, 0);
                }

                try
                {
                    RaiseItemsChanged();
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Host may reject a cross-thread notification while tearing down.
                    // The queued items still apply on the next GetItems.
                }
            },
            cancellationToken);
    }

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
