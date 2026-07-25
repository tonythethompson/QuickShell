using QuickShell.Interop;

namespace QuickShell.Services;

internal static class FolderPickerService
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JoinGracePeriod = TimeSpan.FromSeconds(5);

    public static string? PickFolder(string? initialDirectory = null)
    {
        var ownerHandle = NativeForegroundWindow.Get();
        return StaModalDialogRunner.Run(
            ownerHandle,
            () => PickFolderOnStaThread(initialDirectory, ownerHandle),
            DialogTimeout,
            JoinGracePeriod);
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
