using System.IO;
using System.Threading;
using QuickShell.Interop;

namespace QuickShell.Services;

internal static class FolderPickerService
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JoinGracePeriod = TimeSpan.FromSeconds(5);

    public static string? PickFolder(string? initialDirectory = null)
    {
        var ownerHandle = NativeForegroundWindow.Get();

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickFolderOnStaThread(initialDirectory, ownerHandle);
        }

        string? selected = null;
        var nativeThreadId = 0;
        var thread = new Thread(() =>
        {
            nativeThreadId = StaDialogCloser.CurrentNativeThreadId();
            selected = PickFolderOnStaThread(initialDirectory, ownerHandle);
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // The modal IFileDialog blocks the STA thread until dismissed. DialogTimeout +
        // JoinGracePeriod bounds a stuck dialog; on timeout, post WM_CLOSE only to a
        // dialog owned by that STA thread (not an arbitrary foreground window).
        if (thread.Join(DialogTimeout + JoinGracePeriod))
        {
            return selected;
        }

        StaDialogCloser.TryCloseThreadDialog(Volatile.Read(ref nativeThreadId), ownerHandle);
        return thread.Join(JoinGracePeriod) ? selected : null;
    }

    private static string? PickFolderOnStaThread(string? initialDirectory, nint ownerHandle)
    {
        var initial = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? initialDirectory
            : null;

        return ShellFileDialog.PickFolder(ownerHandle, initial);
    }
}
