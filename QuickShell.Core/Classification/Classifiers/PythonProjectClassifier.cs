using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class PythonProjectClassifier : IProjectClassifier
{
    public string Name => "python";

    public int Priority => 59;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasPyprojectToml && !layout.HasRequirementsTxt && !layout.HasSetupPy)
        {
            return;
        }

        builder.TryClassifyPython();
    }
}
