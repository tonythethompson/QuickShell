using QuickShell.Abstractions;
using QuickShell.Classification.Suggestions;
using QuickShell.Models;
using QuickShell.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace QuickShell.Core.Tests;

public class StartupWarmupCoordinatorTests
{
    private static readonly TerminalShortcut[] NoShortcuts = [];

    [Fact]
    public void Nothing_Runs_Before_FirstListSignal()
    {
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.Create(
            new FakeShortcutRepository(NoShortcuts),
            new ShortcutDraftStore(new FakeShortcutRepository(NoShortcuts)),
            settings,
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
        var context = new StartupWarmupContext(services, settings, services.Lifetime);
        var executed = false;
        var stages = new List<IStartupWarmupStage>
        {
            new LambdaStage("marker", _ => executed = true),
        };

        using var coordinator = new StartupWarmupCoordinator(services.Lifetime, context, stages);

        Assert.False(coordinator.IsStarted);
        Assert.False(coordinator.IsCompleted);
        Assert.Empty(coordinator.StageResults);
        Assert.False(executed);
    }

    [Fact]
    public void Repeated_Signal_Is_Idempotent()
    {
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.Create(
            new FakeShortcutRepository(NoShortcuts),
            new ShortcutDraftStore(new FakeShortcutRepository(NoShortcuts)),
            settings,
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
        var context = new StartupWarmupContext(services, settings, services.Lifetime);
        var count = 0;
        var stages = new List<IStartupWarmupStage>
        {
            new LambdaStage("idempotent", _ => Interlocked.Increment(ref count)),
        };

        using var coordinator = new StartupWarmupCoordinator(services.Lifetime, context, stages);
        coordinator.SignalFirstListPublished();
        coordinator.SignalFirstListPublished();
        WaitForCompletion(coordinator);

        Assert.Equal(1, count);
        Assert.True(coordinator.IsStarted);
    }

    [Fact]
    public void Stages_Run_InDeclaredOrder()
    {
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.Create(
            new FakeShortcutRepository(NoShortcuts),
            new ShortcutDraftStore(new FakeShortcutRepository(NoShortcuts)),
            settings,
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
        var context = new StartupWarmupContext(services, settings, services.Lifetime);
        var order = new List<string>();
        var stages = new List<IStartupWarmupStage>
        {
            new LambdaStage("one", _ => order.Add("one")),
            new LambdaStage("two", _ => order.Add("two")),
            new LambdaStage("three", _ => order.Add("three")),
        };

        using var coordinator = new StartupWarmupCoordinator(services.Lifetime, context, stages);
        coordinator.SignalFirstListPublished();
        WaitForCompletion(coordinator);

        Assert.Equal(["one", "two", "three"], order);
        Assert.Equal(["one", "two", "three"], coordinator.StageResults.Select(r => r.Name).ToList());
    }

    [Fact]
    public void SingleFlight_OnlyOneStageRunsAtATime()
    {
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.Create(
            new FakeShortcutRepository(NoShortcuts),
            new ShortcutDraftStore(new FakeShortcutRepository(NoShortcuts)),
            settings,
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
        var context = new StartupWarmupContext(services, settings, services.Lifetime);
        using var stage1Started = new ManualResetEventSlim(false);
        using var stage1Continue = new ManualResetEventSlim(false);
        var stage2SawStage1Done = false;
        var stages = new List<IStartupWarmupStage>
        {
            new LambdaStage("first", _ =>
            {
                stage1Started.Set();
                stage1Continue.Wait();
            }),
            new LambdaStage("second", _ => stage2SawStage1Done = stage1Started.IsSet && stage1Continue.IsSet),
        };

        using var coordinator = new StartupWarmupCoordinator(services.Lifetime, context, stages);
        coordinator.SignalFirstListPublished();
        stage1Started.Wait(TimeSpan.FromSeconds(2));
        stage1Continue.Set();
        WaitForCompletion(coordinator);

        Assert.True(stage2SawStage1Done);
    }

    [Fact]
    public void FailureIsolation_LaterStageRunsAfterEarlierThrows()
    {
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.Create(
            new FakeShortcutRepository(NoShortcuts),
            new ShortcutDraftStore(new FakeShortcutRepository(NoShortcuts)),
            settings,
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
        var context = new StartupWarmupContext(services, settings, services.Lifetime);
        var stages = new List<IStartupWarmupStage>
        {
            new LambdaStage("boom", _ => throw new InvalidOperationException("expected failure")),
            new LambdaStage("recovers", _ => { }),
        };

        using var coordinator = new StartupWarmupCoordinator(services.Lifetime, context, stages);
        coordinator.SignalFirstListPublished();
        WaitForCompletion(coordinator);

        Assert.True(coordinator.IsCompleted);
        Assert.Equal(2, coordinator.StageResults.Count);
        Assert.Equal("InvalidOperationException", coordinator.StageResults[0].Outcome);
        Assert.Null(coordinator.StageResults[1].Outcome);
    }

    [Fact]
    public void Cancellation_StopsFurtherStages()
    {
        var lifetime = new QuickShellLifetime();
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.Create(
            new FakeShortcutRepository(NoShortcuts),
            new ShortcutDraftStore(new FakeShortcutRepository(NoShortcuts)),
            settings,
            new FakeProjectAnalysisService(),
            lifetime);
        var context = new StartupWarmupContext(services, settings, lifetime);
        var ranAfterCancel = false;
        var stages = new List<IStartupWarmupStage>
        {
            new LambdaStage("pre-cancel", _ => lifetime.Cancel()),
            new LambdaStage("post-cancel", (c, t) =>
            {
                t.ThrowIfCancellationRequested();
                ranAfterCancel = true;
            }),
        };

        using var coordinator = new StartupWarmupCoordinator(lifetime, context, stages);
        coordinator.SignalFirstListPublished();
        WaitForCompletion(coordinator);

        Assert.True(coordinator.IsCompleted);
        Assert.False(ranAfterCancel);
    }

    [Fact]
    public void Disposal_PreventsStageExecutionAndStopsBackgroundThread()
    {
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.Create(
            new FakeShortcutRepository(NoShortcuts),
            new ShortcutDraftStore(new FakeShortcutRepository(NoShortcuts)),
            settings,
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
        var context = new StartupWarmupContext(services, settings, services.Lifetime);
        var ran = false;
        var stages = new List<IStartupWarmupStage>
        {
            new LambdaStage("never", _ => ran = true),
        };

        var coordinator = new StartupWarmupCoordinator(services.Lifetime, context, stages);
        coordinator.Dispose();
        coordinator.SignalFirstListPublished();

        // Give a brief moment for any leaked task to start.
        Thread.Sleep(50);

        Assert.False(ran);
        Assert.True(coordinator.IsCompleted || !coordinator.IsStarted);
    }

    [Fact]
    public void FirstListSignal_RacingDispose_DoesNotThrow()
    {
        var settings = new QuickShellSettingsManager();
        var services = TestQuickShellServicesFactory.Create(
            new FakeShortcutRepository(NoShortcuts),
            new ShortcutDraftStore(new FakeShortcutRepository(NoShortcuts)),
            settings,
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
        var context = new StartupWarmupContext(services, settings, services.Lifetime);
        var stages = new List<IStartupWarmupStage>
        {
            new LambdaStage("race", _ => { }),
        };
        var exceptions = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var coordinator = new StartupWarmupCoordinator(services.Lifetime, context, stages);
            Parallel.Invoke(
                () =>
                {
                    try
                    {
                        coordinator.SignalFirstListPublished();
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException
                        and not StackOverflowException
                        and not AccessViolationException
                        and not AppDomainUnloadedException
                        and not BadImageFormatException
                        and not CannotUnloadAppDomainException
                        and not InvalidProgramException
                        and not ThreadAbortException)
                    {
                        exceptions.Enqueue(ex);
                    }
                },
                () =>
                {
                    try
                    {
                        coordinator.Dispose();
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException
                        and not StackOverflowException
                        and not AccessViolationException
                        and not AppDomainUnloadedException
                        and not BadImageFormatException
                        and not CannotUnloadAppDomainException
                        and not InvalidProgramException
                        and not ThreadAbortException)
                    {
                        exceptions.Enqueue(ex);
                    }
                });
        }

        Assert.Empty(exceptions);
    }

    [Fact]
    public void GitIndexWarmup_PassesSavedWorkspaceRootsOnly()
    {
        var root = CreateGitRepoRoot();
        try
        {
            var shortcut = new TerminalShortcut
            {
                Id = "ws1",
                Name = "Workspace",
                Directory = root,
            };
            var repository = new FakeShortcutRepository([shortcut]);
            var gitIndex = new RecordingGitRepoIndex();
            var lifetime = new QuickShellLifetime();
            var settings = new QuickShellSettingsManager();
            var services = CreateServices(repository, gitIndex, settings, lifetime);
            var context = new StartupWarmupContext(services, settings, lifetime);
            var stages = StartupWarmupStages.Create(context);

            using var coordinator = new StartupWarmupCoordinator(lifetime, context, stages);
            coordinator.SignalFirstListPublished(repository.GetSnapshot());
            WaitForCompletion(coordinator);

            Assert.Single(gitIndex.PrewarmCalls);
            var roots = gitIndex.PrewarmCalls[0];
            Assert.Equal(2, roots.Count);
            Assert.Contains(root, roots, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.GetDirectoryName(root)!, roots, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GitIndexWarmup_SkipsPrewarmWhenNoSavedRoots()
    {
        var called = false;
        var gitIndex = new RecordingGitRepoIndex(() => called = true);
        var lifetime = new QuickShellLifetime();
        var settings = new QuickShellSettingsManager();
        var repository = new FakeShortcutRepository(NoShortcuts);
        var services = CreateServices(repository, gitIndex, settings, lifetime);
        var context = new StartupWarmupContext(services, settings, lifetime);
        var stages = StartupWarmupStages.Create(context);

        using var coordinator = new StartupWarmupCoordinator(lifetime, context, stages);
        coordinator.SignalFirstListPublished();
        WaitForCompletion(coordinator);

        Assert.False(called);
        Assert.Empty(gitIndex.PrewarmCalls);
    }

    [Fact]
    public void GitIndexWarmup_DoesNotScanDefaultDrives()
    {
        var root = CreateGitRepoRoot();
        try
        {
            var shortcut = new TerminalShortcut
            {
                Id = "ws1",
                Name = "Workspace",
                Directory = root,
            };
            var repository = new FakeShortcutRepository([shortcut]);
            var gitIndex = new RecordingGitRepoIndex();
            var lifetime = new QuickShellLifetime();
            var settings = new QuickShellSettingsManager();
            var services = CreateServices(repository, gitIndex, settings, lifetime);
            var context = new StartupWarmupContext(services, settings, lifetime);
            var stages = StartupWarmupStages.Create(context);

            using var coordinator = new StartupWarmupCoordinator(lifetime, context, stages);
            coordinator.SignalFirstListPublished(repository.GetSnapshot());
            WaitForCompletion(coordinator);

            Assert.Single(gitIndex.PrewarmCalls);
            var roots = gitIndex.PrewarmCalls[0];
            var driveRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
            Assert.All(roots, r => Assert.NotEqual(driveRoot, r));
            Assert.All(roots, r =>
            {
                Assert.True(
                    r.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || r.Equals(Path.GetDirectoryName(root)!, StringComparison.OrdinalIgnoreCase),
                    $"Unexpected root: {r}");
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateGitRepoRoot()
    {
        var path = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Join(path, ".git"));
        return path;
    }

    private static QuickShellServices CreateServices(
        IShortcutRepository repository,
        IGitRepoIndex gitIndex,
        QuickShellSettingsManager settings,
        IQuickShellLifetime lifetime)
    {
        var analysis = new FakeProjectAnalysisService();
        var drafts = new ShortcutDraftStore(repository);
        var classificationCache = new ProjectClassificationCache(analysis);
        var commandSuggestions = new CommandSuggestionService([
            new WorkspaceSetupTaskSuggestionProvider(),
            new DockerComposeTaskSuggestionProvider(),
            new AgentCliSuggestionProvider(),
        ]);
        var bundle = LaunchTestServices.CreateBundle();
        return new QuickShellServices(
            repository,
            drafts,
            settings,
            analysis,
            commandSuggestions,
            bundle.Executor,
            bundle.Git,
            bundle.Companion,
            bundle.Health,
            bundle.GitGate,
            lifetime,
            gitIndex,
            classificationCache,
            new ExtensionCallbackQueue());
    }

    private static void WaitForCompletion(StartupWarmupCoordinator coordinator, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!coordinator.IsCompleted && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        Assert.True(coordinator.IsCompleted, "Startup warmup coordinator did not complete in time.");
    }

    private sealed class LambdaStage : IStartupWarmupStage
    {
        private readonly Action<IStartupWarmupContext, CancellationToken> _execute;

        public LambdaStage(string name, Action<IStartupWarmupContext> execute)
        {
            Name = name;
            _execute = (c, t) => execute(c);
        }

        public LambdaStage(string name, Action<IStartupWarmupContext, CancellationToken> execute)
        {
            Name = name;
            _execute = execute;
        }

        public string Name { get; }

        public void Execute(IStartupWarmupContext context, CancellationToken cancellationToken) =>
            _execute(context, cancellationToken);
    }

    private sealed class RecordingGitRepoIndex : IGitRepoIndex
    {
        private readonly Action? _onPrewarm;

        public RecordingGitRepoIndex(Action? onPrewarm = null)
        {
            _onPrewarm = onPrewarm;
        }

        public List<IReadOnlyList<string>> PrewarmCalls { get; } = new();

        public bool IsRefreshInFlight => false;

        public void Invalidate()
        {
        }

        public void Prewarm(IReadOnlyList<string> searchRoots, CancellationToken cancellationToken = default)
        {
            _onPrewarm?.Invoke();
            PrewarmCalls.Add(searchRoots.ToList());
        }

        public IReadOnlyList<GitRepoCandidate> Search(
            string query,
            IReadOnlyList<string> searchRoots,
            IReadOnlySet<string>? savedDirectories = null,
            int maxResults = 8,
            CancellationToken cancellationToken = default) =>
            [];

        public IReadOnlyList<GitRepoCandidate> GetAll(
            IReadOnlyList<string>? extraRoots = null,
            CancellationToken cancellationToken = default) =>
            [];

        public void RunAfterNextRefresh(Action callback)
        {
        }

        public bool TryRunAfterNextRefreshIfInFlight(Action callback) => false;
    }
}
