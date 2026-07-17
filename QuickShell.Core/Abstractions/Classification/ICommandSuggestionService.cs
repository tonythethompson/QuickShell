using QuickShell.Services;

namespace QuickShell.Abstractions.Classification;

internal interface ICommandSuggestionService
{
    IReadOnlyList<CommandSuggestionPill> GetPills(
        string? directory,
        IEnumerable<string?> usedCommands,
        IProjectAnalysisService projectAnalysis,
        int maxCount = SuggestionPillPresentation.MaxSlots);

    bool HasSuggestions(string? directory, IEnumerable<string?> usedCommands, IProjectAnalysisService projectAnalysis);

    CommandSuggestionPill? TryFindPill(IReadOnlyList<CommandSuggestionPill> pills, string? command, string? taskType);

    bool ApplyPill(List<LaunchRowDraft> rows, CommandSuggestionPill pill, string fallbackLaunchTarget);

    void ResetForTests();
}
