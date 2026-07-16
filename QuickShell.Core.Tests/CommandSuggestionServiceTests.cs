using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Services;
using System.Reflection;

namespace QuickShell.Core.Tests;

[Collection(AgentCliCatalogIsolation.Name)]
public sealed class CommandSuggestionServiceTests : IDisposable
{
    private readonly string _root;

    public CommandSuggestionServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-pills-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void GetPills_DockerProject_IncludesLogsAndServices()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        CommandSuggestionService.ClearResultCache();

        var pills = CommandSuggestionService.GetPills(_root, [], ProjectAnalysisAccessor.Instance);

        Assert.Contains(pills, pill => pill.Command == "docker compose logs -f");
        Assert.Contains(pills, pill => pill.Command == "docker compose up");
        Assert.All(pills, pill =>
        {
            Assert.Equal(SuggestionPillPresentation.FormatDisplayTitle(pill.Command), pill.DisplayTitle);
            Assert.DoesNotContain('·', pill.DisplayTitle);
            Assert.Contains('·', pill.Tooltip);
        });
    }

    [Fact]
    public void HasSuggestions_TrueForDockerProject_FalseForEmptyDir()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        CommandSuggestionService.ClearResultCache();

        Assert.True(CommandSuggestionService.HasSuggestions(_root, [], ProjectAnalysisAccessor.Instance));
        Assert.False(CommandSuggestionService.HasSuggestions(Path.Combine(_root, "missing"), [], ProjectAnalysisAccessor.Instance));
        Assert.False(CommandSuggestionService.HasSuggestions(null, [], ProjectAnalysisAccessor.Instance));
    }

    [Fact]
    public void GetPills_MaxCountOne_MatchesFullListHead()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        CommandSuggestionService.ClearResultCache();

        var full = CommandSuggestionService.GetPills(_root, [], ProjectAnalysisAccessor.Instance);
        CommandSuggestionService.ClearResultCache();
        var single = CommandSuggestionService.GetPills(_root, [], ProjectAnalysisAccessor.Instance, maxCount: 1);

        Assert.NotEmpty(full);
        Assert.Single(single);
        Assert.Equal(full[0].Command, single[0].Command);
        Assert.Equal(full[0].Score, single[0].Score);
    }

    [Fact]
    public void GetPills_RepeatedDirectory_UsesCacheWithoutChangingResults()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        CommandSuggestionService.ClearResultCache();

        var first = CommandSuggestionService.GetPills(_root, [], ProjectAnalysisAccessor.Instance);
        var second = CommandSuggestionService.GetPills(_root, [], ProjectAnalysisAccessor.Instance);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first.Select(pill => (pill.Command, pill.Score, pill.TaskType)),
            second.Select(pill => (pill.Command, pill.Score, pill.TaskType)));
    }

    [Fact]
    public void GetPills_FiltersVsCodeVariablesAndTempProjects()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        Directory.CreateDirectory(Path.Combine(_root, ".vscode"));
        File.WriteAllText(
            Path.Combine(_root, ".vscode", "tasks.json"),
            """
            {
              "version": "2.0.0",
              "tasks": [
                {
                  "label": "broken",
                  "type": "shell",
                  "command": "dotnet",
                  "args": ["watch", "run", "--project", "${workspaceFolder}/Trackdub.sln"]
                },
                {
                  "label": "probe",
                  "type": "shell",
                  "command": "dotnet watch --project tmp_serilog_probe.csproj"
                }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(_root, "tmp_serilog_probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var pills = CommandSuggestionService.GetPills(_root, [], ProjectAnalysisAccessor.Instance);

        Assert.DoesNotContain(pills, pill => pill.Command.Contains("${workspaceFolder}", StringComparison.Ordinal));
        Assert.DoesNotContain(pills, pill => pill.Command.Contains("tmp_serilog_probe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetPills_ExcludesUsedCommands()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var pills = CommandSuggestionService.GetPills(_root, ["docker compose logs -f"], ProjectAnalysisAccessor.Instance);

        Assert.DoesNotContain(pills, pill => pill.Command == "docker compose logs -f");
    }

    [Fact]
    public void GetPills_LargePackageJson_DoesNotThrowAndCaps()
    {
        var scripts = new Dictionary<string, string>();
        for (var i = 0; i < 100; i++)
        {
            scripts[$"script{i}"] = "echo test";
        }

        var json = System.Text.Json.JsonSerializer.Serialize(new { scripts });
        File.WriteAllText(Path.Combine(_root, "package.json"), json);

        var pills = CommandSuggestionService.GetPills(_root, [], ProjectAnalysisAccessor.Instance);

        Assert.True(pills.Count <= CommandSuggestionService.MaxPills);
    }

    [Fact]
    public void ApplyPill_FillThenAppend_UsesThreeRowsBeforeAddingFourth()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        var rows = new List<LaunchRowDraft>
        {
            new() { LaunchTarget = "default", IsEditorPlaceholder = true },
            new() { LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId, IsEditorPlaceholder = true },
            new() { LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId, IsEditorPlaceholder = true },
        };

        var pills = CommandSuggestionService.GetPills(_root, [], ProjectAnalysisAccessor.Instance);
        Assert.True(pills.Count >= 2);

        Assert.False(CommandSuggestionService.ApplyPill(rows, pills[0], "default"));
        Assert.Equal(3, rows.Count);
        Assert.False(CommandSuggestionService.ApplyPill(rows, pills[1], "default"));
        Assert.Equal(3, rows.Count);
        var thirdPill = pills.Count > 2 ? pills[2] : pills[0];
        Assert.False(CommandSuggestionService.ApplyPill(rows, thirdPill, "default"));
        Assert.Equal(3, rows.Count);
        Assert.True(CommandSuggestionService.ApplyPill(rows, pills[0], "default"));
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void TryFindPill_MatchesCommandAndTaskType()
    {
        var pills = new[]
        {
            new CommandSuggestionPill("npm run dev", TaskTypeCatalog.Frontend, "Frontend", "npm run dev", "Frontend · npm run dev", 10, "node-script"),
            new CommandSuggestionPill("npm run dev", TaskTypeCatalog.Api, "API", "npm run dev", "API · npm run dev", 8, "node-script"),
        };

        var pill = CommandSuggestionService.TryFindPill(pills, "npm run dev", TaskTypeCatalog.Api);

        Assert.NotNull(pill);
        Assert.Equal(TaskTypeCatalog.Api, pill.TaskType);
    }

    [Fact]
    public void RankTop_EqualScoreAndTitle_OrdersByCommand()
    {
        var alpha = new CommandSuggestionPill("alpha", TaskTypeCatalog.Agent, "Agent", "Agent", "Agent · alpha", 94, "test");
        var beta = new CommandSuggestionPill("beta", TaskTypeCatalog.Agent, "Agent", "Agent", "Agent · beta", 94, "test");
        var method = typeof(CommandSuggestionService).GetMethod("RankTop", BindingFlags.NonPublic | BindingFlags.Static);

        var ranked = Assert.IsType<List<CommandSuggestionPill>>(method!.Invoke(
            null,
            new object?[] { new[] { alpha, beta }, 2 }));

        Assert.Equal(["alpha", "beta"], ranked.Select(pill => pill.Command));
    }

    public void Dispose()
    {
        CommandSuggestionService.ClearResultCache();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
