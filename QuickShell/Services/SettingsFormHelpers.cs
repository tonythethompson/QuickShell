using System.Runtime.InteropServices;
using System.Threading.Tasks;



namespace QuickShell.Services;



internal static class SettingsFormHelpers

{

    private const int DefaultRefreshDelayMs = 50;



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

            try

            {

                refresh();

            }

            catch (Exception ex) when (ex is ObjectDisposedException or COMException)

            {

                // Best effort; the settings page/COM host may have been torn down

                // before this fired. Anything else is a real bug and should surface.

            }

        });

    }

}


