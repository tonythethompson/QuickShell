using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class OpenShortcutLaunchCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;
    private readonly string _launchId;
    private readonly bool _runAsAdmin;
    private readonly bool _runAsStandard;

    public OpenShortcutLaunchCommand(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        IQuickShellServices services,
        bool runAsAdmin = false,
        bool runAsStandard = false)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _shortcutId = shortcut.Id;
        _launchId = launch.Id;
        _runAsAdmin = runAsAdmin;
        _runAsStandard = runAsStandard;
        Id = CommandDescriptor.OpenLaunch(shortcut.Id, launch.Id, runAsAdmin, runAsStandard).Id;
        var enabledLaunches = ShortcutLaunchNormalization.GetLaunchesForDisplay(shortcut);
        Name = ShortcutDisplay.GetLaunchContextMenuTitle(launch, enabledLaunches);
        Icon = new IconInfo(
            runAsAdmin || (launch.RunAsAdmin && !runAsStandard)
                ? ShortcutGlyphs.AdminLaunch
                : TerminalLaunchGlyphs.GetForList(launch));
    }

    public override CommandResult Invoke()
    {
        var settings = _services.Settings;
        var launchDefaults = settings.GetValidatedLaunchDefaults();
        var result = _services.WorkspaceLaunch.LaunchEntry(
            _shortcutId,
            _launchId,
            launchDefaults.TerminalApplicationId,
            launchDefaults.DefaultProfileId,
            new ShortcutLaunchOptions(
                _runAsAdmin,
                _runAsStandard,
                IncludeCompanionApp: false,
                IncludeDevServerLink: false,
                BlockDirtyBranchSwitch: settings.BlockDirtyBranchSwitch));

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
