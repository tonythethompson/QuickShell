using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ProjectAnalysisServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;

    public ProjectAnalysisServiceTests()
    {
        _root = Path.Join(Path.GetTempPath(), "quickshell-analysis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _services = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
    }

    [Fact]
    public void Classify_matches_legacy_project_classifier_for_node_layout()
    {
        File.WriteAllText(
            Path.Join(_root, "package.json"),
            """
            {
              "name": "demo",
              "scripts": { "dev": "vite" }
            }
            """);

        var service = _services.GetRequiredService<IProjectAnalysisService>();
        var viaService = service.Classify(_root);
        var viaLegacy = ProjectClassifier.Classify(_root);

        Assert.Equal(viaLegacy.Stacks, viaService.Stacks);
        Assert.Equal(viaLegacy.Labels, viaService.Labels);
        Assert.Equal(viaLegacy.NodeScripts, viaService.NodeScripts);
    }

    [Fact]
    public void Classify_matches_legacy_project_classifier_for_dotnet_layout()
    {
        File.WriteAllText(
            Path.Join(_root, "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var service = _services.GetRequiredService<IProjectAnalysisService>();
        var viaService = service.Classify(_root);
        var viaLegacy = ProjectClassifier.Classify(_root);

        Assert.Equal(viaLegacy.Stacks, viaService.Stacks);
        Assert.Contains("App.csproj", viaService.RunnableDotNetProjects);
        Assert.Equal(viaLegacy.RunnableDotNetProjects, viaService.RunnableDotNetProjects);
    }

    [Fact]
    public void Layout_analyzer_detects_docker_and_taskfile_signals()
    {
        File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}");
        File.WriteAllText(Path.Join(_root, "Taskfile.yml"), "version: '3'\n");

        var analyzer = _services.GetRequiredService<IProjectLayoutAnalyzer>();
        var layout = analyzer.Analyze(_root);

        Assert.True(layout.HasDockerCompose);
        Assert.True(layout.HasTaskfile);
    }

    [Fact]
    public void Classify_matches_legacy_project_classifier_for_rust_layout()
    {
        File.WriteAllText(Path.Join(_root, "Cargo.toml"), "[package]\nname = \"demo\"\n");

        var service = _services.GetRequiredService<IProjectAnalysisService>();
        var viaService = service.Classify(_root);
        var viaLegacy = ProjectClassifier.Classify(_root);

        Assert.Equal(viaLegacy.Stacks, viaService.Stacks);
        Assert.True(viaService.Has(ProjectStack.Rust));
    }

    [Fact]
    public void Classify_matches_legacy_project_classifier_for_python_layout()
    {
        File.WriteAllText(Path.Join(_root, "pyproject.toml"), "[project]\nname = \"demo\"\n");

        var service = _services.GetRequiredService<IProjectAnalysisService>();
        var viaService = service.Classify(_root);
        var viaLegacy = ProjectClassifier.Classify(_root);

        Assert.Equal(viaLegacy.Stacks, viaService.Stacks);
        Assert.True(viaService.Has(ProjectStack.Python));
    }

    [Fact]
    public void Classify_matches_legacy_project_classifier_for_go_layout()
    {
        File.WriteAllText(Path.Join(_root, "go.mod"), "module example.com/demo\n\ngo 1.22\n");

        var service = _services.GetRequiredService<IProjectAnalysisService>();
        var viaService = service.Classify(_root);
        var viaLegacy = ProjectClassifier.Classify(_root);

        Assert.Equal(viaLegacy.Stacks, viaService.Stacks);
        Assert.True(viaService.Has(ProjectStack.Go));
    }

    [Fact]
    public void Layout_analyzer_detects_rust_and_companion_signals()
    {
        File.WriteAllText(Path.Join(_root, "Cargo.toml"), "[package]\nname = \"demo\"\n");
        Directory.CreateDirectory(Path.Join(_root, ".vscode"));

        var layout = _services.GetRequiredService<IProjectLayoutAnalyzer>().Analyze(_root);

        Assert.True(layout.HasCargoToml);
        Assert.True(layout.HasVsCodeDirectory);
    }

    [Fact]
    public void TryDetectDevServerUrl_matches_static_detector()
    {
        File.WriteAllText(
            Path.Join(_root, "package.json"),
            """
            {
              "scripts": { "dev": "vite --port 4321" }
            }
            """);

        var service = _services.GetRequiredService<IProjectAnalysisService>();
        Assert.Equal(
            DevServerUrlDetection.TryDetectDevServerUrl(_root),
            service.TryDetectDevServerUrl(_root));
    }

    [Fact]
    public void TrySuggestCompanionApp_matches_static_detector()
    {
        Directory.CreateDirectory(Path.Join(_root, ".vscode"));

        var service = _services.GetRequiredService<IProjectAnalysisService>();
        var viaService = service.TrySuggestCompanionApp(_root);
        var viaStatic = CompanionAppDetection.TrySuggestFromDirectory(_root);

        if (viaStatic is null)
        {
            Assert.Null(viaService);
            return;
        }

        Assert.NotNull(viaService);
        Assert.Equal(viaStatic.PresetId, viaService.PresetId);
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }
}
