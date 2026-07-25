using System.Runtime.InteropServices;

namespace QuickShell.Interop;

/// <summary>
/// Owner HWND for modal shell dialogs. Win32 is intentional: after dropping WinForms
/// there is no BCL/WinRT equivalent that works for both the packaged CmdPal host and
/// the PowerToys Run plugin.
/// </summary>
internal static partial class NativeForegroundWindow
{
    /// <summary>
    /// Gets the handle of the current foreground window.
    /// </summary>
    /// <returns>The handle of the current foreground window.</returns>
    public static nint Get()
    {
        // codeql[cs/call-to-unmanaged-code]: Required for IFileDialog owner hwnd; no managed API after WinForms removal.
        return GetForegroundWindow();
    }

    /// <summary>
    /// Retrieves the handle of the current foreground window.
    /// </summary>
    /// <returns>The handle of the foreground window, or zero if no foreground window exists.</returns>
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
