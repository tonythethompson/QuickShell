using QuickShell.Classification;

namespace QuickShell.Abstractions.Classification;

internal interface IProjectClassifier
{
    string Name { get; }

    int Priority { get; }

    void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder);
}
