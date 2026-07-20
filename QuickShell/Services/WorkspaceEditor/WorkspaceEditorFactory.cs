using QuickShell.Abstractions;

namespace QuickShell.Services.WorkspaceEditor;

internal sealed class WorkspaceEditorFactory : IWorkspaceEditorFactory
{
    private readonly Func<IQuickShellServices> _services;
    private readonly IQuickShellLifetime _lifetime;

    public WorkspaceEditorFactory(Func<IQuickShellServices> services, IQuickShellLifetime lifetime)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    public IWorkspaceEditor Create(Action? onSaved = null) =>
        new WorkspaceEditor(_services(), _lifetime, onSaved);
}
