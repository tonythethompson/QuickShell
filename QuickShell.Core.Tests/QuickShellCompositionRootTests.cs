using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class QuickShellCompositionRootTests : IDisposable
{
    private readonly string _configDirectory;
    private readonly ServiceProvider _services;

    public QuickShellCompositionRootTests()
    {
        _configDirectory = Path.Join(
            Path.GetTempPath(),
            "quickshell-composition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);

        var collection = new ServiceCollection();
        collection.AddQuickShellCore(_configDirectory);
        _services = collection.BuildServiceProvider();
    }

    [Fact]
    public void AddQuickShellCore_resolves_same_singleton_repository()
    {
        var first = _services.GetRequiredService<IShortcutRepository>();
        var second = _services.GetRequiredService<IShortcutRepository>();

        Assert.Same(first, second);
        Assert.IsType<ShortcutRepository>(first);
        Assert.Equal(_configDirectory, first.ConfigDirectory);
    }

    [Fact]
    public void AddQuickShellCore_pairs_draft_store_with_same_repository_singleton()
    {
        var repository = _services.GetRequiredService<IShortcutRepository>();
        var drafts = _services.GetRequiredService<IDraftStore>();
        var draftsAgain = _services.GetRequiredService<IDraftStore>();

        Assert.Same(drafts, draftsAgain);
        Assert.IsType<ShortcutDraftStore>(drafts);
        Assert.Equal(
            Path.Join(_configDirectory, "shortcut-edit-draft.json"),
            drafts.DraftPath);
        Assert.Same(repository, _services.GetRequiredService<IShortcutRepository>());
    }

    [Fact]
    public void AddQuickShellCore_resolves_same_singleton_atomic_file_writer()
    {
        var first = _services.GetRequiredService<IAtomicFileWriter>();
        var second = _services.GetRequiredService<IAtomicFileWriter>();

        Assert.Same(first, second);
        Assert.IsType<AtomicFileWriter>(first);
    }

    [Fact]
    public void AddQuickShellCore_resolves_command_id_parser()
    {
        var parser = _services.GetRequiredService<ICommandIdParser>();
        Assert.IsType<CommandIdParser>(parser);
        Assert.Same(parser, _services.GetRequiredService<ICommandIdParser>());
    }

    [Fact]
    public void AddQuickShellCore_resolves_core_service_abstractions()
    {
        Assert.IsType<TerminalLauncherService>(_services.GetRequiredService<ITerminalLauncher>());
        Assert.IsType<TerminalProfileResolverService>(_services.GetRequiredService<ITerminalProfileResolver>());
        Assert.IsType<WorkspaceMapperService>(_services.GetRequiredService<IWorkspaceMapper>());
        Assert.IsType<GitRepoIndexService>(_services.GetRequiredService<IGitRepoIndex>());
        Assert.IsType<WorkspaceGitOperationsService>(_services.GetRequiredService<IWorkspaceGitOperations>());
        Assert.IsType<WorkspaceHealthCheckerService>(_services.GetRequiredService<IWorkspaceHealthChecker>());
    }

    [Fact]
    public void AddQuickShellCore_resolves_project_analysis_service()
    {
        var analysis = _services.GetRequiredService<IProjectAnalysisService>();
        Assert.IsType<Classification.ProjectAnalysisService>(analysis);
        Assert.Same(analysis, _services.GetRequiredService<IProjectAnalysisService>());
        Assert.NotEmpty(_services.GetServices<IProjectClassifier>());
    }

    [Fact]
    public void AddQuickShellCore_resolves_companion_and_dev_server_detectors()
    {
        Assert.IsType<Classification.Detectors.CompanionAppDetector>(_services.GetRequiredService<ICompanionAppDetector>());
        Assert.IsType<Classification.Detectors.DevServerDetector>(_services.GetRequiredService<IDevServerDetector>());
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
