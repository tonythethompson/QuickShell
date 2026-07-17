namespace QuickShell.Services.WorkspaceEditor;

internal interface IWorkspaceEditorFactory
{
    WorkspaceEditor Create(Action? onSaved = null);
}
