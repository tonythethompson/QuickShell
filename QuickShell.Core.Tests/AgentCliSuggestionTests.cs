using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AgentCliCatalogIsolation { public const string Name = "AgentCliCatalog"; }

[Collection(AgentCliCatalogIsolation.Name)]
public sealed class AgentCliSuggestionTests : IDisposable
{
    private readonly string _root;
    private readonly Func<string, bool>? _prev;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ICommandSuggestionService _suggestions;

    public AgentCliSuggestionTests()
    {
        _root = Path.Join(Path.GetTempPath(), "quickshell-agents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _prev = AgentCliCatalog.IsCommandOnPathOverride;
        AgentCliCatalog.IsCommandOnPathOverride = _ => false;
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        _suggestions = _provider.GetRequiredService<ICommandSuggestionService>();
    }
    [Fact] public void GetPills_PathDetected_IncludesAgent() { AgentCliCatalog.IsCommandOnPathOverride = n => n.Equals("claude", StringComparison.OrdinalIgnoreCase); var p = _suggestions.GetPills(_root, [], _projectAnalysis); var c = Assert.Single(p); Assert.Equal("claude", c.Command); Assert.Equal(TaskTypeCatalog.Agent, c.TaskType); }
    [Fact] public void GetPills_CopilotUsesCopilotNotGh() { AgentCliCatalog.IsCommandOnPathOverride = n => n.Equals("copilot", StringComparison.OrdinalIgnoreCase) || n.Equals("gh", StringComparison.OrdinalIgnoreCase); var p = _suggestions.GetPills(_root, [], _projectAnalysis); Assert.Contains(p, x => x.Command == "copilot"); Assert.DoesNotContain(p, x => x.Command.Contains("gh", StringComparison.OrdinalIgnoreCase)); }
    [Fact] public void GetPills_CursorAgent_UsesAgentCommand() { AgentCliCatalog.IsCommandOnPathOverride = n => n.Equals("agent", StringComparison.OrdinalIgnoreCase); var c = Assert.Single(_suggestions.GetPills(_root, [], _projectAnalysis)); Assert.Equal("agent", c.Command); }
    [Fact] public void GetPills_AlternatePathExecutable_UsesDetectedCommand() { AgentCliCatalog.IsCommandOnPathOverride = n => n.Equals("kiro", StringComparison.OrdinalIgnoreCase); var k = Assert.Single(_suggestions.GetPills(_root, [], _projectAnalysis)); Assert.Equal("kiro", k.Command); }
    [Fact] public void GetPills_GhAlone_NoCopilot() { AgentCliCatalog.IsCommandOnPathOverride = n => n.Equals("gh", StringComparison.OrdinalIgnoreCase); var p = _suggestions.GetPills(_root, [], _projectAnalysis); Assert.Empty(p); }
    [Fact] public void GetPills_MarkerFallback_Suggests() { File.WriteAllText(Path.Join(_root, "CLAUDE.md"), "# Claude"); File.WriteAllText(Path.Join(_root, "GEMINI.md"), "# Gemini"); var p = _suggestions.GetPills(_root, [], _projectAnalysis); Assert.Contains(p, x => x.Command == "claude" && x.Source == "agent-marker"); Assert.Contains(p, x => x.Command == "gemini" && x.Source == "agent-marker"); }
    [Fact] public void GetPills_AgentsMd_MapsToCodex() { File.WriteAllText(Path.Join(_root, "AGENTS.md"), "# Agents"); var p = _suggestions.GetPills(_root, [], _projectAnalysis); Assert.Contains(p, x => x.Command == "codex"); }
    [Fact] public void GetPills_CapsAtFour() { AgentCliCatalog.IsCommandOnPathOverride = _ => true; var p = _suggestions.GetPills(_root, [], _projectAnalysis); Assert.True(p.Count <= 4); }
    [Fact] public void GetPills_ExcludesUsedCommands() { AgentCliCatalog.IsCommandOnPathOverride = n => n.Equals("claude", StringComparison.OrdinalIgnoreCase); var p = _suggestions.GetPills(_root, ["claude"], _projectAnalysis); Assert.DoesNotContain(p, x => x.Command == "claude"); }
    [Fact] public void GetPills_MarkerOnly_ReturnsAgent() { File.WriteAllText(Path.Join(_root, "CLAUDE.md"), "# Claude"); var p = _suggestions.GetPills(_root, [], _projectAnalysis); Assert.Contains(p, x => x.Command == "claude" && x.TaskType == TaskTypeCatalog.Agent); }
    public void Dispose()
    {
        _provider.Dispose();
        AgentCliCatalog.IsCommandOnPathOverride = _prev;
        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Cleanup failed for '{_root}': {ex}");
        }
    }
}
