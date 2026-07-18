using QuickShell.Models;

namespace QuickShell.Services;

internal static class TerminalLauncherArgs
{
    public static string EscapeWindowsTerminalArg(string value)
    {
        value = value.Replace("\"", "\\\"");

        // MSVC argv rules: an odd run of trailing backslashes escapes the closing quote.
        var trailingBackslashes = 0;
        for (var i = value.Length - 1; i >= 0 && value[i] == '\\'; i--)
        {
            trailingBackslashes++;
        }

        if (trailingBackslashes > 0)
        {
            value += new string('\\', trailingBackslashes);
        }

        return value;
    }

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

    /// <summary>
    /// Windows Terminal already receives <c>-d</c> for the workspace folder, so only run the command.
    /// </summary>
    public static string ToWindowsTerminalPowerShellSuffix(TerminalShortcut shortcut, string executable)
    {
        var command = shortcut.Command;
        if (string.IsNullOrWhiteSpace(command))
        {
            return executable;
        }

        return $"{executable} -NoExit -Command \"{EscapePowerShellInline(command)}\"";
    }

    public static string ToWindowsTerminalNushellSuffix(TerminalShortcut shortcut, string executable)
    {
        var command = shortcut.Command;
        if (string.IsNullOrWhiteSpace(command))
        {
            return executable;
        }

        return $"{executable} -c '{EscapeSingleQuotedNushell(command)}'";
    }

    public static string EscapeSingleQuotedNushell(string value) => value.Replace("'", "''");

    public static string ToPowerShellExecutableCommand(TerminalShortcut shortcut, string executable, string directory) =>
        $"{executable} {ToPowerShellArguments(shortcut, directory)}";

    public static bool IsPackageManagerCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var trimmed = command.Trim();
        var space = trimmed.IndexOf(' ');
        var tool = space < 0 ? trimmed : trimmed[..space];

        return tool.Equals("npm", StringComparison.OrdinalIgnoreCase)
            || tool.Equals("pnpm", StringComparison.OrdinalIgnoreCase)
            || tool.Equals("yarn", StringComparison.OrdinalIgnoreCase)
            || tool.Equals("npx", StringComparison.OrdinalIgnoreCase)
            || tool.Equals("bun", StringComparison.OrdinalIgnoreCase)
            || tool.Equals("bunx", StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryExtractExecutableFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        if (expanded.StartsWith('"'))
        {
            var endQuote = expanded.IndexOf('"', 1);
            return endQuote > 1 ? expanded[1..endQuote] : null;
        }

        var space = expanded.IndexOf(' ');
        return space < 0 ? expanded : expanded[..space];
    }

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

    public static string BuildWindowsTerminalCmdSuffix(TerminalShortcut shortcut, bool omitDirectoryChange = false)
    {
        var command = shortcut.Command;

        if (omitDirectoryChange)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return "cmd.exe";
            }

            return $"cmd.exe /k \"{EscapeCmd(command)}\"";
        }

        var arguments = $"/k \"cd /d \"\"{EscapeCmd(shortcut.Directory)}\"\"";

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            arguments += $" && {EscapeCmd(shortcut.Command)}";
        }

        arguments += '"';
        return $"cmd.exe {arguments}";
    }
}
