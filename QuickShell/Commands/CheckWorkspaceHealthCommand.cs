using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class CheckWorkspaceHealthCommand : InvokableCommand
{
    private readonly string _shortcutId;
    private readonly QuickShellSettingsManager _settings;

    public CheckWorkspaceHealthCommand(TerminalShortcut shortcut, QuickShellSettingsManager settings)
    {
        _shortcutId = shortcut.Id;
        _settings = settings;
        Name = "Check workspace health";
        Icon = new IconInfo(ShortcutGlyphs.IncidentTriangle);
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellRuntimeServices.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        var health = WorkspaceHealthCheck.Check(
            shortcut,
            _settings.TerminalApplicationId,
            _settings.DefaultProfileId);
        return QuickShellNavigation.StayOpen(WorkspaceHealthCheck.FormatDetailedSummary(health));
    }
}
