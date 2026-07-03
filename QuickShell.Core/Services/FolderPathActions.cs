using System.Diagnostics;

namespace QuickShell.Services;

internal static class FolderPathActions
{
    public static bool TryOpenInExplorer(string directory, out string error)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory, out var normalized, out error))
        {
            return false;
        }

        if (!ShortcutValidation.DirectoryExists(normalized))
        {
            error = $"Folder not found: {normalized}";
            return false;
        }

        if (Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{normalized}\"",
                UseShellExecute = true,
            }) is null)
        {
            error = "Failed to open File Explorer.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryCopyPath(string directory, out string error)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory, out var normalized, out error))
        {
            return false;
        }

        if (!StaClipboard.TrySetText(normalized))
        {
            error = "Failed to copy path to clipboard.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
