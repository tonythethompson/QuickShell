using Microsoft.Win32;
using QuickShell.Services;
using System.IO;
using System.Windows;

namespace QuickShell.Run;

internal static class RunFileDialogs
{
    public static bool TryExportShortcuts(IShortcutRepository shortcuts, Window? owner, out string message)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Quick Shell shortcuts",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "quickshell-shortcuts.json",
            AddExtension = true,
            DefaultExt = ".json",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(owner) != true)
        {
            message = string.Empty;
            return false;
        }

        if (shortcuts.TryExportToFile(dialog.FileName, out var error))
        {
            message = $"Exported shortcuts to {dialog.FileName}.";
            return true;
        }

        message = error;
        return false;
    }

    public static bool TryImportShortcuts(
        IShortcutRepository shortcuts,
        Window? owner,
        bool replace,
        out string message)
    {
        var dialog = new OpenFileDialog
        {
            Title = replace ? "Replace all Quick Shell shortcuts" : "Import Quick Shell shortcuts",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(owner) != true)
        {
            message = string.Empty;
            return false;
        }

        var result = replace
            ? shortcuts.ImportReplace(dialog.FileName)
            : shortcuts.ImportMerge(dialog.FileName);

        message = result.Message;
        return result.Success;
    }

    public static bool TryPickExecutable(Window? owner, out string path)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose companion app",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(owner) != true)
        {
            path = string.Empty;
            return false;
        }

        path = dialog.FileName;
        return true;
    }

    public static bool OpenPathInEditor(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        }) is not null;
    }

    public static bool OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        }) is not null;
    }
}
