using QuickShell.Abstractions.Classification;
using QuickShell.Classification.Detectors;

namespace QuickShell.Services;

internal static class CompanionAppDetection
{
    private static readonly ICompanionAppDetector Default = new CompanionAppDetector();

    public static CompanionAppSuggestion? TrySuggestFromDirectory(string directory) =>
        Default.TrySuggest(directory);
}
