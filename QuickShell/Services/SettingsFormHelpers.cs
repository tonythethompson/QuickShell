using System.Threading.Tasks;

namespace QuickShell.Services;

internal static class SettingsFormHelpers
{
    /// <summary>
    /// Defers settings UI refresh so CmdPal can show a page-level toast first.
    /// </summary>
    internal static void ScheduleRefresh(Action? refresh)
    {
        if (refresh is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(350).ConfigureAwait(false);
            refresh();
        });
    }
}
