using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class OpenShortcutLaunchCommand : InvokableCommand
{
    private readonly string _shortcutId;
    private readonly string _launchId;
    private readonly QuickShellSettingsManager _settings;
    private readonly bool _runAsAdmin;
    private readonly bool _runAsStandard;

    public OpenShortcutLaunchCommand(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        QuickShellSettingsManager settings,
        bool runAsAdmin = false,
        bool runAsStandard = false)
    {
        _shortcutId = shortcut.Id;
        _launchId = launch.Id;
        _settings = settings;
        _runAsAdmin = runAsAdmin;
        _runAsStandard = runAsStandard;
        var baseId = ShortcutCommandIds.OpenLaunch(shortcut.Id, launch.Id);
        Id = runAsAdmin
            ? $"{baseId}.admin"
            : runAsStandard
                ? $"{baseId}.standard"
                : baseId;
        var enabledLaunches = ShortcutLaunchNormalization.GetLaunchesForDisplay(shortcut);
        Name = ShortcutDisplay.GetLaunchContextMenuTitle(launch, enabledLaunches);
        Icon = new IconInfo(
            runAsAdmin || (launch.RunAsAdmin && !runAsStandard)
                ? ShortcutGlyphs.AdminLaunch
                : TerminalLaunchGlyphs.GetForLaunch(launch));
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellServices.Current.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen(Strings.WorkspaceNotFound);
        }

        var launch = shortcut.Launches.FirstOrDefault(entry => entry.Id.Equals(_launchId, StringComparison.OrdinalIgnoreCase));
        if (launch is null || !launch.IsEnabled)
        {
            return QuickShellNavigation.StayOpen("That launch entry was not found.");
        }

        var result = ShortcutLaunchExecutor.LaunchEntry(
            shortcut,
            launch,
            _settings.TerminalApplicationId,
            _settings.DefaultProfileId,
            new ShortcutLaunchOptions(
                _runAsAdmin,
                _runAsStandard,
                IncludeCompanionApp: false,
                IncludeDevServerLink: false,
                BlockDirtyBranchSwitch: _settings.BlockDirtyBranchSwitch));

        LaunchDiagnosticsState.Set(result.Diagnostics);

        if (result.MarkUsed)
        {
            QuickShellServices.Current.Shortcuts.MarkUsed(_shortcutId);
        }

        return result.Dismiss
            ? CommandResult.Dismiss()
            : QuickShellNavigation.StayOpen(result.StayOpenMessage ?? "Launch failed.");
    }
}
