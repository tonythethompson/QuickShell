using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;

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
    private readonly QuickShellPageContext _context;

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
        var lifetime = _serviceProvider.GetRequiredService<IQuickShellLifetime>();
        _quickShellServices = TestQuickShellServicesFactory.CreateFromProvider(
            _serviceProvider,
            _repository,
            drafts,
            _settings,
            analysis,
            lifetime);
        _context = new QuickShellPageContext(
            new QuickShellHostServices(_quickShellServices),
            new CreateShortcutCommand(() => { }, _quickShellServices),
            () => { });

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
        using var page = new QuickShellPage(_context);
        _ = page.GetItems();

        SetPrivateField(page, "_query", "Alpha");
        SetPrivateField(page, "_hasShownInitialList", false);

        page.UpdateSearchText(string.Empty, "Alpha");

        var titles = page.GetItems().OfType<ListItem>().Select(item => item.Title).ToList();
        Assert.Contains("Alpha", titles);
        Assert.DoesNotContain("Beta", titles);
    }

    [Fact]
    public void Reload_PreservesDirectoryRepairState_AndRebuildsCachedRowAsRepair()
    {
        var shortcut = _repository.GetByName("Alpha")!;
        var repairKey = GetDirectoryRepairKey(shortcut);

        var previousScheduler = QuickShellPage.DirectoryRepairProbeSchedulerOverride;
        QuickShellPage.DirectoryRepairProbeSchedulerOverride = _ => { };
        try
        {
            using var page = new QuickShellPage(_context);
            _ = page.GetItems();

            SetDirectoryRepairState(page, repairKey, needsRepair: true);

            page.Reload();

            Assert.True(TryGetDirectoryRepairNeedsRepair(page, repairKey, out var stillNeedsRepair));
            Assert.True(stillNeedsRepair);

            var items = page.GetItems().OfType<ListItem>().ToList();
            var alphaItem = items.Single(i => i.Title == "Alpha");
            // A repair row opens the edit form, not the terminal launch command.
            Assert.IsType<ShortcutFormPage>(alphaItem.Command);
        }
        finally
        {
            QuickShellPage.DirectoryRepairProbeSchedulerOverride = previousScheduler;
        }
    }

    [Fact]
    public void InvalidateWorkspaces_PreservesDirectoryRepairState()
    {
        var shortcut = _repository.GetByName("Alpha")!;
        var repairKey = GetDirectoryRepairKey(shortcut);

        using var page = new QuickShellPage(_context);
        _ = page.GetItems();

        SetDirectoryRepairState(page, repairKey, needsRepair: true);

        page.InvalidateWorkspaces();

        Assert.True(TryGetDirectoryRepairNeedsRepair(page, repairKey, out var stillNeedsRepair));
        Assert.True(stillNeedsRepair);
    }

    [Fact]
    public void RequiresHomeRepair_ExpiredEntry_AllowsReprobe()
    {
        var shortcut = _repository.GetByName("Alpha")!;
        var repairKey = GetDirectoryRepairKey(shortcut);
        var scheduled = 0;

        var previousScheduler = QuickShellPage.DirectoryRepairProbeSchedulerOverride;
        QuickShellPage.DirectoryRepairProbeSchedulerOverride = _ => Interlocked.Increment(ref scheduled);
        try
        {
            using var page = new QuickShellPage(_context);
            _ = page.GetItems();

            // Expired "needs repair" should still paint as repair, but schedule a fresh probe.
            SetDirectoryRepairState(page, repairKey, needsRepair: true, ttlMs: -1);
            ClearDirectoryRepairInFlight(page, repairKey);

            page.Reload();

            var items = page.GetItems().OfType<ListItem>().ToList();
            var alphaItem = items.Single(i => i.Title == "Alpha");
            Assert.IsType<ShortcutFormPage>(alphaItem.Command);
            Assert.True(scheduled >= 1);
        }
        finally
        {
            QuickShellPage.DirectoryRepairProbeSchedulerOverride = previousScheduler;
        }
    }

    private static string GetDirectoryRepairKey(TerminalShortcut shortcut) =>
        string.Concat(shortcut.Id, "|", shortcut.Directory);

    private static void SetDirectoryRepairState(
        QuickShellPage page,
        string key,
        bool needsRepair,
        long ttlMs = QuickShellPage.DirectoryRepairCacheTtlMs)
    {
        var dict = GetDirectoryRepairStates(page);
        var entryType = typeof(QuickShellPage).GetNestedType(
            "DirectoryRepairCacheEntry",
            BindingFlags.NonPublic)!;
        var entry = Activator.CreateInstance(
            entryType,
            needsRepair,
            Environment.TickCount64 + ttlMs)!;
        dict.GetType().GetProperty("Item")!.SetValue(dict, entry, [key]);
    }

    private static bool TryGetDirectoryRepairNeedsRepair(
        QuickShellPage page,
        string key,
        out bool needsRepair)
    {
        needsRepair = false;
        var dict = GetDirectoryRepairStates(page);
        var tryGet = dict.GetType().GetMethod("TryGetValue")!;
        var args = new object?[] { key, null };
        if (!(bool)tryGet.Invoke(dict, args)!)
        {
            return false;
        }

        needsRepair = (bool)args[1]!.GetType().GetProperty("NeedsRepair")!.GetValue(args[1])!;
        return true;
    }

    private static void ClearDirectoryRepairInFlight(QuickShellPage page, string key)
    {
        var checks = GetPrivateField<ConcurrentDictionary<string, byte>>(page, "_directoryRepairChecks");
        checks.TryRemove(key, out _);
    }

    private static object GetDirectoryRepairStates(QuickShellPage page) =>
        typeof(QuickShellPage)
            .GetField("_directoryRepairStates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(page)!;

    [Fact]
    public void DiscoverSearch_RevertingToAppliedQuery_ReplacesPendingSearch()
    {
        using var page = new TestDiscoverGitReposPage(_context);
        SetPrivateField(page, "_hasShownInitialList", true);

        page.UpdateSearchText(string.Empty, "alpha");
        page.UpdateSearchText("alpha", string.Empty);

        GetPrivateField<SearchDebouncer>(page, "_searchDebouncer").FlushNow();

        Assert.Equal(string.Empty, GetPrivateField<string>(page, "_query"));
    }

    [Fact]
    public void HomeSearch_RevertingToAppliedQuery_ReplacesPendingSearch()
    {
        using var page = new QuickShellPage(_context);
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
        using var page = new QuickShellPage(_context);
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

    [Fact]
    public void Reload_SameThreadWhileRefreshInProgress_QueuesInsteadOfDeadlocking()
    {
        using var page = new QuickShellPage(_context);
        _ = page.GetItems();

        SetPrivateField(page, "_refreshInProgress", true);
        SetPrivateField(page, "_refreshThreadId", Environment.CurrentManagedThreadId);

        // Favorite/pin Reload can re-enter on the fetch thread via RaiseItemsChanged →
        // GetItems → Drain. Waiting on _refreshInProgress here used to hang forever.
        page.Reload();

        Assert.True(GetPrivateField<bool>(page, "_refreshQueued"));
        Assert.True(GetPrivateField<bool>(page, "_refreshInProgress"));

        SetPrivateField(page, "_refreshInProgress", false);
        SetPrivateField(page, "_refreshThreadId", 0);
        SetPrivateField(page, "_refreshQueued", false);

        _repository.TogglePinned("Alpha");
        Assert.True(_repository.GetByName("Alpha")!.IsPinned);

        page.Reload();

        var items = page.GetItems();
        Assert.Contains(items.OfType<ListItem>(), item => item.Title == "Alpha");
        Assert.Contains(
            items.OfType<Microsoft.CommandPalette.Extensions.Toolkit.Separator>(),
            separator => string.Equals(separator.Title, Strings.Section_Favorites, StringComparison.Ordinal));
    }

    [Fact]
    public void FallbackSearch_RefreshesAfterGitDiscoveryCompletes()
    {
        var gitRepos = new RefreshingGitRepoIndex();
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.CreateFromProvider(
            _serviceProvider,
            _repository,
            _serviceProvider.GetRequiredService<IDraftStore>(),
            settings,
            _serviceProvider.GetRequiredService<IProjectAnalysisService>(),
            _serviceProvider.GetRequiredService<IQuickShellLifetime>(),
            gitRepos);
        var context = new QuickShellPageContext(
            new QuickShellHostServices(services),
            new CreateShortcutCommand(() => { }, services),
            () => { });
        using var fallback = new QuickShellFallback(
            context,
            new Lazy<QuickShellFallbackPage>(() => new QuickShellFallbackPage(context)));

        fallback.UpdateQuery("outside");

        Assert.Equal(string.Empty, fallback.Title);
        Assert.True(gitRepos.HasRefreshCallback);

        gitRepos.CompleteRefresh(
        [
            new GitRepoCandidate
            {
                Name = "Outside",
                Directory = @"D:\outside",
            },
        ]);

        Assert.Equal("Add Outside", fallback.Title);
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

    private sealed class TestDiscoverGitReposPage(QuickShellPageContext context) : DiscoverGitReposPage(context)
    {
    }

    private static T GetPrivateField<T>(BehaviorSettingsForm form, string name) =>
        (T)typeof(BehaviorSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static T GetPrivateField<T>(TerminalDefaultsSettingsForm form, string name) =>
        (T)typeof(TerminalDefaultsSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private sealed class RefreshingGitRepoIndex : IGitRepoIndex
    {
        private Action? _refreshCallback;
        private IReadOnlyList<GitRepoCandidate> _results = [];

        public bool IsRefreshInFlight { get; private set; } = true;

        public bool HasRefreshCallback => _refreshCallback is not null;

        public IReadOnlyList<GitRepoCandidate> GetAll(
            IReadOnlyList<string>? extraRoots = null,
            CancellationToken cancellationToken = default) =>
            _results;

        public void Invalidate()
        {
            _results = [];
            IsRefreshInFlight = true;
        }

        public void Prewarm(
            IReadOnlyList<string> searchRoots,
            CancellationToken cancellationToken = default)
        {
        }

        public void RunAfterNextRefresh(Action callback)
        {
            _refreshCallback = callback;
        }

        public IReadOnlyList<GitRepoCandidate> Search(
            string query,
            IReadOnlyList<string> searchRoots,
            IReadOnlySet<string>? savedDirectories = null,
            int maxResults = 8,
            CancellationToken cancellationToken = default) =>
            _results;

        public bool TryRunAfterNextRefreshIfInFlight(Action callback)
        {
            if (!IsRefreshInFlight)
            {
                return false;
            }

            _refreshCallback = callback;
            return true;
        }

        public void CompleteRefresh(IReadOnlyList<GitRepoCandidate> results)
        {
            _results = results;
            IsRefreshInFlight = false;
            var callback = _refreshCallback;
            _refreshCallback = null;
            callback?.Invoke();
        }
    }
}
