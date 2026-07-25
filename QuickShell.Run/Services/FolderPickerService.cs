using System.IO;
using QuickShell.Interop;

namespace QuickShell.Services;

internal static class FolderPickerService
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JoinGracePeriod = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Displays a folder-picker dialog and obtains the selected folder path.
    /// </summary>
    /// <param name="initialDirectory">The directory to display initially, if it exists.</param>
    /// <returns>The selected folder path, or <c>null</c> if no folder is selected.</returns>
    public static string? PickFolder(string? initialDirectory = null)
    {
        var ownerHandle = NativeForegroundWindow.Get();
        return StaModalDialogRunner.Run(
            ownerHandle,
            () => PickFolderOnStaThread(initialDirectory, ownerHandle),
            DialogTimeout,
            JoinGracePeriod);
    }

    /// <summary>
    /// Displays a folder picker using the specified owner window and initial directory.
    /// </summary>
    /// <param name="initialDirectory">The directory to show initially when it exists; otherwise, no initial directory is used.</param>
    /// <param name="ownerHandle">The native handle of the window that owns the folder picker.</param>
    /// <returns>The selected folder path, or <c>null</c> if no folder is selected.</returns>
    private static string? PickFolderOnStaThread(string? initialDirectory, nint ownerHandle)
    {
        var initial = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? initialDirectory
            : null;

        return ShellFileDialog.PickFolder(ownerHandle, initial);
    }
}
