using System.Diagnostics;
using TerminalShortcuts.Models;

namespace TerminalShortcuts.Services;

internal static class TerminalLauncher
{
    public static void Open(TerminalShortcut shortcut)
    {
        if (!Directory.Exists(shortcut.Directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {shortcut.Directory}");
        }

        var terminal = shortcut.Terminal?.Trim().ToLowerInvariant() ?? "wt";
        var startInfo = terminal switch
        {
            "wt" or "windows-terminal" => CreateWindowsTerminalStartInfo(shortcut),
            "powershell" or "pwsh" => CreatePowerShellStartInfo(shortcut, usePwsh: false),
            "pwsh" or "powershell7" => CreatePowerShellStartInfo(shortcut, usePwsh: true),
            "cmd" => CreateCmdStartInfo(shortcut),
            _ => CreateWindowsTerminalStartInfo(shortcut),
        };

        Process.Start(startInfo);
    }

    private static ProcessStartInfo CreateWindowsTerminalStartInfo(TerminalShortcut shortcut)
    {
        var arguments = $"-d \"{shortcut.Directory}\"";

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            var command = EscapeForCmd(shortcut.Command);
            arguments += $" cmd /k \"{command}\"";
        }

        return new ProcessStartInfo
        {
            FileName = "wt.exe",
            Arguments = arguments,
            UseShellExecute = true,
        };
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(TerminalShortcut shortcut, bool usePwsh)
    {
        var fileName = usePwsh ? "pwsh.exe" : "powershell.exe";
        var arguments = $"-NoExit -Command \"Set-Location -LiteralPath '{EscapeForSingleQuotedPowerShell(shortcut.Directory)}'";

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            arguments += $"; {shortcut.Command}";
        }

        arguments += '"';

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
        };
    }

    private static ProcessStartInfo CreateCmdStartInfo(TerminalShortcut shortcut)
    {
        var arguments = $"/k \"cd /d \"{shortcut.Directory}\"";

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            arguments += $" && {shortcut.Command}";
        }

        arguments += '"';

        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = arguments,
            UseShellExecute = true,
        };
    }

    private static string EscapeForCmd(string value) => value.Replace("\"", "\"\"");

    private static string EscapeForSingleQuotedPowerShell(string value) => value.Replace("'", "''");
}
