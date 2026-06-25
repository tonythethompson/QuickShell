using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class ImportReplaceShortcutsCommand : InvokableCommand
{
    private readonly Action _onReload;
    private string? _pendingPath;
    private int _pendingImportCount;
    private int _pendingCurrentCount;

    public ImportReplaceShortcutsCommand(Action onReload)
    {
        _onReload = onReload;
        Name = "Import and replace shortcuts";
        Icon = new IconInfo("\uE7C5");
    }

    public override CommandResult Invoke()
    {
        var path = ShortcutFilePickerService.PickImportFile();
        if (path is null)
        {
            return QuickShellNavigation.StayOpen("Import cancelled.");
        }

        if (!ShortcutStore.TryReadImportFile(path, out var imported, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        _pendingPath = path;
        _pendingImportCount = imported.Length;
        _pendingCurrentCount = ShortcutStore.GetShortcuts().Count;

        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = "Replace all shortcuts?",
            Description =
                $"Replace your {_pendingCurrentCount} shortcut{(_pendingCurrentCount == 1 ? "" : "s")} " +
                $"with {_pendingImportCount} from the file? You can undo afterward.",
            PrimaryCommand = new ImportReplaceConfirmCommand(this)
            {
                Name = "Replace",
            },
        });
    }

    internal CommandResult ConfirmReplace()
    {
        if (string.IsNullOrWhiteSpace(_pendingPath))
        {
            return QuickShellNavigation.StayOpen("Import failed: no file selected.");
        }

        var result = ShortcutStore.ImportReplace(_pendingPath);
        _pendingPath = null;

        if (!result.Success)
        {
            return QuickShellNavigation.StayOpen(result.Message);
        }

        _onReload();
        return QuickShellNavigation.StayOpen(result.Message);
    }

    private sealed partial class ImportReplaceConfirmCommand : InvokableCommand
    {
        private readonly ImportReplaceShortcutsCommand _parent;

        public ImportReplaceConfirmCommand(ImportReplaceShortcutsCommand parent) => _parent = parent;

        public override CommandResult Invoke() => _parent.ConfirmReplace();
    }
}
