using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class OpenTerminalShortcutCommand : InvokableCommand
{
    private readonly string _shortcutId;
    private readonly QuickShellSettingsManager _settings;
    private readonly bool _runAsAdmin;
    private readonly bool _runAsStandard;

    public OpenTerminalShortcutCommand(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        bool runAsAdmin = false,
        bool runAsStandard = false)
    {
        _shortcutId = shortcut.Id;
        _settings = settings;
        _runAsAdmin = runAsAdmin;
        _runAsStandard = runAsStandard;
        Id = runAsAdmin
            ? $"{ShortcutCommandIds.Open(shortcut.Id)}.admin"
            : runAsStandard
                ? $"{ShortcutCommandIds.Open(shortcut.Id)}.standard"
                : ShortcutCommandIds.Open(shortcut.Id);
        Name = runAsAdmin
            ? "Run as Admin"
            : runAsStandard
                ? "Run normally"
                : "Run";
        Icon = new IconInfo(ResolveLaunchIcon(shortcut, runAsAdmin, runAsStandard));
    }

    private static string ResolveLaunchIcon(TerminalShortcut shortcut, bool runAsAdmin, bool runAsStandard)
    {
        if (runAsStandard)
        {
            return ShortcutHealth.NeedsRepair(shortcut)
                ? ShortcutGlyphs.IncidentTriangle
                : TerminalLaunchGlyphs.GetForShortcut(shortcut);
        }

        if (runAsAdmin || shortcut.RunAsAdmin)
        {
            return ShortcutGlyphs.AdminLaunch;
        }

        return ShortcutHealth.GetListGlyph(shortcut);
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellRuntimeServices.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        var result = ShortcutLaunchExecutor.Launch(
            shortcut,
            _settings.TerminalApplicationId,
            _settings.DefaultProfileId,
            new ShortcutLaunchOptions(_runAsAdmin, _runAsStandard));

        return ToCommandResult(result);
    }

    private CommandResult ToCommandResult(ShortcutLaunchResult result)
    {
        if (result.MarkUsed)
        {
            QuickShellRuntimeServices.Shortcuts.MarkUsed(_shortcutId);
        }

        return result.Dismiss
            ? CommandResult.Dismiss()
            : QuickShellNavigation.StayOpen(result.StayOpenMessage ?? "Launch failed.");
    }
}
