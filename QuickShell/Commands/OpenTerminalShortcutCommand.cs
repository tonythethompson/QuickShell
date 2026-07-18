using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class OpenTerminalShortcutCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;
    private readonly bool _runAsAdmin;
    private readonly bool _runAsStandard;

    public OpenTerminalShortcutCommand(
        TerminalShortcut shortcut,
        IQuickShellServices services,
        bool runAsAdmin = false,
        bool runAsStandard = false)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _shortcutId = shortcut.Id;
        _runAsAdmin = runAsAdmin;
        _runAsStandard = runAsStandard;
        Id = CommandDescriptor.OpenWorkspace(shortcut.Id, runAsAdmin, runAsStandard).Id;
        Name = runAsAdmin
            ? Strings.Menu_RunAsAdmin
            : runAsStandard
                ? Strings.Menu_RunNormally
                : Strings.Menu_Run;
        Icon = new IconInfo(ResolveLaunchIcon(shortcut, runAsAdmin, runAsStandard));
    }

    private static string ResolveLaunchIcon(TerminalShortcut shortcut, bool runAsAdmin, bool runAsStandard)
    {
        const bool requireDirectoryExists = false;
        var needsRepair = ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists);

        if (runAsStandard)
        {
            return needsRepair
                ? ShortcutGlyphs.IncidentTriangle
                : TerminalLaunchGlyphs.GetForShortcut(shortcut);
        }

        if (runAsAdmin || shortcut.RunAsAdmin)
        {
            return ShortcutGlyphs.AdminLaunch;
        }

        return ShortcutHealth.GetListGlyph(shortcut, needsRepair);
    }

    public override CommandResult Invoke()
    {
        var settings = _services.Settings;
        var result = _services.WorkspaceLaunch.Launch(
            _shortcutId,
            settings.TerminalApplicationId,
            settings.DefaultProfileId,
            new ShortcutLaunchOptions(
                _runAsAdmin,
                _runAsStandard,
                BlockDirtyBranchSwitch: settings.BlockDirtyBranchSwitch,
                SeparateWindowsForMultiLaunch: settings.SeparateWindowsForMultiLaunch));

        return ToCommandResult(result);
    }

    private CommandResult ToCommandResult(ShortcutLaunchResult result)
    {
        LaunchDiagnosticsState.Set(result.Diagnostics);

        if (result.MarkUsed)
        {
            _services.Shortcuts.MarkUsed(_shortcutId);
        }

        return result.Dismiss
            ? CommandResult.Dismiss()
            : QuickShellNavigation.StayOpen(result.StayOpenMessage ?? "Launch failed.");
    }
}
