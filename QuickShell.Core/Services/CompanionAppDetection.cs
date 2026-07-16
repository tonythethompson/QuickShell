using QuickShell.Abstractions.Classification;
using QuickShell.Classification.Detectors;

namespace QuickShell.Services;

internal static class CompanionAppDetection
{
    private static readonly CompanionAppDetector Default = new();

    public static CompanionAppSuggestion? TrySuggestFromDirectory(string directory) =>
        Default.TrySuggest(directory);
}
