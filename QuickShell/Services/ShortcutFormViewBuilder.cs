using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Services.WorkspaceEditor;

namespace QuickShell.Services;

/// <summary>
/// Single owner of Adaptive Card JSON construction for the CmdPal workspace form.
/// </summary>
internal sealed class ShortcutFormViewBuilder : IShortcutFormViewBuilder
{
    private readonly ITerminalCatalog _terminalCatalog;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ICommandSuggestionService _commandSuggestions;

    public ShortcutFormViewBuilder(
        ITerminalCatalog terminalCatalog,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions)
    {
        _terminalCatalog = terminalCatalog ?? throw new ArgumentNullException(nameof(terminalCatalog));
        _projectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        _commandSuggestions = commandSuggestions ?? throw new ArgumentNullException(nameof(commandSuggestions));
    }

    public ShortcutFormCard BuildMain(WorkspaceEditState state, string terminalApplicationId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var commandCount = Math.Max(1, state.Commands.Count);
        var companionCount = Math.Max(1, state.Companions.Count);
        var companionChoicesJson = CompanionAppCatalog.BuildFormChoicesJson();
        var taskTypeChoicesJson = TaskTypeCatalog.BuildFormChoicesJson(_projectAnalysis, state.Directory);
        var commandTuples = state.Commands
            .Select(c => (c.Command, c.TaskType, c.LaunchTarget, c.RunAsAdmin))
            .ToList();

        var templateJson = ShortcutFormTemplateCache.GetOrBuild(
            commandCount,
            terminalApplicationId,
            companionChoicesJson,
            taskTypeChoicesJson,
            () => ShortcutFormTemplateJson.BuildTemplate(
                _terminalCatalog.BuildFormChoicesJson(includeDefaultChoice: true, terminalApplicationId),
                companionChoicesJson,
                commandTuples,
                QuickShellBrand.DisplayName,
                companionCount));

        var dataJson = ShortcutFormTemplateJson.BuildDataJson(
            new ShortcutFormTemplateJson.DataPayload
            {
                OriginalName = state.OriginalName ?? string.Empty,
                Name = state.Name,
                Abbreviation = state.Abbreviation,
                Directory = state.Directory,
                LaunchTarget = state.LaunchTarget,
                DevServerUrl = state.DevServerUrl,
                RepoUrl = state.RepoUrl,
                CompanionAppPreset = state.CompanionAppPreset,
                CompanionAppPath = state.CompanionAppPath,
                CompanionAppArguments = state.CompanionAppArguments,
                Companions = state.Companions,
                OpenDevServerOnLaunch = state.OpenDevServerOnLaunch,
                ShowRestoredDraftNote = state.ShowRestoredDraftNote,
                ExpandSuggestionPills = state.ExpandSuggestionPills,
                SuggestionScanning = state.IsSuggestionScanning,
                SaveError = state.SaveError ?? string.Empty,
            },
            _projectAnalysis,
            _commandSuggestions,
            commandTuples);

        return new ShortcutFormCard(templateJson, dataJson);
    }

    public ShortcutFormCard BuildDiscardPrompt() =>
        new(ShortcutFormTemplateJson.BuildDiscardPromptTemplate(), "{}");
}
