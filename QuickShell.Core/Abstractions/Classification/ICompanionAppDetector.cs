using QuickShell.Services;

namespace QuickShell.Abstractions.Classification;

internal interface ICompanionAppDetector
{
    CompanionAppSuggestion? TrySuggest(string directory);
}
