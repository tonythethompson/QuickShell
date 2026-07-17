using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(AgentCliCatalogIsolation.Name)]
public sealed class CommandSuggestionServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ICommandSuggestionService _suggestions;
    private readonly Func<string, bool>? _prev;

    public CommandSuggestionServiceTests()
    {
        _prev = AgentCliCatalog.IsCommandOnPathOverride;
        AgentCliCatalog.IsCommandOnPathOverride = _ => false;
        _root = Path.Join(Path.GetTempPath(), "quickshell-pills-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        _suggestions = _provider.GetRequiredService<ICommandSuggestionService>();
    }
    [Fact] public void GetPills_DockerProject_IncludesLogsAndServices() { File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}"); _suggestions.ResetForTests(); var p = _suggestions.GetPills(_root, [], _projectAnalysis); Assert.Contains(p, x => x.Command == "docker compose logs -f"); Assert.Contains(p, x => x.Command == "docker compose up"); }
    [Fact] public void HasSuggestions_TrueForDocker_FalseForEmpty() { File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}"); _suggestions.ResetForTests(); Assert.True(_suggestions.HasSuggestions(_root, [], _projectAnalysis)); Assert.False(_suggestions.HasSuggestions(Path.Join(_root, "missing"), [], _projectAnalysis)); Assert.False(_suggestions.HasSuggestions(null, [], _projectAnalysis)); }
    [Fact] public void GetPills_MaxCountOne_MatchesHead() { File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}"); _suggestions.ResetForTests(); var f = _suggestions.GetPills(_root, [], _projectAnalysis); _suggestions.ResetForTests(); var s = _suggestions.GetPills(_root, [], _projectAnalysis, maxCount: 1); Assert.NotEmpty(f); Assert.Single(s); Assert.Equal(f[0].Command, s[0].Command); }
    [Fact] public void GetPills_Cache_RepeatedSameResult() { File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}"); _suggestions.ResetForTests(); var a = _suggestions.GetPills(_root, [], _projectAnalysis); var b = _suggestions.GetPills(_root, [], _projectAnalysis); Assert.Equal(a.Count, b.Count); }
    [Fact] public void GetPills_ExcludesUsedCommands() { File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}"); var p = _suggestions.GetPills(_root, ["docker compose logs -f"], _projectAnalysis); Assert.DoesNotContain(p, x => x.Command == "docker compose logs -f"); }
    [Fact] public void GetPills_LargePackageJson_Caps() { var s = new Dictionary<string, string>(); for (var i = 0; i < 100; i++) s[$"script{i}"] = "echo test"; File.WriteAllText(Path.Join(_root, "package.json"), System.Text.Json.JsonSerializer.Serialize(new { scripts = s })); Assert.True(_suggestions.GetPills(_root, [], _projectAnalysis).Count <= SuggestionPillPresentation.MaxSlots); }
    [Fact] public void TryFindPill_MatchesCommandAndTaskType() { var p = new[] { new CommandSuggestionPill("npm run dev", TaskTypeCatalog.Frontend, "Frontend", "npm run dev", "f", 10, "s"), new CommandSuggestionPill("npm run dev", TaskTypeCatalog.Api, "API", "npm run dev", "a", 8, "s") }; var r = _suggestions.TryFindPill(p, "npm run dev", TaskTypeCatalog.Api); Assert.NotNull(r); Assert.Equal(TaskTypeCatalog.Api, r.TaskType); }
    [Fact] public void ApplyPill_FillThenAppend_ThreeRowsBeforeFourth() { File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}"); var rows = new List<LaunchRowDraft> { new() { LaunchTarget = "default", IsEditorPlaceholder = true }, new() { LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId, IsEditorPlaceholder = true }, new() { LaunchTarget = TerminalCatalog.SameAsPreviousLaunchTargetId, IsEditorPlaceholder = true } }; var pills = _suggestions.GetPills(_root, [], _projectAnalysis); Assert.NotEmpty(pills); Assert.False(_suggestions.ApplyPill(rows, pills[0], "default")); Assert.Equal(3, rows.Count); }
    public void Dispose() { _provider.Dispose(); _suggestions.ResetForTests(); AgentCliCatalog.IsCommandOnPathOverride = _prev; try { Directory.Delete(_root, true); } catch { } }
}
