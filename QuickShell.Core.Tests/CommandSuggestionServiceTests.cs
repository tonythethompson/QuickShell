using QuickShell.Services;

namespace QuickShell.Core.Tests;

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

        var pills = CommandSuggestionService.GetPills(_root, []);

        Assert.Contains(pills, pill => pill.Command == "docker compose logs -f");
        Assert.Contains(pills, pill => pill.Command == "docker compose up");
        Assert.All(pills, pill => Assert.Contains('·', pill.DisplayTitle));
    }

    [Fact]
    public void GetPills_ExcludesUsedCommands()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var pills = CommandSuggestionService.GetPills(_root, ["docker compose logs -f"]);

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

        var pills = CommandSuggestionService.GetPills(_root, []);

        Assert.True(pills.Count <= CommandSuggestionService.MaxPills);
    }

    [Fact]
    public void ApplyPill_FillThenAppend_UsesThreeRowsBeforeAddingFourth()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        var rows = new List<LaunchRowDraft>
        {
            new() { LaunchTarget = "default" },
            new() { LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
            new() { LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId },
        };

        var pills = CommandSuggestionService.GetPills(_root, []);
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
            new CommandSuggestionPill("npm run dev", TaskTypeCatalog.Frontend, "Frontend", "Frontend · npm run dev", "npm run dev", 10, "node-script"),
            new CommandSuggestionPill("npm run dev", TaskTypeCatalog.Api, "API", "API · npm run dev", "npm run dev", 8, "node-script"),
        };

        var pill = CommandSuggestionService.TryFindPill(pills, "npm run dev", TaskTypeCatalog.Api);

        Assert.NotNull(pill);
        Assert.Equal(TaskTypeCatalog.Api, pill.TaskType);
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
