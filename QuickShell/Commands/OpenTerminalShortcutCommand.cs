using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;
using System.ComponentModel;

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
        Icon = new IconInfo(runAsAdmin || (shortcut.RunAsAdmin && !runAsStandard) ? ShortcutGlyphs.AdminLaunch : ShortcutGlyphs.Terminal);
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellRuntimeServices.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That shortcut was not found.");
        }

        try
        {
            TerminalLauncher.Open(
                shortcut,
                _settings.TerminalApplicationId,
                _settings.DefaultProfileId,
                _runAsAdmin,
                _runAsStandard);
            QuickShellRuntimeServices.Shortcuts.MarkUsed(shortcut.Id);
            return CommandResult.Dismiss();
        }
        catch (DirectoryNotFoundException)
        {
            return QuickShellNavigation.StayOpen("Failed to open terminal: the folder path could not be found.");
        }
        catch (InvalidOperationException)
        {
            return QuickShellNavigation.StayOpen("Failed to open terminal: check the shortcut settings and try again.");
        }
        catch (Win32Exception)
        {
            return QuickShellNavigation.StayOpen("Failed to open terminal: launch was canceled or blocked by the system.");
        }
    }
}
