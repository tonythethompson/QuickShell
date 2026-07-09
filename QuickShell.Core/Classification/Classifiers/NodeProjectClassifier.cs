using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class NodeProjectClassifier : IProjectClassifier
{
    public string Name => "node";

    public int Priority => 100;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasPackageJson)
        {
            return;
        }

        builder.TryClassifyNode();
    }
}
