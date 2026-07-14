using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class GoProjectClassifier : IProjectClassifier
{
    public string Name => "go";

    public int Priority => 57;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasGoMod)
        {
            return;
        }

        builder.TryClassifyGo();
    }
}
