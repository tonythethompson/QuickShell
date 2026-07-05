using QuickShell.Services;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class TaskTypeCommandSuggestionTests : IDisposable
{
    private readonly string _root;

    public TaskTypeCommandSuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-task-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TrySuggest_Logs_PrefersDockerComposeLogs()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Logs);

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

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Frontend);

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

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Api);

        Assert.StartsWith("dotnet watch", suggested, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySuggest_Database_PrefersDockerComposeUp()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var suggested = TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.Services);

        Assert.Equal("docker compose up", suggested);
    }

    [Fact]
    public void TrySuggest_None_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        Assert.Null(TaskTypeCommandSuggestion.TrySuggest(_root, TaskTypeCatalog.None));
    }

    [Fact]
    public void GetChoiceTooltip_WithSuggestion_IncludesSuggestedCommand()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var tooltip = TaskTypeCommandSuggestion.GetChoiceTooltip(_root, TaskTypeCatalog.Logs);

        Assert.Contains("docker compose logs -f", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFormChoicesJson_IncludesTooltipPerChoice()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        using var document = JsonDocument.Parse(TaskTypeCatalog.BuildPickerChoicesJson(_root));

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

        Assert.True(TaskTypeCommandSuggestion.HasAvailableTypes(_root));
        Assert.Contains(TaskTypeCatalog.Logs, TaskTypeCommandSuggestion.GetAvailableTaskTypes(_root));
    }

    [Fact]
    public void HasAvailableTypes_EmptyDirectory_IsFalse()
    {
        Assert.False(TaskTypeCommandSuggestion.HasAvailableTypes(_root));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
