using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class DenoProjectClassifier : IProjectClassifier
{
    public string Name => "deno";

    public int Priority => 55;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasDenoJson)
        {
            return;
        }

        builder.TryClassifyDeno();
    }
}
