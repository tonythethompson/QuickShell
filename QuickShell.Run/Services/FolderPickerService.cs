using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using QuickShell.Interop;

namespace QuickShell.Services;

internal static class FolderPickerService
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JoinGracePeriod = TimeSpan.FromSeconds(5);

    public static string? PickFolder(string? initialDirectory = null)
    {
        var ownerHandle = GetForegroundWindow();

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickFolderOnStaThread(initialDirectory, ownerHandle);
        }

        string? selected = null;
        var thread = new Thread(() => selected = PickFolderOnStaThread(initialDirectory, ownerHandle))
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // The modal IFileDialog blocks the STA thread until dismissed. DialogTimeout +
        // JoinGracePeriod bounds a stuck dialog; on timeout, post WM_CLOSE to the foreground
        // window (the modal dialog is topmost) so the thread unblocks and the caller never hangs.
        if (thread.Join(DialogTimeout + JoinGracePeriod))
        {
            return selected;
        }

        TryCloseForegroundDialog(ownerHandle);
        return thread.Join(JoinGracePeriod) ? selected : null;
    }

    private static string? PickFolderOnStaThread(string? initialDirectory, nint ownerHandle)
    {
        var initial = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? initialDirectory
            : null;

        return ShellFileDialog.PickFolder(ownerHandle, initial);
    }

    private static void TryCloseForegroundDialog(nint ownerHandle)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || hwnd == ownerHandle)
        {
            return;
        }

        PostMessage(hwnd, WM_CLOSE, nint.Zero, nint.Zero);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private const int WM_CLOSE = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);
}
