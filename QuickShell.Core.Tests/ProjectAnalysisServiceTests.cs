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
        _root = Path.Combine(Path.GetTempPath(), "quickshell-analysis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _services = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
    }

    [Fact]
    public void Classify_matches_legacy_project_classifier_for_node_layout()
    {
        File.WriteAllText(
            Path.Combine(_root, "package.json"),
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
            Path.Combine(_root, "App.csproj"),
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
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        File.WriteAllText(Path.Combine(_root, "Taskfile.yml"), "version: '3'\n");

        var analyzer = _services.GetRequiredService<IProjectLayoutAnalyzer>();
        var layout = analyzer.Analyze(_root);

        Assert.True(layout.HasDockerCompose);
        Assert.True(layout.HasTaskfile);
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
        catch
        {
            // Best effort cleanup.
        }
    }
}
