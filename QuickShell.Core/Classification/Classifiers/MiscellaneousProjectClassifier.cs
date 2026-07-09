using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class MiscellaneousProjectClassifier : IProjectClassifier
{
    public string Name => "miscellaneous";

    public int Priority => 10;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        builder.TryClassifyRust();
        builder.TryClassifyPython();
        builder.TryClassifyEditors();
        builder.TryClassifyGo();
        builder.TryClassifyJava();
        builder.TryClassifyDeno();
        builder.TryClassifyProcfile();
        builder.TryClassifyRuby();
        builder.TryClassifyElixir();
    }
}
