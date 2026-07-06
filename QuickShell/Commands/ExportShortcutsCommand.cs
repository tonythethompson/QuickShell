using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Threading;

namespace QuickShell.Commands;

internal sealed partial class ExportShortcutsCommand : InvokableCommand
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(30);
    private readonly bool _stayOnSettings;

    public ExportShortcutsCommand(bool stayOnSettings = true)
    {
        _stayOnSettings = stayOnSettings;
        Name = Strings.Command_ExportWorkspaces_Name;
        Icon = new IconInfo("\uE896");
    }

    public override CommandResult Invoke()
    {
        var path = ShortcutFilePickerService.PickExportFile();
        if (path is null)
        {
            return Finish(Strings.Export_Cancelled);
        }

        using var cancellation = new CancellationTokenSource(IoTimeout);
        var result = QuickShellRuntimeServices.Shortcuts.TryExportToFileAsync(path, cancellation.Token).GetAwaiter().GetResult();
        if (!result.Success)
        {
            return Finish(Strings.ExportFailedFormat(result.Error));
        }

        return Finish(Strings.ExportedWorkspacesFormat(path));
    }

    private CommandResult Finish(string? message) =>
        _stayOnSettings
            ? QuickShellNavigation.StayOnSettings(message)
            : QuickShellNavigation.StayOpen(message);
}
