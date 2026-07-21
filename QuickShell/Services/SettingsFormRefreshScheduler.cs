using System.Runtime.InteropServices;
using System.Threading.Tasks;
using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <inheritdoc cref="ISettingsFormRefreshScheduler"/>
internal sealed class SettingsFormRefreshScheduler : ISettingsFormRefreshScheduler
{
    private readonly IQuickShellLifetime _lifetime;
    private readonly IExtensionCallbackQueue _callbackQueue;

    public SettingsFormRefreshScheduler(IQuickShellLifetime lifetime, IExtensionCallbackQueue callbackQueue)
    {
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _callbackQueue = callbackQueue ?? throw new ArgumentNullException(nameof(callbackQueue));
    }

    public void SchedulePostNavigationRefresh(Action? refresh)
    {
        if (refresh is null)
        {
            return;
        }

        // Queue before navigation so the destination page cannot fetch once and miss
        // the callback. The callback itself performs the lightweight invalidation;
        // the host drains it on its next fetch.
        _callbackQueue.Enqueue(refresh);
    }

    public void ScheduleRefresh(Action? refresh, int delayMs = 50)
    {
        if (refresh is null)
        {
            return;
        }

        if (delayMs <= 0)
        {
            InvokeSafe(refresh);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, _lifetime.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_lifetime.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            _callbackQueue.Enqueue(() => InvokeSafe(refresh));
        }, _lifetime.CancellationToken);
    }

    private static void InvokeSafe(Action? refresh)
    {
        if (refresh is null)
        {
            return;
        }

        try
        {
            refresh();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or COMException)
        {
            // Best effort; the settings page/COM host may have been torn down
            // before this fired. Anything else is a real bug and should surface.
        }
    }
}
