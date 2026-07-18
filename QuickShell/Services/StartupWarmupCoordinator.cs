using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// Runs startup warmup stages sequentially on a single background thread after the first
/// real workspace list has been published. Failures are isolated, cancellation/disposal
/// stops further stages, and only one I/O-heavy stage runs at a time.
/// </summary>
internal sealed partial class StartupWarmupCoordinator : IStartupWarmupCoordinator
{
    private readonly IQuickShellLifetime _lifetime;
    private readonly IStartupWarmupContext _context;
    private readonly IReadOnlyList<IStartupWarmupStage> _stages;
    private readonly CancellationTokenSource _cts;
    private readonly object _sync = new();
    private readonly Stopwatch _queueWait = new();
    private readonly List<StartupWarmupStageResult> _results = new();
    private int _started;
    private int _disposed;
    private bool _completed;
    private Task? _runTask;
    private IDisposable? _queueWaitSpan;

    public StartupWarmupCoordinator(
        IQuickShellLifetime lifetime,
        IStartupWarmupContext context,
        IEnumerable<IStartupWarmupStage> stages)
    {
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _stages = stages is IReadOnlyList<IStartupWarmupStage> list ? list : new List<IStartupWarmupStage>(stages).AsReadOnly();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.CancellationToken);
    }

    public IReadOnlyList<IStartupWarmupStage> Stages => _stages;

    public bool IsStarted => _started == 1;

    public bool IsCompleted => Volatile.Read(ref _completed);

    public IReadOnlyList<StartupWarmupStageResult> StageResults => _results.AsReadOnly();

    public void SignalFirstListPublished(WorkspaceRepositorySnapshot? snapshot = null)
    {
        if (Volatile.Read(ref _disposed) != 0 || _cts.IsCancellationRequested)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return;
        }

        if (snapshot is not null)
        {
            _context.Snapshot = snapshot;
        }

        _queueWait.Restart();
        _queueWaitSpan = StartupPerformanceTrace.Measure("Warmup queue wait");
        _runTask = Task.Factory.StartNew(
            RunStages,
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void RunStages()
    {
        var queueWaitSpan = Interlocked.Exchange(ref _queueWaitSpan, null);
        if (queueWaitSpan is not null)
        {
            using (queueWaitSpan) { }
        }

        StartupPerformanceTrace.Write($"Warmup queue wait: {_queueWait.Elapsed.TotalMilliseconds:0.###}ms");

        foreach (var stage in _stages)
        {
            if (_cts.IsCancellationRequested)
            {
                StartupPerformanceTrace.Write($"Warmup cancelled before stage: {stage.Name}");
                break;
            }

            using (StartupPerformanceTrace.Measure($"Warmup stage: {stage.Name}"))
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    stage.Execute(_context, _cts.Token);
                    lock (_sync)
                    {
                        _results.Add(new StartupWarmupStageResult(stage.Name, sw.Elapsed, false, null));
                    }

                    StartupPerformanceTrace.Write($"Warmup stage completed: {stage.Name} {sw.Elapsed.TotalMilliseconds:0.###}ms");
                }
                catch (OperationCanceledException)
                {
                    lock (_sync)
                    {
                        _results.Add(new StartupWarmupStageResult(stage.Name, sw.Elapsed, false, "cancelled"));
                    }

                    StartupPerformanceTrace.Write($"Warmup stage cancelled: {stage.Name} {sw.Elapsed.TotalMilliseconds:0.###}ms");
                    break;
                }
                // codeql[cs/catch-of-all-exceptions]: intentional isolation boundary — a
                // failing warmup stage (any exception type) must not take down startup or
                // block later stages; the failure is recorded and traced instead.
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (_sync)
                    {
                        _results.Add(new StartupWarmupStageResult(stage.Name, sw.Elapsed, false, ex.GetType().Name));
                    }

                    StartupPerformanceTrace.Write($"Warmup stage failed: {stage.Name} {ex.GetType().Name}: {ex.Message}");
                    SupportDiagnostics.Write(
                        "StartupWarmupCoordinator",
                        "stage failed",
                        new { stage = stage.Name, exception = ex.GetType().Name, message = ex.Message },
                        runId: "warmup");
                    continue;
                }
            }
        }

        Volatile.Write(ref _completed, true);
        _queueWait.Stop();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _runTask?.Wait(TimeSpan.FromSeconds(2));
        }
        // codeql[cs/catch-of-all-exceptions]: intentional — Dispose must not throw during
        // process shutdown regardless of why the wait failed (cancellation, aggregate
        // exception from the stage task, timeout).
        catch
        {
            // Best effort: the process is shutting down.
        }

        _queueWaitSpan?.Dispose();
        _cts.Dispose();
    }
}
