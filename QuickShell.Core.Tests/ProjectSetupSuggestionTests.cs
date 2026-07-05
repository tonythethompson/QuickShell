using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(GitRepoIndexIsolation.Name)]
public sealed class ProjectSetupSuggestionTests : IDisposable
{
    private readonly string _root;

    public ProjectSetupSuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-project-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

        var classification = ProjectClassifier.Classify(_root);

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

        var suggestions = WorkspaceSetupSuggestion.Build(_root);

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
        });

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

        var suggestions = WorkspaceSetupSuggestion.Build(_root);

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

        suggestions = WorkspaceSetupSuggestion.Build(_root);

        Assert.DoesNotContain(suggestions, task => task.Command.StartsWith("dotnet run", StringComparison.Ordinal));
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

        var suggestions = WorkspaceSetupSuggestion.Build(_root);

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

        var suggestions = WorkspaceSetupSuggestion.Build(_root);

        Assert.Contains(suggestions, task => task.Label == "VS Code: format" && task.Command == "dotnet format Sample.sln");
        Assert.DoesNotContain(suggestions, task => task.Label.Contains("compound", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Discovery_IncludesClassificationInCandidateSearchAndSubtitle()
    {
        var repoPath = Path.Combine(_root, "api");
        Directory.CreateDirectory(Path.Combine(repoPath, ".git"));
        File.WriteAllText(Path.Combine(repoPath, "go.mod"), "module example.com/api\n");
        GitRepoIndex.Invalidate();

        var discovered = GitRepoDiscovery.Discover([_root]);

        var candidate = Assert.Single(discovered);
        Assert.True(candidate.Classification.Has(ProjectStack.Go));
        Assert.Contains("Go", DiscoverGitRepoListItems.BuildSubtitleForNew(candidate), StringComparison.Ordinal);

        var matches = GitRepoIndex.Search("go", [_root], savedDirectories: null);

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

    public void Dispose()
    {
        GitRepoIndex.Invalidate();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
