using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(ProjectAnalysisStaticStateIsolation.Name)]
public sealed class TaskTypeCommandSuggestionTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;

    public TaskTypeCommandSuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-task-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
    }

    [Fact]
    public void TrySuggest_Logs_PrefersDockerComposeLogs()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var suggested = _projectAnalysis.TrySuggestTaskCommand(_root, TaskTypeCatalog.Logs, TaskTypePickContext.Empty);

        Assert.Equal("docker compose logs -f", suggested);
    }

    [Fact]
    public void TrySuggest_Frontend_PrefersPackageDevScript()
    {
        File.WriteAllText(
            Path.Combine(_root, "package.json"),
            """
            {
              "scripts": {
                "dev": "vite"
              },
              "devDependencies": {
                "vite": "1.0.0"
              }
            }
            """);

        var suggested = _projectAnalysis.TrySuggestTaskCommand(_root, TaskTypeCatalog.Frontend, TaskTypePickContext.Empty);

        Assert.Equal("npm run dev", suggested);
    }

    [Fact]
    public void TrySuggest_Api_PrefersDotNetRun()
    {
        File.WriteAllText(
            Path.Combine(_root, "sample.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var suggested = _projectAnalysis.TrySuggestTaskCommand(_root, TaskTypeCatalog.Api, TaskTypePickContext.Empty);

        Assert.StartsWith("dotnet watch", suggested, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySuggest_Services_PrefersDockerComposeUp()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var suggested = _projectAnalysis.TrySuggestTaskCommand(_root, TaskTypeCatalog.Services, TaskTypePickContext.Empty);

        Assert.Equal("docker compose up", suggested);
    }

    [Fact]
    public void HasAvailableTypes_TrueForDockerCompose()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        Assert.True(_projectAnalysis.HasAvailableTaskTypes(_root));
        Assert.Contains(TaskTypeCatalog.Logs, _projectAnalysis.GetAvailableTaskTypes(_root, TaskTypePickContext.Empty));
    }

    [Fact]
    public void IsTaskTypeAvailable_IgnoresDisplayPillCap()
    {
        // Many high-scoring agent/script pills must not hide lower-ranked task types from the picker.
        var scripts = new Dictionary<string, string>();
        for (var i = 0; i < 40; i++)
        {
            scripts[$"dev{i}"] = "vite";
        }

        scripts["logs"] = "docker compose logs -f";
        File.WriteAllText(
            Path.Combine(_root, "package.json"),
            System.Text.Json.JsonSerializer.Serialize(new { scripts }));
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        Assert.True(_projectAnalysis.IsTaskTypeAvailable(_root, TaskTypeCatalog.Logs, TaskTypePickContext.Empty));
        Assert.Equal(
            "docker compose logs -f",
            _projectAnalysis.TrySuggestTaskCommand(_root, TaskTypeCatalog.Logs, TaskTypePickContext.Empty));
    }

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}

[Collection(ProjectAnalysisStaticStateIsolation.Name)]
public sealed class DockerComposeDiscoveryTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;

    public DockerComposeDiscoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
    }

    [Fact]
    public void DiscoverServiceNames_ParsesTopLevelServices()
    {
        File.WriteAllText(
            Path.Combine(_root, "docker-compose.yml"),
            """
            services:
              postgres:
                image: postgres
              api:
                image: node
              web:
                image: nginx
            """);

        var services = DockerComposeDiscovery.DiscoverServiceNames(_root);

        Assert.Equal(["postgres", "api", "web"], services);
    }

    [Fact]
    public void ClassifyService_MapsDatabaseAndAppRoles()
    {
        Assert.Equal(DockerServiceRole.Services, DockerComposeDiscovery.ClassifyService("postgres"));
        Assert.Equal(DockerServiceRole.Api, DockerComposeDiscovery.ClassifyService("api"));
        Assert.Equal(DockerServiceRole.Frontend, DockerComposeDiscovery.ClassifyService("web"));
    }

    [Fact]
    public void TrySuggest_Services_PrefersDatabaseServiceOverWholeStack()
    {
        File.WriteAllText(
            Path.Combine(_root, "docker-compose.yml"),
            """
            services:
              postgres:
                image: postgres
            """);

        var suggested = _projectAnalysis.TrySuggestTaskCommand(_root, TaskTypeCatalog.Services, TaskTypePickContext.Empty);

        Assert.Equal("docker compose up postgres", suggested);
    }

    [Fact]
    public void TrySuggest_Logs_PrefersServiceSpecificLogs()
    {
        File.WriteAllText(
            Path.Combine(_root, "docker-compose.yml"),
            """
            services:
              api:
                image: node
              web:
                image: nginx
            """);

        var suggested = _projectAnalysis.TrySuggestTaskCommand(_root, TaskTypeCatalog.Logs, TaskTypePickContext.Empty);

        Assert.Equal("docker compose logs -f api", suggested);
    }

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
