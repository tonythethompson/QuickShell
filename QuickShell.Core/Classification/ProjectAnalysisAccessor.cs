using QuickShell.Abstractions.Classification;
using QuickShell.Classification.Detectors;

namespace QuickShell.Classification;

internal static class ProjectAnalysisAccessor
{
    private static readonly Lazy<IProjectAnalysisService> DefaultLazy = new(CreateDefault);

    private static IProjectAnalysisService _instance = DefaultLazy.Value;

    internal static IProjectAnalysisService Instance
    {
        get => _instance;
        set => _instance = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal static void Reset() => _instance = DefaultLazy.Value;

    private static ProjectAnalysisService CreateDefault() =>
        new ProjectAnalysisService(
            ProjectClassificationPipeline.CreateDefaultClassifiers(),
            ProjectLayoutAnalyzer.Default,
            new CompanionAppDetector(),
            new DevServerDetector(),
            ProjectClassificationPipeline.CreateDefaultTaskSuggestionProviders());
}
