using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class ProcfileProjectClassifier : IProjectClassifier
{
    public string Name => "procfile";

    public int Priority => 54;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasProcfile)
        {
            return;
        }

        builder.TryClassifyProcfile();
    }
}
