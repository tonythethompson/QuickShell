using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services.WorkspaceEditor;

internal sealed class WorkspaceEditorFactory : IWorkspaceEditorFactory
{
    private readonly IShortcutRepository _shortcuts;
    private readonly IDraftStore _drafts;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ICommandSuggestionService _commandSuggestions;
    private readonly IQuickShellLifetime _lifetime;

    public WorkspaceEditorFactory(
        IShortcutRepository shortcuts,
        IDraftStore drafts,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        IQuickShellLifetime lifetime)
    {
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
        _projectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        _commandSuggestions = commandSuggestions ?? throw new ArgumentNullException(nameof(commandSuggestions));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    public IWorkspaceEditor Create(Action? onSaved = null) =>
        new WorkspaceEditor(
            _shortcuts,
            _drafts,
            _projectAnalysis,
            _commandSuggestions,
            _lifetime,
            onSaved);
}
