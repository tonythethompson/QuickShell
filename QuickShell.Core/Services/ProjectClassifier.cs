using QuickShell.Classification;
using QuickShell.Services;

namespace QuickShell.Services;

internal static class ProjectClassifier
{
    public static ProjectClassification Classify(string directory) =>
        ProjectClassificationPipeline.Classify(
            directory,
            ProjectClassificationPipeline.CreateDefaultClassifiers(),
            ProjectLayoutAnalyzer.Default);
}
