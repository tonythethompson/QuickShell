using System.Runtime.InteropServices;
using System.Threading;

namespace QuickShell.Interop;

/// <summary>
/// Win32 clipboard text read/write via user32/kernel32 P/Invoke. Replaces
/// System.Windows.Forms.Clipboard so the app no longer forces UseWindowsForms.
/// Behaves like WinForms Clipboard.SetText/GetText — the OS owns the CF_UNICODETEXT
/// data after SetClipboardData, so it persists after the calling thread exits (no
/// WinRT-style Flush needed). Callers already marshal to an STA thread, which the
/// clipboard API requires.
/// </summary>
internal static partial class Win32Clipboard
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int OpenRetryCount = 10;
    private const int OpenRetryDelayMs = 50;

    /// <summary>
    /// Reads Unicode text from the Windows clipboard.
    /// </summary>
    /// <returns>The clipboard text, or <c>null</c> if Unicode text is unavailable or cannot be read.</returns>
    public static string? GetText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
        {
            return null;
        }

        if (!TryOpenClipboard())
        {
            return null;
        }

        try
        {
            nint handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0)
            {
                return null;
            }

            nint locked = GlobalLock(handle);
            if (locked == 0)
            {
                return null;
            }

            try
            {
                // Bound the read: clipboard memory is owned by another process and may omit
                // a terminator. GlobalSize is the allocated byte length of the HGLOBAL.
                nint byteLen = GlobalSize(handle);
                if (byteLen <= 0)
                {
                    return null;
                }

                int charCount = (int)(byteLen / sizeof(char));
                var text = Marshal.PtrToStringUni(locked, charCount);
                if (text is null)
                {
                    return null;
                }

                // CF_UNICODETEXT is a single null-terminated string; drop anything after the
                // first embedded '\0' (padding in the HGLOBAL), not trailing-only with TrimEnd.
                var terminator = text.IndexOf('\0');
                return terminator < 0 ? text : text[..terminator];
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Places Unicode text on the system clipboard.
    /// </summary>
    /// <param name="text">The text to place on the clipboard.</param>
    /// <returns><c>true</c> if the text is set successfully; <c>false</c> if the clipboard cannot be updated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <c>null</c>.</exception>
    public static bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!TryOpenClipboard())
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            // Include the terminating null. GlobalAlloc GMEM_MOVEABLE is required for
            // clipboard data; ownership transfers to the system on SetClipboardData.
            int bytes = (text.Length + 1) * sizeof(char);
            nint hGlobal = GlobalAlloc(GMEM_MOVEABLE, bytes);
            if (hGlobal == 0)
            {
                return false;
            }

            bool ownershipTransferred = false;
            try
            {
                nint target = GlobalLock(hGlobal);
                if (target == 0)
                {
                    return false;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                if (SetClipboardData(CF_UNICODETEXT, hGlobal) == 0)
                {
                    return false;
                }

                ownershipTransferred = true;
                return true;
            }
            finally
            {
                // Only free on failure; on success the system owns the memory.
                if (!ownershipTransferred)
                {
                    GlobalFree(hGlobal);
                }
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// WinForms Clipboard retried OpenClipboard briefly when another app held it.
    /// Match that so transient contention does not fail copy/paste.
    /// <summary>
    /// Attempts to open the clipboard, retrying after transient failures.
    /// </summary>
    /// <returns><c>true</c> if the clipboard is opened successfully; <c>false</c> if all attempts fail.</returns>
    private static bool TryOpenClipboard()
    {
        for (var attempt = 0; attempt < OpenRetryCount; attempt++)
        {
            if (OpenClipboard(0))
            {
                return true;
            }

            if (attempt + 1 < OpenRetryCount)
            {
                Thread.Sleep(OpenRetryDelayMs);
            }
        }

        return false;
    }

    /// <summary>
    /// Opens the clipboard for access by the specified window.
    /// </summary>
    /// <param name="hWndNewOwner">The handle of the window that owns the open clipboard, or zero for no owner.</param>
    /// <returns><c>true</c> if the clipboard is opened successfully; otherwise, <c>false</c>.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    /// <summary>
    /// Determines whether the specified clipboard format is available.
    /// </summary>
    /// <param name="format">The clipboard format to check.</param>
    /// <returns><c>true</c> if the format is available; otherwise, <c>false</c>.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    /// <summary>
    /// Retrieves clipboard data in the specified format.
    /// </summary>
    /// <param name="uFormat">The clipboard format to retrieve.</param>
    /// <returns>A handle to the clipboard data, or zero if the data is unavailable.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nint dwBytes);

    /// <summary>
    /// Releases globally allocated memory.
    /// </summary>
    /// <param name="hMem">A handle to the memory block to release.</param>
    /// <returns>A null handle if the memory is released successfully; otherwise, the handle remains valid.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);

    /// <summary>
    /// Locks a global memory object and returns a pointer to its memory.
    /// </summary>
    /// <param name="hMem">The handle of the global memory object to lock.</param>
    /// <returns>A pointer to the locked memory, or zero if the memory could not be locked.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    /// <summary>
    /// Releases the lock on a global memory object.
    /// </summary>
    /// <param name="hMem">A handle to the global memory object.</param>
    /// <returns><c>true</c> if the memory object was unlocked; otherwise, <c>false</c>.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);

    /// <summary>
    /// Retrieves the size of a global memory block in bytes.
    /// </summary>
    /// <param name="hMem">A handle to the global memory block.</param>
    /// <returns>The size of the memory block in bytes.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalSize(nint hMem);
}
