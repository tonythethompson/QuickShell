using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Core.Services;
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

    /// <summary>
    /// Initializes a builder for shortcut form views.
    /// </summary>
    /// <param name="terminalCatalog">The catalog used to provide terminal choices.</param>
    /// <param name="projectAnalysis">The service used to provide project analysis data.</param>
    /// <param name="commandSuggestions">The service used to provide command suggestions.</param>
    public ShortcutFormViewBuilder(
        ITerminalCatalog terminalCatalog,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions)
    {
        _terminalCatalog = terminalCatalog ?? throw new ArgumentNullException(nameof(terminalCatalog));
        _projectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        _commandSuggestions = commandSuggestions ?? throw new ArgumentNullException(nameof(commandSuggestions));
    }

    /// <summary>
    /// Builds the main shortcut form card for the specified workspace state.
    /// </summary>
    /// <param name="state">The workspace state used to populate the form.</param>
    /// <param name="terminalApplicationId">The identifier of the terminal application selected for the form.</param>
    /// <returns>The Adaptive Card template and data for the main shortcut form.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
    public ShortcutFormCard BuildMain(WorkspaceEditState state, string terminalApplicationId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var commandCount = state.Commands.Count;
        var companionCount = Math.Max(1, state.Companions.Count);
        var companionChoicesJson = CompanionAppCatalog.BuildFormChoicesJson();
        var taskTypeChoicesJson = TaskTypeCatalog.BuildFormChoicesJson(_projectAnalysis, state.Directory);
        var commandRows = state.Commands.ToList();
        var usedCommands = commandRows.Select(row => row.Command);
        var selectablePills = state.IsSuggestionScanning
            ? Array.Empty<CommandSuggestionPill>()
            : SuggestionPillPresentation.BuildSelectablePills(
                state.Directory,
                usedCommands,
                _projectAnalysis,
                _commandSuggestions);
        var visiblePillCount = SuggestionPillPresentation.GetVisiblePillCount(
            selectablePills.Count,
            state.ExpandSuggestionPills,
            state.IsSuggestionScanning);
        var templateSchemaKey = taskTypeChoicesJson + "|launch-kinds="
            + string.Join(',', commandRows.Select(row => row.Kind))
            + $"|pills={visiblePillCount}";

        var templateJson = ShortcutFormTemplateCache.GetOrBuild(
            commandCount,
            companionCount,
            terminalApplicationId,
            companionChoicesJson,
            templateSchemaKey,
            () => ShortcutFormTemplateJson.BuildTemplate(
                _terminalCatalog.BuildFormChoicesJson(includeDefaultChoice: true, terminalApplicationId),
                companionChoicesJson,
                commandRows,
                new LaunchEditorText(
                    Strings.LaunchEditor_AddCommand,
                    Strings.LaunchEditor_AddOpenInTerminal,
                    Strings.LaunchEditor_OpenInTerminal,
                    Strings.LaunchEditor_RemoveTooltip,
                    Strings.LaunchEditor_EmptyTitle,
                    Strings.LaunchEditor_EmptyGuidance,
                    Strings.LaunchEditor_ValidationAtLeastOne,
                    Strings.LaunchEditor_CommandsSectionTooltip,
                    Strings.LaunchEditor_CommandsSectionTitle),
                QuickShellBrand.DisplayName,
                companionCount,
                visiblePillCount));

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
            commandRows);

        return new ShortcutFormCard(templateJson, dataJson);
    }

    /// <summary>
    /// Builds the discard confirmation prompt card.
    /// </summary>
    /// <returns>A shortcut form card containing the discard prompt.</returns>
    public ShortcutFormCard BuildDiscardPrompt() =>
        new(ShortcutFormTemplateJson.BuildDiscardPromptTemplate(), "{}");
}
