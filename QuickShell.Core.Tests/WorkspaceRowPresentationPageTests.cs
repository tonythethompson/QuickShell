using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// End-to-end contracts for the row presentation cache and deferred enrichment on the
/// real pages: first paint runs no git process and applies no icon enrichment, repeated
/// refreshes reuse presentation data, and pages never share command instances.
/// </summary>
[Collection(RowPresentationIsolation.Name)]
public sealed class WorkspaceRowPresentationPageTests : IDisposable
{
    private readonly string _configDirectory;
    private readonly ServiceProvider _serviceProvider;
    private readonly ShortcutRepository _repository;
    private readonly QuickShellSettingsManager _settings;
    private readonly QuickShellServices _quickShellServices;
    private readonly QuickShellPageContext _context;
    private int _gitInvocations;

    public WorkspaceRowPresentationPageTests()
    {
        RowPresentationDiagnostics.ResetForTests();

        _configDirectory = Path.Combine(Path.GetTempPath(), "qs-row-pres-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);

        var services = new ServiceCollection();
        services.AddQuickShellCore(_configDirectory);
        _serviceProvider = services.BuildServiceProvider();
        _repository = (ShortcutRepository)_serviceProvider.GetRequiredService<IShortcutRepository>();
        var drafts = (ShortcutDraftStore)_serviceProvider.GetRequiredService<IDraftStore>();
        var analysis = _serviceProvider.GetRequiredService<IProjectAnalysisService>();
        _settings = new QuickShellSettingsManager();
        var lifetime = _serviceProvider.GetRequiredService<IQuickShellLifetime>();

        // Every git process the pages would start funnels through this counter.
        var bundle = LaunchTestServices.CreateBundle(
            git: LaunchTestServices.CreateGit(runGit: (_, _) =>
            {
                Interlocked.Increment(ref _gitInvocations);
                return GitCommandResult.Failed;
            }));

        _quickShellServices = TestQuickShellServicesFactory.Create(
            _repository, drafts, _settings, analysis, lifetime, bundle);
        _context = new QuickShellPageContext(
            new QuickShellHostServices(_quickShellServices),
            new CreateShortcutCommand(() => { }, _quickShellServices),
            () => { });

        for (var i = 0; i < 5; i++)
        {
            _repository.Upsert(new TerminalShortcut
            {
                Id = "ws-" + i,
                Name = "Workspace " + i,
                Directory = _configDirectory,
                Command = "echo " + i,
            });
        }
    }

    public void Dispose()
    {
        RowPresentationDiagnostics.ResetForTests();
        _serviceProvider.Dispose();
        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    [Fact]
    public void FirstPublication_RunsNoGit_AndDefersIconEnrichment()
    {
        using var page = new QuickShellPage(_context);

        var items = page.GetItems();

        Assert.True(items.Length >= 5, "expected workspace rows in the first published list");
        Assert.Equal(0, Volatile.Read(ref _gitInvocations));

        // Icon work was queued for later but nothing has touched the published rows:
        // enrichment applies only when the host drains the callback queue.
        Assert.True(
            RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentQueued) >= 5);
        Assert.Equal(
            0,
            RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.EnrichmentBatchApplied));
    }

    [Fact]
    public void RepeatedRefresh_SameSnapshot_ReusesPresentationData()
    {
        using var page = new QuickShellPage(_context);
        _ = page.GetItems();

        var buildsAfterFirstPaint = RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheBuild);
        var hitsAfterFirstPaint = RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheHit);

        page.Reload();

        Assert.Equal(
            buildsAfterFirstPaint,
            RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheBuild));
        Assert.True(
            RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheHit) > hitsAfterFirstPaint);
    }

    [Fact]
    public void RepositoryVersionChange_RebuildsPresentation()
    {
        using var page = new QuickShellPage(_context);
        _ = page.GetItems();
        var buildsBefore = RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheBuild);

        _repository.Upsert(new TerminalShortcut
        {
            Id = "ws-new",
            Name = "Workspace new",
            Directory = _configDirectory,
            Command = "echo new",
        });
        page.Reload();

        Assert.True(
            RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheBuild) > buildsBefore);
    }

    [Fact]
    public void HomeAndFallbackPages_ShareTheProviderCache()
    {
        using var page = new QuickShellPage(_context);
        _ = page.GetItems();

        var snapshot = _repository.GetSnapshot();
        var shortcuts = snapshot.Shortcuts.ToArray();

        using var fallback = new QuickShellFallbackPage(_context);
        fallback.SetWorkspaceResults("workspace", shortcuts, snapshot.Version);
        var buildsAfterFallback = RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheBuild);

        // Second render of the same results reuses every fallback presentation.
        fallback.SetWorkspaceResults("workspace", shortcuts, snapshot.Version);

        Assert.Equal(
            buildsAfterFallback,
            RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheBuild));
        Assert.True(RowPresentationDiagnostics.GetCount(RowPresentationDiagnostics.CacheHit) > 0);
    }

    [Fact]
    public void PageSpecificCommands_AreNeverShared()
    {
        var snapshot = _repository.GetSnapshot();
        var shortcut = snapshot.Shortcuts[0];
        var presentation = _quickShellServices.RowPresentation.GetOrBuild(
            shortcut,
            snapshot.Version,
            _settings.RowPresentationFingerprint,
            WorkspaceRowPresentationMode.Home);

        var homeItem = ShortcutListItems.CreateOpen(
            _context, shortcut, presentation, onChanged: () => { }, useHomePinContextMenu: true);
        var otherItem = ShortcutListItems.CreateOpen(
            _context, shortcut, presentation, onChanged: () => { });

        // Presentation strings are shared; commands and menus are page-local.
        Assert.Equal(homeItem.Title, otherItem.Title);
        Assert.NotSame(homeItem.Command, otherItem.Command);
        Assert.NotSame(homeItem.MoreCommands, otherItem.MoreCommands);
    }
}
