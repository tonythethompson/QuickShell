using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Services;
using System.Text.Json;

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

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Logs, TaskTypePickContext.Empty, _projectAnalysis);

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

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Frontend, TaskTypePickContext.Empty, _projectAnalysis);

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

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Api, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.StartsWith("dotnet watch", suggested, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySuggest_Database_PrefersDockerComposeUp()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Services, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Equal("docker compose up", suggested);
    }

    [Fact]
    public void TrySuggest_None_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        Assert.Null(TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.None, TaskTypePickContext.Empty, _projectAnalysis));
    }

    [Fact]
    public void GetChoiceTooltip_WithSuggestion_IncludesSuggestedCommand()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var tooltip = TaskTypeCommandSuggestion.GetChoiceTooltip(_root, TaskTypeCatalog.Logs, TaskTypePickContext.Empty, _projectAnalysis);

        Assert.Contains("docker compose logs -f", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFormChoicesJson_IncludesTooltipPerChoice()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        using var document = JsonDocument.Parse(TaskTypeCatalog.BuildPickerChoicesJson(_projectAnalysis, _root));

        foreach (var choice in document.RootElement.EnumerateArray())
        {
            if (string.Equals(
                    choice.GetProperty("value").GetString(),
                    TaskTypeCatalog.None,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.True(choice.TryGetProperty("tooltip", out var tooltip));
            Assert.False(string.IsNullOrWhiteSpace(tooltip.GetString()));
        }
    }

    [Fact]
    public void HasAvailableTypes_DockerProject_IsTrue()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        Assert.True(TaskTypeCommandSuggestion.HasAvailableTypes(_root, _projectAnalysis));
        Assert.Contains(TaskTypeCatalog.Logs, TaskTypeCommandSuggestion.GetAvailableTaskTypes(_root, TaskTypePickContext.Empty, _projectAnalysis));
    }

    [Fact]
    public void HasAvailableTypes_EmptyDirectory_IsFalse()
    {
        Assert.False(TaskTypeCommandSuggestion.HasAvailableTypes(_root, _projectAnalysis));
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
