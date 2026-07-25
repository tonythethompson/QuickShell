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
                return Marshal.PtrToStringUni(locked, charCount)?.TrimEnd('\0');
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
    /// </summary>
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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalSize(nint hMem);
}
