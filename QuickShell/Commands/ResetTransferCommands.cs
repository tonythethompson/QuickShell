using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class ResetProjectsCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly Action _onReload;
    private readonly Action? _onSettingsRefresh;

    public ResetProjectsCommand(Action onReload, Action? onSettingsRefresh, IQuickShellServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _onReload = onReload;
        _onSettingsRefresh = onSettingsRefresh;
        Name = "Reset all workspaces";
        Icon = new IconInfo("\uE74D");
    }

    public override CommandResult Invoke()
    {
        if (ImportConflictState.Pending?.Kind == ImportTransferKind.Projects)
        {
            ImportConflictState.Clear();
        }

        _services.Drafts.Clear();
        var result = _services.Shortcuts.ResetAll();
        if (result.Success)
        {
            _onReload();
        }

        SettingsFormHelpers.ScheduleRefresh(_onSettingsRefresh);
        return QuickShellNavigation.StayOnSettings(result.Message);
    }
}
