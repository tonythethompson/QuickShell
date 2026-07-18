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
    private bool _disposed;
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

    public bool IsStarted => Volatile.Read(ref _started) == 1;

    public bool IsCompleted => Volatile.Read(ref _completed);

    public IReadOnlyList<StartupWarmupStageResult> StageResults
    {
        get
        {
            lock (_sync)
            {
                return _results.ToArray();
            }
        }
    }

    public void SignalFirstListPublished(WorkspaceRepositorySnapshot? snapshot = null)
    {
        lock (_sync)
        {
            if (_disposed || _cts.IsCancellationRequested || _started != 0)
            {
                return;
            }

            _started = 1;

            if (snapshot is not null)
            {
                _context.Snapshot = snapshot;
            }

            _queueWait.Restart();
            _queueWaitSpan = StartupPerformanceTrace.Measure("Warmup queue wait");
            var token = _cts.Token;
            _runTask = Task.Factory.StartNew(
                RunStages,
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
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
                        _results.Add(new StartupWarmupStageResult(stage.Name, sw.Elapsed, null));
                    }

                    StartupPerformanceTrace.Write($"Warmup stage completed: {stage.Name} {sw.Elapsed.TotalMilliseconds:0.###}ms");
                }
                catch (OperationCanceledException)
                {
                    lock (_sync)
                    {
                        _results.Add(new StartupWarmupStageResult(stage.Name, sw.Elapsed, "cancelled"));
                    }

                    StartupPerformanceTrace.Write($"Warmup stage cancelled: {stage.Name} {sw.Elapsed.TotalMilliseconds:0.###}ms");
                    break;
                }
                catch (Exception ex) when (!IsCriticalException(ex))
                {
                    lock (_sync)
                    {
                        _results.Add(new StartupWarmupStageResult(stage.Name, sw.Elapsed, ex.GetType().Name));
                    }

                    StartupPerformanceTrace.Write($"Warmup stage failed: {stage.Name} {ex.GetType().Name}: {ex.Message}");
                    SupportDiagnostics.Write(
                        "StartupWarmupCoordinator",
                        "stage failed",
                        new { stage = stage.Name, exception = ex.GetType().Name });
                    continue;
                }
            }
        }

        Volatile.Write(ref _completed, true);
        _queueWait.Stop();
    }

    private static bool IsCriticalException(Exception ex)
    {
        return ex is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException
            or ThreadAbortException;
    }

    public void Dispose()
    {
        Task? runTask;
        IDisposable? queueWaitSpan;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cts.Cancel();
            runTask = _runTask;
            queueWaitSpan = Interlocked.Exchange(ref _queueWaitSpan, null);
        }

        try
        {
            runTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
            // Best effort: the process is shutting down.
        }
        catch (ObjectDisposedException)
        {
            // Best effort: the process is shutting down.
        }
        catch (AggregateException)
        {
            // Best effort: the process is shutting down.
        }

        queueWaitSpan?.Dispose();
        if (runTask is null || runTask.IsCompleted)
        {
            _cts.Dispose();
        }
    }
}
