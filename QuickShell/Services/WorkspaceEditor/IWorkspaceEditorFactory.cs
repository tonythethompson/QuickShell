namespace QuickShell.Services.WorkspaceEditor;

internal interface IWorkspaceEditorFactory
{
    IWorkspaceEditor Create(Action? onSaved = null);
}
