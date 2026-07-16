using QuickShell.Services;

namespace QuickShell.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AgentCliCatalogIsolation
{
    public const string Name = "AgentCliCatalog";
}

[Collection(AgentCliCatalogIsolation.Name)]
public sealed class AgentCliSuggestionTests : IDisposable
{
    private readonly string _root;
    private readonly Func<string, bool>? _previousOverride;

    public AgentCliSuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-agents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _previousOverride = AgentCliCatalog.IsCommandOnPathOverride;
        AgentCliCatalog.IsCommandOnPathOverride = _ => false;
    }

    [Fact]
    public void BuildPills_PathDetected_IncludesAgentEvenWithoutProjectStack()
    {
        AgentCliCatalog.IsCommandOnPathOverride = name =>
            name.Equals("claude", StringComparison.OrdinalIgnoreCase);

        var pills = AgentCliSuggestion.BuildPills(_root, TaskTypePickContext.Empty);

        var claude = Assert.Single(pills);
        Assert.Equal("claude", claude.Command);
        Assert.Equal(TaskTypeCatalog.Agent, claude.TaskType);
        Assert.Equal("agent-path", claude.Source);
        Assert.Equal(AgentCliCatalog.PathDetectedScore, claude.Score);
        Assert.Equal("claude", claude.DisplayTitle);
        Assert.Contains("Claude Code", claude.Tooltip, StringComparison.Ordinal);
        Assert.Contains("Agent", claude.Tooltip, StringComparison.Ordinal);
        Assert.StartsWith("Agent · Claude Code", claude.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPills_CopilotUsesCopilotCommandNotGh()
    {
        AgentCliCatalog.IsCommandOnPathOverride = name =>
            name.Equals("copilot", StringComparison.OrdinalIgnoreCase)
            || name.Equals("gh", StringComparison.OrdinalIgnoreCase);

        var pills = AgentCliSuggestion.BuildPills(_root, TaskTypePickContext.Empty);

        Assert.Contains(pills, pill => pill.Command == "copilot");
        Assert.DoesNotContain(pills, pill => pill.Command.Contains("gh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPills_CursorAgentExecutable_UsesAgentCommand()
    {
        AgentCliCatalog.IsCommandOnPathOverride = name =>
            name.Equals("agent", StringComparison.OrdinalIgnoreCase);

        var cursor = Assert.Single(AgentCliSuggestion.BuildPills(_root, TaskTypePickContext.Empty));

        Assert.Equal("agent", cursor.Command);
        Assert.Contains("Cursor Agent", cursor.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPills_AlternatePathExecutable_UsesDetectedCommand()
    {
        AgentCliCatalog.IsCommandOnPathOverride = name =>
            name.Equals("kiro", StringComparison.OrdinalIgnoreCase);

        var kiro = Assert.Single(AgentCliSuggestion.BuildPills(_root, TaskTypePickContext.Empty));

        Assert.Equal("kiro", kiro.Command);
    }

    [Fact]
    public void BuildPills_GhAloneDoesNotSuggestCopilot()
    {
        AgentCliCatalog.IsCommandOnPathOverride = name =>
            name.Equals("gh", StringComparison.OrdinalIgnoreCase);

        var pills = AgentCliSuggestion.BuildPills(_root, TaskTypePickContext.Empty);

        Assert.DoesNotContain(pills, pill => pill.Command.Contains("copilot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(pills, pill => pill.Command.Contains("gh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPills_MarkerFallback_SuggestsWhenNotOnPath()
    {
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Claude");
        File.WriteAllText(Path.Combine(_root, "GEMINI.md"), "# Gemini");
        Directory.CreateDirectory(Path.Combine(_root, ".github"));
        File.WriteAllText(Path.Combine(_root, ".github", "copilot-instructions.md"), "# Copilot");

        var pills = AgentCliSuggestion.BuildPills(_root, TaskTypePickContext.Empty);

        Assert.Contains(pills, pill => pill.Command == "claude" && pill.Source == "agent-marker");
        Assert.Contains(pills, pill => pill.Command == "gemini" && pill.Source == "agent-marker");
        Assert.Contains(pills, pill => pill.Command == "copilot" && pill.Source == "agent-marker");
        Assert.All(
            pills.Where(pill => pill.Source == "agent-marker"),
            pill => Assert.Equal(AgentCliCatalog.MarkerFallbackScore, pill.Score));
    }

    [Fact]
    public void BuildPills_AgentsMd_MapsToCodex()
    {
        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), "# Agents");

        var pills = AgentCliSuggestion.BuildPills(_root, TaskTypePickContext.Empty);

        Assert.Contains(pills, pill => pill.Command == "codex");
    }

    [Fact]
    public void BuildPills_CapsAgentsAtFourByDefault()
    {
        AgentCliCatalog.IsCommandOnPathOverride = _ => true;

        var pills = AgentCliSuggestion.BuildPills(_root, TaskTypePickContext.Empty);

        Assert.Equal(AgentCliCatalog.MaxDefaultAgentPills, pills.Count);
        Assert.True(pills.Count <= 4);
        Assert.All(pills, pill => Assert.Equal(TaskTypeCatalog.Agent, pill.TaskType));
    }

    [Fact]
    public void BuildPills_IncludesNewlyAddedAgentsWhenOnPath()
    {
        AgentCliCatalog.IsCommandOnPathOverride = name =>
            name.Equals("kiro-cli", StringComparison.OrdinalIgnoreCase)
            || name.Equals("agy", StringComparison.OrdinalIgnoreCase)
            || name.Equals("aider", StringComparison.OrdinalIgnoreCase)
            || name.Equals("cmdc", StringComparison.OrdinalIgnoreCase);

        var pills = AgentCliSuggestion.BuildPills(_root, TaskTypePickContext.Empty);
        var commands = pills.Select(pill => pill.Command).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("kiro-cli", commands);
        Assert.Contains("agy", commands);
        Assert.Contains("aider", commands);
        Assert.Contains("cmdc", commands);
        Assert.Equal(4, pills.Count);
    }

    [Fact]
    public void BuildPills_ExcludesUsedCommands()
    {
        AgentCliCatalog.IsCommandOnPathOverride = name =>
            name.Equals("claude", StringComparison.OrdinalIgnoreCase)
            || name.Equals("codex", StringComparison.OrdinalIgnoreCase);

        var used = TaskTypePickContext.FromCommands(["claude"]);
        var pills = AgentCliSuggestion.BuildPills(_root, used);

        Assert.DoesNotContain(pills, pill => pill.Command == "claude");
        Assert.Contains(pills, pill => pill.Command == "codex");
    }

    [Fact]
    public void GetPills_MarkerOnlyDirectory_ReturnsAgentPillsWithoutStack()
    {
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Claude");

        var pills = CommandSuggestionService.GetPills(_root, []);

        Assert.Contains(pills, pill => pill.Command == "claude" && pill.TaskType == TaskTypeCatalog.Agent);
    }

    [Fact]
    public void GetPills_PathAgentsMergeWithDockerSuggestions()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        AgentCliCatalog.IsCommandOnPathOverride = name =>
            name.Equals("claude", StringComparison.OrdinalIgnoreCase);

        var pills = CommandSuggestionService.GetPills(_root, []);

        Assert.Contains(pills, pill => pill.Command == "claude");
        Assert.Contains(pills, pill => pill.Command == "docker compose up");
        Assert.True(pills.First(pill => pill.Command == "claude").Score
            >= pills.First(pill => pill.Command == "docker compose up").Score);
    }

    public void Dispose()
    {
        AgentCliCatalog.IsCommandOnPathOverride = _previousOverride;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
