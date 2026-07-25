using System.Runtime.InteropServices;
using System.Threading;
using QuickShell.Interop;

namespace QuickShell.Services;

/// <summary>
/// Runs a modal shell dialog on an STA thread with timeout recovery. Publishes the
/// native thread id with <see cref="Volatile"/> so timeout close can find the dialog,
/// and maps known picker failures to a null result so they never tear down the process.
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
            return TryInvoke(action);
        }

        string? selected = null;
        Exception? fault = null;
        var nativeThreadId = 0;
        var thread = new Thread(() =>
        {
            // Must be the Win32 thread id for EnumThreadWindows — not Environment.CurrentManagedThreadId.
            Volatile.Write(ref nativeThreadId, StaDialogCloser.CurrentNativeThreadId());
            try
            {
                selected = action();
            }
            catch (Exception ex) when (IsPickerFailure(ex))
            {
                fault = ex;
            }
            // codeql[cs/catch-of-all-exceptions] Background STA must not crash the host; any other failure is a canceled pick.
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

    private static string? TryInvoke(Func<string?> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (IsPickerFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// COM/interop and allocation failures from IFileDialog / clipboard paths.
    /// Unexpected exceptions still propagate so real bugs are not swallowed.
    /// </summary>
    private static bool IsPickerFailure(Exception ex) =>
        ex is COMException
            or ExternalException
            or SEHException
            or InvalidOperationException
            or ArgumentException
            or OutOfMemoryException
            or NotSupportedException
            or UnauthorizedAccessException;
}
