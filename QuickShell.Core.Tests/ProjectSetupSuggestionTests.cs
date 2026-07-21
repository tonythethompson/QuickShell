using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(GitRepoIndexIsolation.Name)]
public sealed class ProjectSetupSuggestionTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly GitRepoIndex _gitRepoIndex;

    public ProjectSetupSuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-project-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        _gitRepoIndex = new GitRepoIndex(
            _projectAnalysis,
            _provider.GetRequiredService<IQuickShellLifetime>(),
            new SyncExtensionThreadScheduler(),
            discoverOverride: roots => GitRepoDiscovery.Discover(_projectAnalysis, roots, includeDefaultSearchRoots: false));
    }

    [Fact]
    public void Classify_DetectsCommonProjectMarkers()
    {
        Write("package.json", """
        {
          "workspaces": ["apps/*"],
          "devDependencies": {
            "turbo": "^2.0.0"
          },
          "scripts": {
            "dev": "turbo dev"
          }
        }
        """);
        Write("docker-compose.yml", "services: {}\n");
        Write(".devcontainer/devcontainer.json", "{}");
        Write(".vscode/tasks.json", """
        {
          "tasks": [
            {
              "label": "lint",
              "type": "shell",
              "command": "npm",
              "args": ["run", "lint"]
            }
          ]
        }
        """);

        var classification = _projectAnalysis.Classify(_root);

        Assert.True(classification.Has(ProjectStack.Node));
        Assert.True(classification.Has(ProjectStack.Docker));
        Assert.True(classification.Has(ProjectStack.DevContainer));
        Assert.True(classification.Has(ProjectStack.VsCodeWorkspace));
        Assert.True(classification.Has(ProjectStack.Monorepo));
        Assert.True(classification.Has(ProjectStack.Turbo));
        Assert.Contains("Node", classification.Labels);
        Assert.Contains("monorepo", classification.Labels);
    }

    [Fact]
    public void Build_NodeSuggestionsUseDetectedPackageManagerAndPreferredScripts()
    {
        Write("package.json", """
        {
          "scripts": {
            "dev": "vite",
            "test": "vitest",
            "build": "vite build",
            "lint": "eslint ."
          }
        }
        """);
        Write("pnpm-lock.yaml", string.Empty);

        var suggestions = WorkspaceSetupSuggestion.Build(_root, _projectAnalysis);

        Assert.Collection(
            suggestions.Take(3),
            task =>
            {
                Assert.Equal("Dev", task.Label);
                Assert.Equal("pnpm dev", task.Command);
            },
            task =>
            {
                Assert.Equal("Tests", task.Label);
                Assert.Equal("pnpm test", task.Command);
            },
            task =>
            {
                Assert.Equal("Build", task.Label);
                Assert.Equal("pnpm build", task.Command);
            });
        Assert.DoesNotContain(suggestions, task => task.Command.Contains("lint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyDirectoryHints_SeedsMultipleLaunchesForDiscoveredWorkspace()
    {
        Write("package.json", """
        {
          "scripts": {
            "dev": "vite",
            "test": "vitest",
            "build": "vite build"
          }
        }
        """);

        var seed = WorkspaceSeedFactory.ApplyDirectoryHints(new TerminalShortcut
        {
            Name = "sample",
            Directory = _root,
        }, _projectAnalysis);

        Assert.Equal("npm run dev", seed.Command);
        Assert.Collection(
            seed.Launches,
            launch => Assert.Equal("npm run dev", launch.Command),
            launch => Assert.Equal("npm run test", launch.Command),
            launch => Assert.Equal("npm run build", launch.Command));
    }

    [Fact]
    public void Build_DotNetRunOnlyWhenExactlyOneRunnableProjectExists()
    {
        Write("App.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
        </Project>
        """);
        Write("App.Tests.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
        </Project>
        """);

        var suggestions = WorkspaceSetupSuggestion.Build(_root, _projectAnalysis);

        Assert.Contains(suggestions, task => task.Command == "dotnet build");
        Assert.Contains(suggestions, task => task.Command == "dotnet test");
        Assert.Contains(suggestions, task => task.Command == "dotnet run --project App.csproj");

        Write("Worker.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
        </Project>
        """);

        suggestions = WorkspaceSetupSuggestion.Build(_root, _projectAnalysis);

        Assert.DoesNotContain(suggestions, task => task.Command.StartsWith("dotnet run", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_FileMarkerScansAreLineLocalAndDoNotBufferWholeFiles()
    {
        Write("App.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
        </Project>
        """);
        Write("SplitMarker.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Ex
            e</OutputType>
          </PropertyGroup>
        </Project>
        """);
        Write("mix.exs", """
        defmodule Sample.MixProject do
          # phoenix
        end
        """);

        var suggestions = WorkspaceSetupSuggestion.Build(_root, _projectAnalysis);

        Assert.Contains(suggestions, task => task.Command == "dotnet run --project App.csproj");
        Assert.Contains(suggestions, task => task.Command == "mix phx.server");
        Assert.DoesNotContain(suggestions, task => task.Command == "dotnet run --project SplitMarker.csproj");

        Assert.DoesNotContain("File.ReadAllText", ReadCoreSource("Classification", "ProjectClassificationBuilder.cs"));
        Assert.DoesNotContain("File.ReadAllText", ReadCoreSource("Services", "WorkspaceSetupSuggestion.cs"));
    }

    [Fact]
    public void Build_GenericRunnerSuggestionsUseOnlyKnownTargets()
    {
        Write("Makefile", """
        dev:
        	npm run dev
        test:
        	npm test
        """);
        Write("justfile", """
        build:
            dotnet build
        lint:
            dotnet format
        """);
        Write("Taskfile.yml", """
        version: '3'
        tasks:
          dev:
            cmds:
              - go run .
        """);
        Write("docker-compose.yml", "services: {}\n");

        var suggestions = WorkspaceSetupSuggestion.Build(_root, _projectAnalysis);

        Assert.Contains(suggestions, task => task.Command == "make");
        Assert.Contains(suggestions, task => task.Command == "make dev");
        Assert.Contains(suggestions, task => task.Command == "make test");
        Assert.Contains(suggestions, task => task.Command == "just build");
        Assert.Contains(suggestions, task => task.Command == "task dev");
        Assert.Contains(suggestions, task => task.Command == "docker compose up");
        Assert.Contains(suggestions, task => task.Command == "docker compose logs -f");
        Assert.DoesNotContain(suggestions, task => task.Command.Contains("lint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_ImportsRunnableVsCodeTasksAndSkipsCompounds()
    {
        Write(".vscode/tasks.json", """
        {
          // JSONC comments are accepted by VS Code and should not break parsing.
          "tasks": [
            {
              "label": "format",
              "type": "shell",
              "command": "dotnet",
              "args": ["format", "Sample.sln"]
            },
            {
              "label": "compound",
              "dependsOn": ["format"]
            }
          ]
        }
        """);

        var suggestions = WorkspaceSetupSuggestion.Build(_root, _projectAnalysis);

        Assert.Contains(suggestions, task => task.Label == "VS Code: format" && task.Command == "dotnet format Sample.sln");
        Assert.DoesNotContain(suggestions, task => task.Label.Contains("compound", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Discovery_IncludesClassificationInCandidateSearchAndSubtitle()
    {
        var repoPath = Path.Combine(_root, "api");
        Directory.CreateDirectory(Path.Combine(repoPath, ".git"));
        File.WriteAllText(Path.Combine(repoPath, "go.mod"), "module example.com/api\n");
        _gitRepoIndex.Invalidate();

        var discovered = GitRepoDiscovery.Discover(_projectAnalysis, [_root], includeDefaultSearchRoots: false);

        var candidate = Assert.Single(discovered);
        Assert.True(candidate.Classification.Has(ProjectStack.Go));
        Assert.Contains("Go", DiscoverGitRepoListItems.BuildSubtitleForNew(candidate), StringComparison.Ordinal);

        _ = _gitRepoIndex.Search("go", [_root], savedDirectories: null);
        _gitRepoIndex.WaitForPopulationForTests(_root, TimeSpan.FromSeconds(10));

        var matches = _gitRepoIndex.Search("go", [_root], savedDirectories: null);

        Assert.Single(matches);
        Assert.Equal("api", matches[0].Name);
    }

    private void Write(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, contents);
    }

    private static string ReadCoreSource(params string[] relativePath)
    {
        var repositoryRoot = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../../"));
        var sourcePath = Path.Join(repositoryRoot, "QuickShell.Core");
        foreach (var segment in relativePath)
        {
            sourcePath = Path.Join(sourcePath, segment);
        }

        return File.ReadAllText(sourcePath);
    }

    public void Dispose()
    {
        _gitRepoIndex.Dispose();
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
