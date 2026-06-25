using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class ImportShortcutsCommand : InvokableCommand
{
    private readonly Action _onReload;

    public ImportShortcutsCommand(Action onReload)
    {
        _onReload = onReload;
        Name = "Import shortcuts";
        Icon = new IconInfo("\uE8B5");
    }

    public override CommandResult Invoke()
    {
        var path = ShortcutFilePickerService.PickImportFile();
        if (path is null)
        {
            return QuickShellNavigation.StayOpen("Import cancelled.");
        }

        var result = ShortcutStore.ImportMerge(path);
        if (!result.Success)
        {
            return QuickShellNavigation.StayOpen(result.Message);
        }

        _onReload();
        return QuickShellNavigation.StayOpen(result.Message);
    }
}
