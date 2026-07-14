using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class JavaProjectClassifier : IProjectClassifier
{
    public string Name => "java";

    public int Priority => 56;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasPomXml && !layout.HasGradleBuild)
        {
            return;
        }

        builder.TryClassifyJava();
    }
}
