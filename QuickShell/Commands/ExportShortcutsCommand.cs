using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class ExportShortcutsCommand : InvokableCommand
{
    public ExportShortcutsCommand()
    {
        Name = "Export shortcuts";
        Icon = new IconInfo("\uE898");
    }

    public override CommandResult Invoke()
    {
        var path = ShortcutFilePickerService.PickExportFile();
        if (path is null)
        {
            return QuickShellNavigation.StayOpen("Export cancelled.");
        }

        if (!ShortcutStore.TryExportToFile(path, out var error))
        {
            return QuickShellNavigation.StayOpen($"Export failed: {error}");
        }

        return QuickShellNavigation.StayOpen($"Exported shortcuts to {path}.");
    }
}
