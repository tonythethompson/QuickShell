using System.Runtime.InteropServices;

namespace QuickShell.Interop;

/// <summary>
/// Owner HWND for modal shell dialogs. Win32 is intentional: after dropping WinForms
/// there is no BCL/WinRT equivalent that works for both the packaged CmdPal host and
/// the PowerToys Run plugin.
/// </summary>
internal static partial class NativeForegroundWindow
{
    public static nint Get()
    {
        // codeql[cs/call-to-unmanaged-code]: Required for IFileDialog owner hwnd; no managed API after WinForms removal.
        return GetForegroundWindow();
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
