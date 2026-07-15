using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProjectAnalysisStaticStateIsolation
{
    public const string Name = "ProjectAnalysisStaticState";
}

[Collection(ProjectAnalysisStaticStateIsolation.Name)]
public sealed class ProjectAnalysisAccessorTests : IDisposable
{
    private readonly string _root;
    private readonly IProjectAnalysisService _original;

    public ProjectAnalysisAccessorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-accessor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _original = ProjectAnalysisAccessor.Instance;
    }

    [Fact]
    public void WorkspaceSeedFactory_uses_accessor_for_dev_server_detection()
    {
        File.WriteAllText(
            Path.Combine(_root, "package.json"),
            """
            {
              "scripts": { "dev": "vite --port 4321" }
            }
            """);

        var seed = WorkspaceSeedFactory.ApplyDirectoryHints(new TerminalShortcut
        {
            Name = "demo",
            Directory = _root,
        });

        Assert.Equal("http://localhost:4321", seed.DevServerUrl);
    }

    [Fact]
    public void WorkspaceSeedFactory_uses_accessor_for_classification()
    {
        var fake = new FakeProjectAnalysisService
        {
            ClassifyResult = new ProjectClassification
            {
                Stacks = ProjectStack.Rust,
                Labels = ["Rust"],
            },
        };

        ProjectAnalysisAccessor.Instance = fake;

        var seed = WorkspaceSeedFactory.FromGitRepo(new GitRepoCandidate
        {
            Directory = _root,
            Name = "demo",
            Classification = ProjectClassification.Empty,
        });

        Assert.Equal(ProjectStack.Rust, fake.ClassifyResult.Stacks);
    }

    public void Dispose()
    {
        ProjectAnalysisAccessor.Instance = _original;

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private sealed class FakeProjectAnalysisService : IProjectAnalysisService
    {
        public ProjectClassification ClassifyResult { get; init; } = ProjectClassification.Empty;

        public ProjectClassification Classify(string directory) => ClassifyResult;

        public bool HasAvailableTaskTypes(string? directory) => false;

        public IReadOnlyList<string> GetAvailableTaskTypes(string? directory, TaskTypePickContext pickContext) => [];

        public bool IsTaskTypeAvailable(string? directory, string? taskType, TaskTypePickContext pickContext) => false;

        public string? TrySuggestTaskCommand(string? directory, string? taskType, TaskTypePickContext pickContext) => null;

        public string GetTaskTypeChoiceTooltip(string? directory, string? taskType, TaskTypePickContext pickContext) => string.Empty;

        public CompanionAppSuggestion? TrySuggestCompanionApp(string directory) => null;

        public string? TryDetectDevServerUrl(string directory) => null;

        public string? TryInferTaskType(string directory) => null;

        public string? TryDetectDevLaunchCommand(string directory) => null;

        public string FormatPackageScriptCommand(string directory, string scriptName) => $"npm run {scriptName}";
    }
}
