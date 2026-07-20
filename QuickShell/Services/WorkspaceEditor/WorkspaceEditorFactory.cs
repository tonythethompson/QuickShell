using QuickShell.Abstractions;

namespace QuickShell.Services.WorkspaceEditor;

internal sealed class WorkspaceEditorFactory : IWorkspaceEditorFactory
{
    private readonly Func<IQuickShellServices> _services;
    private readonly IQuickShellLifetime _lifetime;

    /// <summary>
    /// Initializes a factory for creating workspace editors.
    /// </summary>
    /// <param name="services">A delegate that provides the services used by created editors.</param>
    /// <param name="lifetime">The lifetime used by created editors.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="lifetime"/> is <see langword="null"/>.</exception>
    public WorkspaceEditorFactory(Func<IQuickShellServices> services, IQuickShellLifetime lifetime)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    /// <summary>
        /// Creates a workspace editor with an optional callback invoked after saving.
        /// </summary>
        /// <param name="onSaved">The callback to invoke after the workspace is saved.</param>
        /// <returns>A workspace editor.</returns>
        public IWorkspaceEditor Create(Action? onSaved = null) =>
        new WorkspaceEditor(_services(), _lifetime, onSaved);
}
