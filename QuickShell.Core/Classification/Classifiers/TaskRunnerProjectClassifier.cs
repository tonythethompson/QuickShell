using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class TaskRunnerProjectClassifier : IProjectClassifier
{
    public string Name => "task-runners";

    public int Priority => 70;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasMakefile && !layout.HasJustfile && !layout.HasTaskfile)
        {
            return;
        }

        builder.TryClassifyTaskRunners();
    }
}
