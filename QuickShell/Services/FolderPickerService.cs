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

        // Happy path: the modal IFileDialog blocks the STA thread until the user dismisses it,
        // then the thread exits. DialogTimeout + JoinGracePeriod bounds a stuck dialog; on
        // timeout, post WM_CLOSE only to a dialog owned by that STA thread.
        if (thread.Join(DialogTimeout + JoinGracePeriod))
        {
            return selected;
        }

        StaDialogCloser.TryCloseThreadDialog(Volatile.Read(ref nativeThreadId), ownerHandle);
        return thread.Join(JoinGracePeriod) ? selected : null;
    }

    private static string? PickFolderOnStaThread(string? initialDirectory, nint ownerHandle)
    {
        var initial = initialDirectory;
        if (!string.IsNullOrWhiteSpace(initialDirectory)
            && WslPathResolver.TryParse(initialDirectory, out var wsl)
            && !string.IsNullOrWhiteSpace(wsl.UncPath))
        {
            initial = wsl.UncPath;
        }

        return ShellFileDialog.PickFolder(ownerHandle, initial);
    }
}
