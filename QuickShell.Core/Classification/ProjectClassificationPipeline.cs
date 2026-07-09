using QuickShell.Abstractions.Classification;
using QuickShell.Classification.Classifiers;
using QuickShell.Services;

namespace QuickShell.Classification;

internal static class ProjectClassificationPipeline
{
    internal static IReadOnlyList<IProjectClassifier> CreateDefaultClassifiers() =>
    [
        new NodeProjectClassifier(),
        new DotNetProjectClassifier(),
        new DockerComposeProjectClassifier(),
        new TaskRunnerProjectClassifier(),
        new MiscellaneousProjectClassifier(),
    ];

    internal static ProjectClassification Classify(
        string directory,
        IEnumerable<IProjectClassifier> classifiers,
        IProjectLayoutAnalyzer layoutAnalyzer)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return ProjectClassification.Empty;
        }

        var layout = layoutAnalyzer.Analyze(directory);
        var builder = new ProjectClassificationBuilder(directory);
        foreach (var classifier in classifiers.OrderByDescending(classifier => classifier.Priority))
        {
            ProjectClassificationContributor.Try(classifier, directory, layout, builder);
        }

        return builder.Build();
    }
}
