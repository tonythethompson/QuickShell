using QuickShell.Models;

namespace QuickShell.Services;

internal static class TerminalLauncherArgs
{
    public static string EscapeWindowsTerminalArg(string value) => value.Replace("\"", "\\\"");

    public static string EscapeCmd(string value) => value.Replace("\"", "\"\"");

    public static string EscapeSingleQuotedPowerShell(string value) => value.Replace("'", "''");

    public static string EscapePowerShellInline(string value) =>
        value.Replace("`", "``", StringComparison.Ordinal).Replace("\"", "`\"", StringComparison.Ordinal);

    public static string EscapeBash(string value) => value.Replace("\"", "\\\"");

    public static string ToPowerShellArguments(TerminalShortcut shortcut, string directory)
    {
        var arguments = $"-NoExit -Command \"Set-Location -LiteralPath '{EscapeSingleQuotedPowerShell(directory)}'";

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            arguments += $"; {EscapePowerShellInline(shortcut.Command)}";
        }

        arguments += '"';
        return arguments;
    }

    public static string ToPowerShellExecutableCommand(TerminalShortcut shortcut, string executable, string directory) =>
        $"{executable} {ToPowerShellArguments(shortcut, directory)}";

    public static string ToWslArguments(
        TerminalShortcut shortcut,
        LaunchTarget target,
        WslPathResolver.WslLocation wslLocation,
        bool interactiveShell = false)
    {
        var distro = WslPathResolver.ResolveDistro(wslLocation, target);
        var arguments = $"-d \"{EscapeWindowsTerminalArg(distro)}\" --cd \"{EscapeWindowsTerminalArg(wslLocation.LinuxPath)}\"";

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            arguments += $" -e bash -lc \"{EscapeBash(shortcut.Command)}\"";
        }
        else if (interactiveShell)
        {
            arguments += " -e bash";
        }

        return arguments;
    }

    public static string ToWslExecutableCommand(
        TerminalShortcut shortcut,
        LaunchTarget target,
        WslPathResolver.WslLocation wslLocation,
        bool interactiveShell = false)
    {
        var args = ToWslArguments(shortcut, target, wslLocation, interactiveShell);
        return $"wsl.exe {args}";
    }

    public static string BuildCmdArguments(TerminalShortcut shortcut)
    {
        var arguments = $"/k \"cd /d \"\"{EscapeCmd(shortcut.Directory)}\"\"";

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            arguments += $" && {EscapeCmd(shortcut.Command)}";
        }

        arguments += '"';
        return arguments;
    }

    public static string BuildWindowsTerminalCmdSuffix(TerminalShortcut shortcut)
    {
        var arguments = $"/k \"cd /d \"\"{EscapeCmd(shortcut.Directory)}\"\"";

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            arguments += $" && {EscapeCmd(shortcut.Command)}";
        }

        arguments += '"';
        return $"cmd.exe {arguments}";
    }
}
