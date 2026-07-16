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
    private readonly IQuickShellServices _quickShellServices;

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
        _quickShellServices = new QuickShellServices(_repository, drafts, _settings, analysis);

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
        using var page = new QuickShellPage(_settings, new CreateShortcutCommand(() => { }, _quickShellServices), _quickShellServices);
        _ = page.GetItems();

        SetPrivateField(page, "_query", "Alpha");
        SetPrivateField(page, "_hasShownInitialList", false);

        page.UpdateSearchText(string.Empty, "Alpha");

        var titles = page.GetItems().OfType<ListItem>().Select(item => item.Title).ToList();
        Assert.Contains("Alpha", titles);
        Assert.DoesNotContain("Beta", titles);
    }

    [Fact]
    public void DiscoverSearch_RevertingToAppliedQuery_ReplacesPendingSearch()
    {
        using var page = new TestDiscoverGitReposPage(() => { }, _quickShellServices);
        SetPrivateField(page, "_hasShownInitialList", true);

        page.UpdateSearchText(string.Empty, "alpha");
        page.UpdateSearchText("alpha", string.Empty);

        GetPrivateField<SearchDebouncer>(page, "_searchDebouncer").FlushNow();

        Assert.Equal(string.Empty, GetPrivateField<string>(page, "_query"));
    }

    [Fact]
    public void HomeSearch_RevertingToAppliedQuery_ReplacesPendingSearch()
    {
        using var page = new QuickShellPage(_settings, new CreateShortcutCommand(() => { }, _quickShellServices), _quickShellServices);
        _ = page.GetItems();
        SetPrivateField(page, "_query", "a");

        page.UpdateSearchText("a", "ab");
        page.UpdateSearchText("ab", "a");

        GetPrivateField<SearchDebouncer>(page, "_searchDebouncer").FlushNow();

        Assert.Equal("a", GetPrivateField<string>(page, "_query"));
    }

    [Fact]
    public void RefreshTerminals_PreservesPendingCombinedSettings()
    {
        var pendingApp = string.Equals(
            _settings.TerminalApplicationId,
            TerminalHostIds.WindowsConsoleHost,
            StringComparison.OrdinalIgnoreCase)
            ? TerminalHostIds.WindowsTerminal
            : TerminalHostIds.WindowsConsoleHost;
        var pendingSingleWindowTabs = _settings.SeparateWindowsForMultiLaunch;
        var pendingShowRecents = !QuickShellRecentSettings.IsEnabled(_settings.RecentWorkspaceCount);
        var pendingBlockDirtyBranchSwitch = !_settings.BlockDirtyBranchSwitch;
        var inputs = $$"""
            {
              "terminalApplication": "{{pendingApp}}",
              "defaultProfile": "{{TerminalHostIds.DefaultProfile}}",
              "singleWindowTabs": {{pendingSingleWindowTabs.ToString().ToLowerInvariant()}},
              "showRecents": {{pendingShowRecents.ToString().ToLowerInvariant()}},
              "blockDirtyBranchSwitch": {{pendingBlockDirtyBranchSwitch.ToString().ToLowerInvariant()}}
            }
            """;

        var form = new BehaviorSettingsForm(_settings, services: _quickShellServices);
        _ = form.SubmitForm(inputs, "{\"action\":\"refreshTerminals\"}");

        var terminalForm = GetPrivateField<TerminalDefaultsSettingsForm>(form, "_terminalForm");
        Assert.Equal(pendingApp, GetPrivateField<string>(terminalForm, "_pendingApp"));
        Assert.Equal(TerminalHostIds.DefaultProfile, GetPrivateField<string>(terminalForm, "_pendingProfile"));
        Assert.Equal(pendingSingleWindowTabs, GetPrivateField<bool>(form, "_pendingSingleWindowTabs"));
        Assert.Equal(pendingShowRecents, GetPrivateField<bool>(form, "_pendingShowRecents"));
        Assert.Equal(pendingBlockDirtyBranchSwitch, GetPrivateField<bool>(form, "_pendingBlockDirtyBranchSwitch"));
    }

    [Fact]
    public void Reload_AfterUnpinnedWorkspaceRename_RebuildsCachedRow()
    {
        using var page = new QuickShellPage(_settings, new CreateShortcutCommand(() => { }, _quickShellServices), _quickShellServices);
        _ = page.GetItems();

        _repository.Upsert(new TerminalShortcut
        {
            Id = "alpha",
            Name = "Renamed Alpha",
            Directory = _configDirectory,
            Command = "echo alpha",
        }, originalName: "Alpha");

        page.Reload();

        var titles = page.GetItems().OfType<ListItem>().Select(item => item.Title).ToList();
        Assert.Contains("Renamed Alpha", titles);
        Assert.DoesNotContain("Alpha", titles);
    }

    public void Dispose()
    {
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

    private static void SetPrivateField<T>(DiscoverGitReposPage page, string name, T value) =>
        typeof(DiscoverGitReposPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    private static T GetPrivateField<T>(QuickShellPage page, string name) =>
        (T)typeof(QuickShellPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static T GetPrivateField<T>(DiscoverGitReposPage page, string name) =>
        (T)typeof(DiscoverGitReposPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private sealed class TestDiscoverGitReposPage(Action onReload, IQuickShellServices? services = null) : DiscoverGitReposPage(onReload, services)
    {
    }

    private static T GetPrivateField<T>(BehaviorSettingsForm form, string name) =>
        (T)typeof(BehaviorSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static T GetPrivateField<T>(TerminalDefaultsSettingsForm form, string name) =>
        (T)typeof(TerminalDefaultsSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
}
