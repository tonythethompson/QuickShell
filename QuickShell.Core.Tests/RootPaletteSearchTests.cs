using Microsoft.Extensions.DependencyInjection;
using QuickShell;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;
using System.Diagnostics;
using System.Reflection;
using Xunit.Abstractions;

namespace QuickShell.Core.Tests;

public sealed class RootPaletteSearchTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _configDirectory;
    private readonly List<string> _directories = [];

    public RootPaletteSearchTests(ITestOutputHelper output)
    {
        _output = output;
        _configDirectory = Path.Join(Path.GetTempPath(), "qs-root-palette-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch (IOException ex)
        {
            _output.WriteLine($"Best-effort cleanup failed for '{_configDirectory}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _output.WriteLine($"Best-effort cleanup unauthorized for '{_configDirectory}': {ex.Message}");
        }
    }

    [Fact]
    public void Search_TaskActionPrecedesWorkspace()
    {
        var index = CreateIndex([CreateWorkspace("Project", MakeDirectory("project"), command: "dotnet watch")]);

        var result = index.Search("watch", new FakeGitRepoIndex());

        Assert.Equal(RootPaletteResultKind.TaskActions, result.Kind);
        Assert.Single(result.TaskActions!);
    }

    [Fact]
    public void Search_AbbreviationPrecedenceWinsWorkspaceMatch()
    {
        var index = CreateIndex(
        [
            CreateWorkspace("Apiary", MakeDirectory("apiary"), abbreviation: "api"),
            CreateWorkspace("Api", MakeDirectory("api")),
        ]);

        var result = index.Search("api", new FakeGitRepoIndex());

        Assert.Equal(RootPaletteResultKind.Workspaces, result.Kind);
        Assert.Single(result.Workspaces!);
        Assert.Equal("Apiary", result.Workspaces![0].Name);
    }

    [Fact]
    public void Search_DiscoverQueryReturnsDiscoverBeforeGit()
    {
        var git = new FakeGitRepoIndex
        {
            Repos = [new GitRepoCandidate { Name = "discover", Directory = MakeDirectory("discover") }],
        };
        var index = CreateIndex([CreateWorkspace("Alpha", MakeDirectory("alpha"))]);

        var result = index.Search("discover", git);

        Assert.Equal(RootPaletteResultKind.Discover, result.Kind);
    }

    [Fact]
    public void Search_OneCharacterQuerySuppressesGit()
    {
        var git = new FakeGitRepoIndex
        {
            Repos = [new GitRepoCandidate { Name = "Zoo", Directory = MakeDirectory("zoo") }],
        };
        var index = CreateIndex([CreateWorkspace("Bee", MakeDirectory("bee"), command: "echo run")]);

        var result = index.Search("z", git);

        Assert.Equal(RootPaletteResultKind.None, result.Kind);
    }

    [Fact]
    public void Search_LocalWorkspaceHitSuppressesGit()
    {
        var directory = MakeDirectory("alpha");
        var git = new FakeGitRepoIndex
        {
            Repos = [new GitRepoCandidate { Name = "Alpha", Directory = directory }],
        };
        var index = CreateIndex([CreateWorkspace("Alpha", directory)]);

        var result = index.Search("alpha", git);

        Assert.Equal(RootPaletteResultKind.Workspaces, result.Kind);
        Assert.Single(result.Workspaces!);
    }

    [Fact]
    public void Search_SavedDirectoryExcludedFromGitResults()
    {
        var savedDirectory = MakeDirectory("saved");
        var git = new FakeGitRepoIndex
        {
            Repos =
            [
                new GitRepoCandidate { Name = "Uniquegit", Directory = savedDirectory },
            ],
        };
        var index = CreateIndex([CreateWorkspace("Other", savedDirectory)]);

        var result = index.Search("uniquegit", git);

        Assert.Equal(RootPaletteResultKind.None, result.Kind);
    }

    [Fact]
    public void Search_QuickShellSuppressed()
    {
        var index = CreateIndex([CreateWorkspace("Quick Shell", MakeDirectory("quick"))]);

        var result = index.Search("quick shell", new FakeGitRepoIndex());

        Assert.Equal(RootPaletteResultKind.None, result.Kind);
    }

    [Fact]
    public void Index_ReusesSameRevision()
    {
        var repository = new FakeShortcutRepository([CreateWorkspace("Alpha", MakeDirectory("alpha"))]);
        var git = new FakeGitRepoIndex();
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("alpha");
        var first = GetCachedIndex(fallback);
        fallback.UpdateQuery("alph");
        var second = GetCachedIndex(fallback);

        Assert.Same(first, second);
    }

    [Fact]
    public void UpdateQuery_AcquiresSnapshotOncePerQuery()
    {
        var repository = new FakeShortcutRepository([CreateWorkspace("Alpha", MakeDirectory("alpha"))]);
        var git = new FakeGitRepoIndex();
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("alpha");
        fallback.UpdateQuery("beta");

        Assert.Equal(2, repository.GetSnapshotCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Quick Shell")]
    public void UpdateQuery_SuppressedQueriesDoNotAcquireSnapshot(string query)
    {
        var repository = new FakeShortcutRepository([CreateWorkspace("Alpha", MakeDirectory("alpha"))]);
        var fallback = CreateFallback(repository, new FakeGitRepoIndex());

        fallback.UpdateQuery(query);

        Assert.Equal(0, repository.GetSnapshotCallCount);
    }

    [Fact]
    public void UpdateQuery_SingleTaskAction_RoutesDirectly()
    {
        var repository = new FakeShortcutRepository([CreateWorkspace("Alpha", MakeDirectory("alpha"))]);
        var git = new FakeGitRepoIndex();
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("test");

        Assert.Contains("Alpha", fallback.Title, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<OpenShortcutLaunchCommand>(fallback.Command);
    }

    [Fact]
    public void UpdateQuery_MultipleTaskActions_RoutesToListPage()
    {
        var repository = new FakeShortcutRepository(
        [
            CreateWorkspace("Alpha", MakeDirectory("alpha")),
            CreateWorkspace("Beta", MakeDirectory("beta")),
        ]);
        var git = new FakeGitRepoIndex();
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("test");

        Assert.Contains("task actions", fallback.Title, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<QuickShellFallbackPage>(fallback.Command);
    }

    [Fact]
    public void UpdateQuery_SingleWorkspace_RoutesToName()
    {
        var repository = new FakeShortcutRepository([CreateWorkspace("Alpha", MakeDirectory("alpha"))]);
        var git = new FakeGitRepoIndex();
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("alpha");

        Assert.Equal("Alpha", fallback.Title);
    }

    [Fact]
    public void UpdateQuery_MultipleWorkspaces_RoutesToCount()
    {
        var repository = new FakeShortcutRepository(
        [
            CreateWorkspace("Alpha", MakeDirectory("alpha")),
            CreateWorkspace("Alpine", MakeDirectory("alpine")),
        ]);
        var git = new FakeGitRepoIndex();
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("alp");

        Assert.Contains("2 workspaces", fallback.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateQuery_GitRepos_RoutesToCount()
    {
        var directory = MakeDirectory("alpha");
        var git = new FakeGitRepoIndex
        {
            Repos =
            [
                new GitRepoCandidate { Name = "Alpha", Directory = directory },
                new GitRepoCandidate { Name = "Alpine", Directory = MakeDirectory("alpine") },
            ],
        };
        var repository = new FakeShortcutRepository([CreateWorkspace("Other", MakeDirectory("other"))]);
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("alp");

        Assert.Contains("git repos", fallback.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateQuery_Discover_RoutesToDiscoverCommand()
    {
        var git = new FakeGitRepoIndex
        {
            Repos = [new GitRepoCandidate { Name = "discover", Directory = MakeDirectory("discover") }],
        };
        var repository = new FakeShortcutRepository([CreateWorkspace("Alpha", MakeDirectory("alpha"))]);
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("discover");

        Assert.Equal("Discover git repos", fallback.Title);
        Assert.IsType<OpenDiscoverGitReposCommand>(fallback.Command);
    }

    [Fact]
    public void Index_RebuildsAfterRepositoryRevisionChange()
    {
        var temp = Path.Join(_configDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var repository = new ShortcutRepository(temp);
        var directory = Path.Join(temp, "alpha");
        Directory.CreateDirectory(directory);
        repository.Upsert(CreateWorkspace("Alpha", directory));

        var git = new FakeGitRepoIndex();
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("alpha");
        var firstRevision = GetCachedIndex(fallback).Revision;

        var betaDirectory = Path.Join(temp, "beta");
        Directory.CreateDirectory(betaDirectory);
        repository.Upsert(CreateWorkspace("Beta", betaDirectory));
        fallback.UpdateQuery("beta");
        var secondRevision = GetCachedIndex(fallback).Revision;

        Assert.NotEqual(firstRevision, secondRevision);
    }

    [Fact]
    public void UpdateQuery_GenerationGuardIncrements()
    {
        var repository = new FakeShortcutRepository([CreateWorkspace("Alpha", MakeDirectory("alpha"))]);
        var git = new FakeGitRepoIndex();
        var fallback = CreateFallback(repository, git);

        fallback.UpdateQuery("alpha");
        var first = GetField<long>(fallback, "_queryGeneration");
        fallback.UpdateQuery("beta");
        var second = GetField<long>(fallback, "_queryGeneration");

        Assert.True(second > first);
    }

    [Fact]
    public async Task UpdateQuery_DoesNotApplyAnOlderOverlappingResult()
    {
        using var firstSearchStarted = new ManualResetEventSlim();
        using var releaseFirstSearch = new ManualResetEventSlim();
        var repository = new FakeShortcutRepository([CreateWorkspace("Beta", MakeDirectory("beta"))]);
        var git = new FakeGitRepoIndex
        {
            SearchOverride = query =>
            {
                if (query == "alpha")
                {
                    firstSearchStarted.Set();
                    releaseFirstSearch.Wait(TimeSpan.FromSeconds(5));
                    return [new GitRepoCandidate { Name = "Alpha", Directory = MakeDirectory("alpha") }];
                }

                return [];
            },
        };
        var fallback = CreateFallback(repository, git);

        var first = Task.Run(() => fallback.UpdateQuery("alpha"));
        Assert.True(firstSearchStarted.Wait(TimeSpan.FromSeconds(5)));

        fallback.UpdateQuery("beta");
        releaseFirstSearch.Set();
        await first;

        Assert.Equal("Beta", fallback.Title);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public void Benchmark_RootPaletteSearch(int count)
    {
        var shortcuts = new List<TerminalShortcut>(count);
        for (var i = 0; i < count; i++)
        {
            var directory = MakeDirectory($"ws{i}");
            shortcuts.Add(CreateWorkspace($"Workspace{i}", directory, abbreviation: $"w{i}", command: $"echo {i}"));
        }

        var repository = new FakeShortcutRepository(shortcuts);
        var snapshot = repository.GetSnapshot();
        var index = new RootPaletteSearchIndex(snapshot, new TerminalCatalog(new WtProfilesService()));
        var git = new FakeGitRepoIndex
        {
            Repos = [new GitRepoCandidate { Name = "UnsavedRepo", Directory = MakeDirectory("unsaved") }],
        };

        RunBenchmark(count, "RootPaletteSearchIndex", query => index.Search(query, git));
        RunBenchmark(count, "RootPaletteSearchIndex cold", query =>
            new RootPaletteSearchIndex(snapshot, new TerminalCatalog(new WtProfilesService())).Search(query, git));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public void Benchmark_LegacyRootPaletteSearch(int count)
    {
        var shortcuts = new List<TerminalShortcut>(count);
        for (var i = 0; i < count; i++)
        {
            var directory = MakeDirectory($"wslegacy{i}");
            shortcuts.Add(CreateWorkspace($"Workspace{i}", directory, abbreviation: $"w{i}", command: $"echo {i}"));
        }

        var repository = new FakeShortcutRepository(shortcuts);
        var snapshot = repository.GetSnapshot();
        var git = new FakeGitRepoIndex
        {
            Repos = [new GitRepoCandidate { Name = "UnsavedRepo", Directory = MakeDirectory("unsaved-legacy") }],
        };

        RunBenchmark(count, "Legacy snapshot path", query => LegacySearch(snapshot, query, git));
    }

    private static RootPaletteSearchResult LegacySearch(WorkspaceRepositorySnapshot snapshot, string query, FakeGitRepoIndex gitRepos)
    {
        var taskActions = snapshot.SearchTaskActions(query, new TerminalCatalog(new WtProfilesService())).ToArray();
        if (taskActions.Length > 0)
        {
            return new RootPaletteSearchResult(RootPaletteResultKind.TaskActions, TaskActions: taskActions);
        }

        var workspaces = snapshot.SearchForRootPalette(query).ToArray();
        if (workspaces.Length > 0)
        {
            return new RootPaletteSearchResult(RootPaletteResultKind.Workspaces, Workspaces: workspaces);
        }

        if (GitRepoIndex.IsDiscoverQuery(query))
        {
            return new RootPaletteSearchResult(RootPaletteResultKind.Discover);
        }

        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 2)
        {
            return new RootPaletteSearchResult(RootPaletteResultKind.None);
        }

        var extraRoots = GitRepoSearchRoots.FromShortcuts(snapshot.Shortcuts).ToList();
        var savedDirectories = snapshot.Shortcuts
            .Select(shortcut => shortcut.Directory)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var gitReposResults = gitRepos.Search(trimmed, extraRoots, savedDirectories, maxResults: 8);
        if (gitReposResults.Count > 0)
        {
            return new RootPaletteSearchResult(RootPaletteResultKind.GitRepos, GitRepos: gitReposResults);
        }

        return new RootPaletteSearchResult(RootPaletteResultKind.None);
    }

    private void RunBenchmark(int count, string label, Func<string, RootPaletteSearchResult> search)
    {
        var queries = new[] { "workspace", "w50", "echo", "unsaved", "discover", "nomatch" };
        const int warmup = 25;
        const int iterations = 100;

        foreach (var query in queries)
        {
            for (var w = 0; w < warmup; w++)
            {
                _ = search(query);
            }

            var times = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < iterations; i++)
            {
                var iterStart = Stopwatch.GetTimestamp();
                _ = search(query);
                var iterElapsed = Stopwatch.GetElapsedTime(iterStart);
                times.Add(iterElapsed.TotalMicroseconds);
            }

            sw.Stop();
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            var bytesPerCall = (allocatedAfter - allocatedBefore) / (double)iterations;

            times.Sort();
            var median = times[times.Count / 2];
            var p95Index = (int)(times.Count * 0.95);
            var p95 = times[Math.Min(p95Index, times.Count - 1)];

            _output.WriteLine(
                $"[{label}] n={count} query='{query}': median={median:F2}us p95={p95:F2}us alloc={bytesPerCall:F0} bytes/call");
        }
    }

    private static RootPaletteSearchIndex CreateIndex(IEnumerable<TerminalShortcut> shortcuts)
    {
        var repository = new FakeShortcutRepository(shortcuts);
        var snapshot = repository.GetSnapshot();
        return new RootPaletteSearchIndex(snapshot, new TerminalCatalog(new WtProfilesService()));
    }

    private static QuickShellFallback CreateFallback(
        IShortcutRepository repository,
        IGitRepoIndex gitRepos)
    {
        var provider = new ServiceCollection().AddQuickShellCore(repository.ConfigDirectory).BuildServiceProvider();
        var drafts = new ShortcutDraftStore(repository);
        var analysis = provider.GetRequiredService<IProjectAnalysisService>();
        var lifetime = provider.GetRequiredService<IQuickShellLifetime>();
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.Create(repository, drafts, settings, analysis, lifetime, gitRepos);
        var context = new QuickShellPageContext(
            new QuickShellHostServices(services),
            new CreateShortcutCommand(() => { }, services),
            () => { });
        var page = new Lazy<QuickShellFallbackPage>(() => new QuickShellFallbackPage(context));
        return new QuickShellFallback(context, page);
    }

    private string MakeDirectory(string name)
    {
        var path = Path.Join(_configDirectory, name);
        Directory.CreateDirectory(path);
        _directories.Add(path);
        return path;
    }

    private static TerminalShortcut CreateWorkspace(string name, string directory, string? abbreviation = null, string command = "echo test")
    {
        return new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Directory = directory,
            Abbreviation = abbreviation,
            Command = command,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = "main",
                    Label = "Main",
                    Command = command,
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        };
    }

    private static RootPaletteSearchIndex GetCachedIndex(QuickShellFallback fallback) =>
        GetField<RootPaletteSearchIndex?>(fallback, "_cachedSearchIndex")!;

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field.GetValue(target)!;
    }
}
