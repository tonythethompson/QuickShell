using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace QuickShell.Services;

internal static class SettingsFormHelpers
{
    private const int DefaultRefreshDelayMs = 50;

    /// <summary>
    /// Delay before refreshing list UI after form navigation (GoBack) completes.
    /// </summary>
    internal const int PostNavigationRefreshDelayMs = 1;

    /// <summary>
    /// Defers the refresh so the calling page can return its current items before the
    /// heavier refresh work runs. Keeps the same exception handling as ScheduleRefresh.
    /// </summary>
    internal static void SchedulePostNavigationRefresh(Action? refresh)
    {
        if (refresh is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(PostNavigationRefreshDelayMs).ConfigureAwait(false);
            InvokeSafe(refresh);
        });
    }

    /// <summary>
    /// Defers settings UI refresh so CmdPal can show a page-level toast first.
    /// </summary>
    internal static void ScheduleRefresh(Action? refresh, int delayMs = DefaultRefreshDelayMs)
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
            await Task.Delay(delayMs).ConfigureAwait(false);
            InvokeSafe(refresh);
        });
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
