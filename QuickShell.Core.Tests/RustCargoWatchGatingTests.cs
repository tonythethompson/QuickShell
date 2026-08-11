using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(AgentCliCatalogIsolation.Name)]
public sealed class RustCargoWatchGatingTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly Func<string, bool>? _prev;

    public RustCargoWatchGatingTests()
    {
        _prev = AgentCliCatalog.IsCommandOnPathOverride;
        AgentCliCatalog.IsCommandOnPathOverride = _ => false;
        _root = Path.Join(Path.GetTempPath(), "quickshell-rust-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Join(_root, "Cargo.toml"), "[package]\nname = \"demo\"\n");
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
    }

    [Fact]
    public void Build_CargoWatchOnPath_IncludesWatchSuggestion()
    {
        AgentCliCatalog.IsCommandOnPathOverride = n => n.Equals("cargo-watch", StringComparison.OrdinalIgnoreCase);

        var suggestions = WorkspaceSetupSuggestion.Build(_root, _projectAnalysis);

        Assert.Contains(suggestions, task => task.Command == "cargo watch -x run");
    }

    [Fact]
    public void Build_CargoWatchMissing_OmitsWatchAndKeepsRun()
    {
        AgentCliCatalog.IsCommandOnPathOverride = _ => false;

        var suggestions = WorkspaceSetupSuggestion.Build(_root, _projectAnalysis);

        Assert.DoesNotContain(suggestions, task => task.Command.Contains("cargo watch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(suggestions, task => task.Command == "cargo run");
    }

    public void Dispose()
    {
        _provider.Dispose();
        AgentCliCatalog.IsCommandOnPathOverride = _prev;
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }
}
