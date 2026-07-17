namespace QuickShell.Services.WorkspaceEditor;

internal sealed class WorkspaceEditChangedEventArgs : EventArgs
{
    public WorkspaceEditChangedEventArgs(WorkspaceEditState state)
    {
        State = state;
    }

    public WorkspaceEditState State { get; }
}
