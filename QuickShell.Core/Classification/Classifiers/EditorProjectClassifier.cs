using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class EditorProjectClassifier : IProjectClassifier
{
    public string Name => "editors";

    public int Priority => 58;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasVsCodeDirectory
            && !layout.HasDevContainerDirectory
            && !layout.HasDevContainerJson
            && !layout.HasCodeWorkspace)
        {
            return;
        }

        builder.TryClassifyEditors();
    }
}
