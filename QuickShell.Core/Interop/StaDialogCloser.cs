using System.Runtime.InteropServices;

namespace QuickShell.Interop;

/// <summary>
/// Closes a modal common-dialog window owned by a known native STA thread.
/// Prefer the dialog class <c>#32770</c>; fall back to the first visible top-level
/// window on that thread that is not the owner. Avoids posting WM_CLOSE to an
/// arbitrary foreground window (which may belong to another app).
/// </summary>
internal static class StaDialogCloser
{
    private const int WM_CLOSE = 0x0010;
    private const uint GA_ROOT = 2;

    /// <summary>
    /// Requests closure of a visible top-level dialog owned by the specified native thread.
    /// </summary>
    /// <param name="nativeThreadId">The native thread identifier whose dialog windows are searched.</param>
    /// <param name="ownerHandle">The window handle to exclude from consideration.</param>
    public static void TryCloseThreadDialog(int nativeThreadId, nint ownerHandle)
    {
        if (nativeThreadId == 0)
        {
            return;
        }

        nint dialog = 0;
        nint fallback = 0;
        // codeql[cs/call-to-unmanaged-code]: Enumerate picker STA thread windows to find the modal dialog HWND.
        EnumThreadWindows(
            nativeThreadId,
            (hwnd, _) =>
            {
                // codeql[cs/call-to-unmanaged-code]: Skip invisible windows when locating the dialog.
                if (hwnd == 0 || hwnd == ownerHandle || !IsWindowVisible(hwnd))
                {
                    return true;
                }

                // Top-level only (skip DirectUI / child chrome inside the dialog).
                // codeql[cs/call-to-unmanaged-code]: Restrict close target to top-level HWNDs on the STA thread.
                if (GetAncestor(hwnd, GA_ROOT) != hwnd)
                {
                    return true;
                }

                if (IsDialogClass(hwnd))
                {
                    dialog = hwnd;
                    return false;
                }

                if (fallback == 0)
                {
                    fallback = hwnd;
                }

                return true;
            },
            0);

        var target = dialog != 0 ? dialog : fallback;
        if (target != 0)
        {
            // codeql[cs/call-to-unmanaged-code]: Unblock a timed-out IFileDialog by closing its HWND.
            PostMessage(target, WM_CLOSE, nint.Zero, nint.Zero);
        }
    }

    /// <summary>
    /// Gets the native identifier of the current thread.
    /// </summary>
    /// <returns>The native thread identifier.</returns>
    public static int CurrentNativeThreadId()
    {
        // codeql[cs/call-to-unmanaged-code]: Native thread id required for EnumThreadWindows on the picker STA thread.
        return GetCurrentThreadId();
    }

    /// <summary>
    /// Determines whether a window uses the standard dialog window class.
    /// </summary>
    /// <param name="hwnd">The window handle to inspect.</param>
    /// <returns><c>true</c> if the window class is <c>#32770</c>; <c>false</c> otherwise.</returns>
    private static unsafe bool IsDialogClass(nint hwnd)
    {
        Span<char> buffer = stackalloc char[64];
        int length;
        fixed (char* p = buffer)
        {
            // codeql[cs/call-to-unmanaged-code]: Classify #32770 dialog HWNDs for timeout close.
            length = GetClassNameW(hwnd, p, buffer.Length);
        }

        return length > 0 && buffer[..length].SequenceEqual("#32770");
    }

    private delegate bool EnumThreadWndProc(nint hWnd, nint lParam);

    /// <summary>
    /// Enumerates the top-level windows associated with a thread.
    /// </summary>
    /// <param name="dwThreadId">The identifier of the thread whose windows are enumerated.</param>
    /// <param name="lpfn">The callback invoked for each enumerated window.</param>
    /// <param name="lParam">Application-defined data passed to the callback.</param>
    /// <returns><c>true</c> if the enumeration was initiated successfully; otherwise, <c>false</c>.</returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(int dwThreadId, EnumThreadWndProc lpfn, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hWnd, uint gaFlags);

    /// <summary>
    /// Retrieves the class name of the specified window.
    /// </summary>
    /// <param name="hWnd">The handle of the window.</param>
    /// <param name="lpClassName">The buffer that receives the window class name.</param>
    /// <param name="nMaxCount">The maximum number of characters to copy to the buffer.</param>
    /// <returns>The number of characters copied to the buffer, excluding the terminating null character.</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern unsafe int GetClassNameW(nint hWnd, char* lpClassName, int nMaxCount);

    /// <summary>
    /// Posts a message to the specified window.
    /// </summary>
    /// <param name="hWnd">The handle of the window that receives the message.</param>
    /// <param name="msg">The message to post.</param>
    /// <param name="wParam">Additional message information.</param>
    /// <param name="lParam">Additional message information.</param>
    /// <returns><c>true</c> if the message was posted successfully; otherwise, <c>false</c>.</returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();
}
