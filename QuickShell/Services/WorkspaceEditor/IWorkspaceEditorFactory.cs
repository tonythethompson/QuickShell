namespace QuickShell.Services.WorkspaceEditor;

internal interface IWorkspaceEditorFactory
{
    /// <summary>
    /// Creates a workspace editor.
    /// </summary>
    /// <param name="onSaved">An optional callback invoked when the workspace is saved.</param>
    /// <returns>The created workspace editor.</returns>
    IWorkspaceEditor Create(Action? onSaved = null);
}
