using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services.WorkspaceEditor;

internal sealed partial class WorkspaceEditor
{
    private void ScheduleSuggestionScan()
    {
        if (_suggestionScanComplete)
        {
            return;
        }

        var directory = _draft.Directory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            _suggestionScanComplete = true;
            return;
        }

        var generation = Interlocked.Increment(ref _scanGeneration);
        var usedCommands = _draft.Commands.Select(command => command.Command).ToArray();
        var token = _scanCts?.Token ?? CancellationToken.None;

        _ = Task.Run(() =>
        {
            try
            {
                if (!token.IsCancellationRequested)
                {
                    _ = _services.CommandSuggestions.GetPills(directory, usedCommands, _services.ProjectAnalysis);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when scan is canceled.
            }
            catch (IOException)
            {
                // Best effort — form remains usable without pills.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort — form remains usable without pills.
            }
            catch (ArgumentException)
            {
                // Best effort — form remains usable without pills.
            }
            catch (InvalidOperationException)
            {
                // Best effort — form remains usable without pills.
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            lock (_sync)
            {
                if (_disposed || generation != _scanGeneration)
                {
                    return;
                }

                _suggestionScanComplete = true;
                OnChanged();
            }
        }, token);
    }

    private void CancelScan()
    {
        Interlocked.Increment(ref _scanGeneration);
        var cts = _scanCts;
        _scanCts = null;
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Best effort.
        }

        try
        {
            cts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Best effort.
        }
    }

    private void InvalidateSuggestionScan()
    {
        _suggestionScanComplete = false;
        Interlocked.Increment(ref _scanGeneration);
        ScheduleSuggestionScan();
    }
}
