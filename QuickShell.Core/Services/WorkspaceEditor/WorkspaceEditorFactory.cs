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
    private readonly string _validationAtLeastOne;
    private readonly string _openInTerminalLabel;

    public WorkspaceEditorFactory(
        IShortcutRepository shortcuts,
        IDraftStore drafts,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        IQuickShellLifetime lifetime,
        string validationAtLeastOne,
        string openInTerminalLabel)
    {
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
        _projectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        _commandSuggestions = commandSuggestions ?? throw new ArgumentNullException(nameof(commandSuggestions));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _validationAtLeastOne = validationAtLeastOne ?? throw new ArgumentNullException(nameof(validationAtLeastOne));
        _openInTerminalLabel = openInTerminalLabel ?? throw new ArgumentNullException(nameof(openInTerminalLabel));
    }

    public IWorkspaceEditor Create(Action? onSaved = null) =>
        new WorkspaceEditor(
            _shortcuts,
            _drafts,
            _projectAnalysis,
            _commandSuggestions,
            _lifetime,
            _validationAtLeastOne,
            _openInTerminalLabel,
            onSaved);
}
