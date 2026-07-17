using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

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

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Services, TaskTypePickContext.Empty, _projectAnalysis);

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

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Logs, TaskTypePickContext.Empty, _projectAnalysis);

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

[Collection(ProjectAnalysisStaticStateIsolation.Name)]
public sealed class TaskTypeCandidatePickTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;

    public TaskTypeCandidatePickTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-pick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
    }

    [Fact]
    public void TrySuggest_SkipsAlreadyUsedCommands()
    {
        File.WriteAllText(
            Path.Combine(_root, "package.json"),
            """
            {
              "scripts": {
                "dev": "vite",
                "dev:web": "vite --host",
                "test": "vitest"
              },
              "devDependencies": {
                "vite": "1.0.0"
              }
            }
            """);

        var first = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Frontend, TaskTypePickContext.Empty, _projectAnalysis);
        Assert.Equal("npm run dev", first);

        var second = TaskTypeCommandSuggestion.TrySuggest(
            _root,
            TaskTypeCatalog.Frontend,
            TaskTypePickContext.FromCommands([first]),
            _projectAnalysis);
        Assert.Equal("npm run dev:web", second);
    }

    [Fact]
    public void TrySuggest_Test_PrefersDotNetTest()
    {
        File.WriteAllText(
            Path.Combine(_root, "sample.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk" />
            """);

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Test, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Equal("dotnet test", suggested);
    }

    [Fact]
    public void TrySuggest_Build_PrefersDotNetBuild()
    {
        File.WriteAllText(
            Path.Combine(_root, "sample.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk" />
            """);

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Build, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Equal("dotnet build", suggested);
    }

    [Fact]
    public void Normalize_LegacyDatabase_MapsToServices()
    {
        Assert.Equal(TaskTypeCatalog.Services, TaskTypeCatalog.Normalize("database"));
        Assert.Equal("Services", TaskTypeCatalog.GetTitle("database"));
    }

    [Fact]
    public void TrySuggest_Api_PrefersMonorepoApiFilter()
    {
        File.WriteAllText(
            Path.Combine(_root, "package.json"),
            """
            {
              "scripts": {
                "dev": "turbo run dev --filter=web",
                "dev:api": "turbo run dev --filter=api"
              },
              "devDependencies": {
                "turbo": "2.0.0"
              }
            }
            """);

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Api, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Equal("npm run dev:api", suggested);
    }

    [Fact]
    public void TrySuggest_Test_PrefersVitestScriptName()
    {
        File.WriteAllText(
            Path.Combine(_root, "package.json"),
            """
            {
              "scripts": {
                "vitest": "vitest",
                "lint": "eslint ."
              }
            }
            """);

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Test, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Equal("npm run vitest", suggested);
    }

    [Fact]
    public void TrySuggest_Api_PrefersPythonRunserver()
    {
        File.WriteAllText(Path.Combine(_root, "manage.py"), "print('ok')");
        File.WriteAllText(Path.Combine(_root, "requirements.txt"), "django\n");

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Api, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Equal("python manage.py runserver", suggested);
    }

    [Fact]
    public void TrySuggest_Api_DoesNotPreferForemanInNodeFrontendRepo()
    {
        File.WriteAllText(
            Path.Combine(_root, "package.json"),
            """
            {
              "scripts": {
                "dev": "vite"
              },
              "devDependencies": {
                "vite": "1.0.0",
                "foreman": "1.0.0"
              }
            }
            """);
        File.WriteAllText(Path.Combine(_root, "Procfile"), "web: npm run dev");

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Api, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Null(suggested);
    }

    [Fact]
    public void TrySuggest_Frontend_PrefersStorybookScript()
    {
        File.WriteAllText(
            Path.Combine(_root, "package.json"),
            """
            {
              "scripts": {
                "storybook": "storybook dev -p 6006"
              }
            }
            """);

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Frontend, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Equal("npm run storybook", suggested);
    }

    [Fact]
    public void TrySuggest_Api_PrefersPhoenixServer()
    {
        File.WriteAllText(
            Path.Combine(_root, "mix.exs"),
            """
            defmodule MyApp.MixProject do
              use Mix.Project
              def project do
                %{deps: [{:phoenix, "~> 1.7"}]}
              end
            end
            """);

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Api, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Equal("mix phx.server", suggested);
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
