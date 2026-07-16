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
                : TerminalLaunchGlyphs.GetForLaunch(launch));
    }

    public override CommandResult Invoke()
    {
        var shortcut = _services.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen(Strings.WorkspaceNotFound);
        }

        var launch = shortcut.Launches.FirstOrDefault(entry => entry.Id.Equals(_launchId, StringComparison.OrdinalIgnoreCase));
        if (launch is null || !launch.IsEnabled)
        {
            return QuickShellNavigation.StayOpen("That launch entry was not found.");
        }

        var settings = _services.Settings;
        var result = ShortcutLaunchExecutor.LaunchEntry(
            shortcut,
            launch,
            settings.TerminalApplicationId,
            settings.DefaultProfileId,
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
