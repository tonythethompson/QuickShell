using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class RustProjectClassifier : IProjectClassifier
{
    public string Name => "rust";

    public int Priority => 60;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasCargoToml)
        {
            return;
        }

        builder.TryClassifyRust();
    }
}
