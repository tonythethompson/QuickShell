using QuickShell.Interop;

namespace QuickShell.Services;

internal static class FolderPickerService
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JoinGracePeriod = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Prompts the user to select a folder.
    /// </summary>
    /// <param name="initialDirectory">The directory to display initially, if specified.</param>
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
    /// Selects a folder using the specified owner window and initial directory.
    /// </summary>
    /// <param name="initialDirectory">The directory to display initially, optionally specified as a WSL path.</param>
    /// <param name="ownerHandle">The native handle of the dialog's owner window.</param>
    /// <returns>The selected folder path, or <c>null</c> if no folder is selected.</returns>
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
