using QuickShell.Classification;

namespace QuickShell.Abstractions.Classification;

internal interface IProjectLayoutAnalyzer
{
    ProjectLayout Analyze(string rootPath);
}
