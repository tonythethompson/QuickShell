using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class WorkspaceFormUndoCommand : InvokableCommand
{
    private readonly Func<bool> _tryFormUndo;
    private readonly Action? _onRepositoryChanged;

    public WorkspaceFormUndoCommand(Func<bool> tryFormUndo, Action? onRepositoryChanged = null)
    {
        _tryFormUndo = tryFormUndo;
        _onRepositoryChanged = onRepositoryChanged;
        Name = Strings.Command_Undo_Name;
        Icon = new IconInfo("\uE7A7");
    }

    public override CommandResult Invoke()
    {
        if (_tryFormUndo())
        {
            return QuickShellNavigation.StayOpen(Strings.Undo_Confirmation);
        }

        if (_onRepositoryChanged is null)
        {
            return QuickShellNavigation.StayOpen(Strings.Undo_NothingToUndo);
        }

        if (!QuickShellServices.Current.Shortcuts.Undo())
        {
            return QuickShellNavigation.StayOpen(Strings.Undo_NothingToUndo);
        }

        _onRepositoryChanged();
        return QuickShellNavigation.StayOpen(Strings.Undo_Confirmation);
    }
}

internal sealed partial class WorkspaceFormRedoCommand : InvokableCommand
{
    private readonly Func<bool> _tryFormRedo;
    private readonly Action? _onRepositoryChanged;

    public WorkspaceFormRedoCommand(Func<bool> tryFormRedo, Action? onRepositoryChanged = null)
    {
        _tryFormRedo = tryFormRedo;
        _onRepositoryChanged = onRepositoryChanged;
        Name = Strings.Command_Redo_Name;
        Icon = new IconInfo("\uE7A6");
    }

    public override CommandResult Invoke()
    {
        if (_tryFormRedo())
        {
            return QuickShellNavigation.StayOpen(Strings.Redo_Confirmation);
        }

        if (_onRepositoryChanged is null)
        {
            return QuickShellNavigation.StayOpen(Strings.Redo_NothingToRedo);
        }

        if (!QuickShellServices.Current.Shortcuts.Redo())
        {
            return QuickShellNavigation.StayOpen(Strings.Redo_NothingToRedo);
        }

        _onRepositoryChanged();
        return QuickShellNavigation.StayOpen(Strings.Redo_Confirmation);
    }
}
