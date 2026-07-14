using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class RubyProjectClassifier : IProjectClassifier
{
    public string Name => "ruby";

    public int Priority => 53;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasGemfile)
        {
            return;
        }

        builder.TryClassifyRuby();
    }
}
