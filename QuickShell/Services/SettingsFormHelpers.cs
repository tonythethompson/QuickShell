using System.Threading;
using System.Threading.Tasks;



namespace QuickShell.Services;



internal static class SettingsFormHelpers

{

    private const int DefaultRefreshDelayMs = 50;

    private const int DefaultDebouncedReloadDelayMs = 400;



    private static readonly object DebouncedReloadLock = new();

    private static CancellationTokenSource? _debouncedReloadCts;



    /// <summary>

    /// Defers settings UI refresh so CmdPal can show a page-level toast first.

    /// </summary>

    internal static void ScheduleRefresh(Action? refresh, int delayMs = DefaultRefreshDelayMs)

    {

        if (refresh is null)

        {

            return;

        }



        _ = Task.Run(async () =>

        {

            await Task.Delay(delayMs).ConfigureAwait(false);

            refresh();

        });

    }



    /// <summary>

    /// Coalesces rapid home reloads (e.g. numeric stepper clicks) into one refresh after the user pauses.

    /// </summary>

    internal static void ScheduleDebouncedReload(Action? reload, int delayMs = DefaultDebouncedReloadDelayMs)

    {

        if (reload is null)

        {

            return;

        }



        CancellationTokenSource cts;

        lock (DebouncedReloadLock)

        {

            _debouncedReloadCts?.Cancel();

            _debouncedReloadCts?.Dispose();

            cts = new CancellationTokenSource();

            _debouncedReloadCts = cts;

        }



        _ = Task.Run(async () =>

        {

            try

            {

                await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);

                reload();

            }

            catch (OperationCanceledException)

            {

            }

        });

    }

}


