using System.Threading;
using QuickShell.Interop;

namespace QuickShell.Services;

/// <summary>
/// Runs a modal shell dialog on an STA thread with timeout recovery. Publishes the
/// native thread id with <see cref="Volatile"/> so timeout close can find the dialog,
/// and swallows worker exceptions into a null result so they never tear down the process.
/// </summary>
internal static class StaModalDialogRunner
{
    /// <summary>
    /// Executes an action in an STA context with timeout-based dialog recovery.
    /// </summary>
    /// <param name="ownerHandle">The native handle of the dialog owner.</param>
    /// <param name="action">The action to execute.</param>
    /// <param name="dialogTimeout">The initial time allowed for the action to complete.</param>
    /// <param name="joinGracePeriod">The additional time allowed for completion and recovery.</param>
    /// <returns>The action's selected value, or <see langword="null"/> if the action fails or does not complete within the recovery period.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public static string? Run(
        nint ownerHandle,
        Func<string?> action,
        TimeSpan dialogTimeout,
        TimeSpan joinGracePeriod)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            try
            {
                return action();
            }
            catch (Exception)
            {
                return null;
            }
        }

        string? selected = null;
        Exception? fault = null;
        var nativeThreadId = 0;
        var thread = new Thread(() =>
        {
            Volatile.Write(ref nativeThreadId, StaDialogCloser.CurrentNativeThreadId());
            try
            {
                selected = action();
            }
            catch (Exception ex)
            {
                fault = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (thread.Join(dialogTimeout + joinGracePeriod))
        {
            return fault is null ? selected : null;
        }

        StaDialogCloser.TryCloseThreadDialog(Volatile.Read(ref nativeThreadId), ownerHandle);
        if (!thread.Join(joinGracePeriod))
        {
            return null;
        }

        return fault is null ? selected : null;
    }
}
