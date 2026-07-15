using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using System.Reflection;

namespace QuickShell.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class QuickShellServicesIsolation
{
    public const string Name = "QuickShellServices";
}

[Collection(QuickShellServicesIsolation.Name)]
public sealed class QuickShellPageSearchTests : IDisposable
{
    private readonly string _configDirectory;
    private readonly ServiceProvider _serviceProvider;
    private readonly ShortcutRepository _repository;
    private readonly QuickShellSettingsManager _settings;

    public QuickShellPageSearchTests()
    {
        _configDirectory = Path.Combine(Path.GetTempPath(), "qs-home-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);

        var services = new ServiceCollection();
        services.AddQuickShellCore(_configDirectory);
        _serviceProvider = services.BuildServiceProvider();
        _repository = (ShortcutRepository)_serviceProvider.GetRequiredService<IShortcutRepository>();
        var drafts = (ShortcutDraftStore)_serviceProvider.GetRequiredService<IDraftStore>();
        var analysis = _serviceProvider.GetRequiredService<IProjectAnalysisService>();
        _settings = new QuickShellSettingsManager();
        QuickShellServices.Bind(new QuickShellServices(_repository, drafts, _settings, analysis));

        _repository.Upsert(new TerminalShortcut
        {
            Id = "alpha",
            Name = "Alpha",
            Directory = _configDirectory,
            Command = "echo alpha",
        });
        _repository.Upsert(new TerminalShortcut
        {
            Id = "beta",
            Name = "Beta",
            Directory = _configDirectory,
            Command = "echo beta",
        });
    }

    [Fact]
    public void UpdateSearchText_ReopenedWithRestoredQuery_RebuildsFilteredItems()
    {
        using var page = new QuickShellPage(_settings, new CreateShortcutCommand(() => { }));
        _ = page.GetItems();

        SetPrivateField(page, "_query", "Alpha");
        SetPrivateField(page, "_hasShownInitialList", false);

        page.UpdateSearchText(string.Empty, "Alpha");

        var titles = page.GetItems().OfType<ListItem>().Select(item => item.Title).ToList();
        Assert.Contains("Alpha", titles);
        Assert.DoesNotContain("Beta", titles);
    }

    public void Dispose()
    {
        QuickShellServices.Unbind();
        _serviceProvider.Dispose();

        try
        {
            Directory.Delete(_configDirectory, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private static void SetPrivateField<T>(QuickShellPage page, string name, T value) =>
        typeof(QuickShellPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
}
