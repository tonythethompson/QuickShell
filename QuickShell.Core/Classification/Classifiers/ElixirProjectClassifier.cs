using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class ElixirProjectClassifier : IProjectClassifier
{
    public string Name => "elixir";

    public int Priority => 52;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasMixExs)
        {
            return;
        }

        builder.TryClassifyElixir();
    }
}
