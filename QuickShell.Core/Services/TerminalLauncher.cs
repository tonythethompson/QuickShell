using QuickShell.Models;
using System.Diagnostics;

namespace QuickShell.Services;

internal static class TerminalLauncher
{
    public static void Open(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        bool runAsAdmin = false,
        bool runAsStandard = false)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(shortcut.Directory, out var directory, out var error))
        {
            throw new InvalidOperationException(error);
        }

        if (!ShortcutValidation.DirectoryExists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        if (!ShortcutValidation.TryValidateCommand(shortcut.Command, out error))
        {
            throw new InvalidOperationException(error);
        }

        var launchShortcut = new TerminalShortcut
        {
            Name = shortcut.Name,
            Abbreviation = shortcut.Abbreviation,
            Directory = directory,
            Command = shortcut.Command,
            Terminal = shortcut.Terminal,
            WtProfile = shortcut.WtProfile,
            RunAsAdmin = shortcut.RunAsAdmin,
            IsPinned = shortcut.IsPinned,
            PinOrder = shortcut.PinOrder,
            LastUsedUtc = shortcut.LastUsedUtc,
        };

        var target = TerminalCatalog.ResolveForShortcut(launchShortcut, terminalApplicationId, defaultProfileId);
        var startInfo = target.Kind switch
        {
            LaunchTargetKind.WindowsTerminal or LaunchTargetKind.IntelligentTerminal =>
                CreateWindowsTerminalStartInfo(launchShortcut, target),
            LaunchTargetKind.PowerShell => CreatePowerShellStartInfo(launchShortcut, usePwsh: false),
            LaunchTargetKind.Pwsh => CreatePowerShellStartInfo(launchShortcut, usePwsh: true),
            LaunchTargetKind.Cmd => CreateCmdStartInfo(launchShortcut, target),
            LaunchTargetKind.Wsl => CreateWslStartInfo(launchShortcut, target),
            _ => CreateWindowsTerminalStartInfo(launchShortcut, target),
        };

        if (!runAsStandard && (runAsAdmin || shortcut.RunAsAdmin))
        {
            startInfo.Verb = "runas";
        }

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");
        }
    }

    private static ProcessStartInfo CreateWindowsTerminalStartInfo(TerminalShortcut shortcut, LaunchTarget target)
    {
        if (WslPathResolver.TryParse(shortcut.Directory, out var wslLocation))
        {
            return CreateWindowsTerminalForWslDirectory(shortcut, target, wslLocation);
        }

        var arguments = new List<string>();

        if (!string.IsNullOrWhiteSpace(target.ProfileOrDistro))
        {
            arguments.Add($"-p \"{TerminalLauncherArgs.EscapeWindowsTerminalArg(target.ProfileOrDistro)}\"");
        }

        if (!IsWslProfile(target))
        {
            arguments.Add($"-d \"{TerminalLauncherArgs.EscapeWindowsTerminalArg(shortcut.Directory)}\"");
        }

        if (!string.IsNullOrWhiteSpace(shortcut.Command) || IsWslProfile(target))
        {
            arguments.Add(BuildWindowsTerminalCommandSuffix(shortcut, target));
        }

        return CreateWtStartInfo(arguments, target.HostExecutable);
    }

    private static ProcessStartInfo CreateWindowsTerminalForWslDirectory(
        TerminalShortcut shortcut,
        LaunchTarget target,
        WslPathResolver.WslLocation wslLocation)
    {
        var arguments = new List<string>();

        if (!string.IsNullOrWhiteSpace(target.ProfileOrDistro))
        {
            arguments.Add($"-p \"{TerminalLauncherArgs.EscapeWindowsTerminalArg(target.ProfileOrDistro)}\"");
        }

        if (IsWslProfile(target))
        {
            arguments.Add(TerminalLauncherArgs.ToWslExecutableCommand(shortcut, target, wslLocation));
            return CreateWtStartInfo(arguments, target.HostExecutable);
        }

        if (IsPowerShellProfile(target))
        {
            var directory = wslLocation.UncPath ?? shortcut.Directory;
            arguments.Add(TerminalLauncherArgs.ToPowerShellExecutableCommand(shortcut, GetPowerShellPathForProfile(target), directory));
            return CreateWtStartInfo(arguments, target.HostExecutable);
        }

        arguments.Add(ToWslExecutableCommand(shortcut, target, wslLocation));
        return CreateWtStartInfo(arguments, target.HostExecutable);
    }

    private static string BuildWindowsTerminalCommandSuffix(TerminalShortcut shortcut, LaunchTarget target)
    {
        var command = shortcut.Command?.Trim();

        if (WslPathResolver.TryParse(shortcut.Directory, out var wslLocation))
        {
            return TerminalLauncherArgs.ToWslExecutableCommand(shortcut, target, wslLocation, interactiveShell: string.IsNullOrWhiteSpace(command));
        }

        if (IsWslProfile(target))
        {
            return ToWslExecutableCommand(shortcut, target, CreateLocationFromWindowsPath(shortcut.Directory, target), interactiveShell: string.IsNullOrWhiteSpace(command));
        }

        var commandLine = target.WtCommandLine ?? string.Empty;

        if (commandLine.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            return TerminalLauncherArgs.ToPowerShellExecutableCommand(shortcut, "pwsh.exe", shortcut.Directory);
        }

        if (commandLine.Contains("powershell", StringComparison.OrdinalIgnoreCase))
        {
            return TerminalLauncherArgs.ToPowerShellExecutableCommand(shortcut, "powershell.exe", shortcut.Directory);
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        return TerminalLauncherArgs.BuildWindowsTerminalCmdSuffix(shortcut);
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(TerminalShortcut shortcut, bool usePwsh)
    {
        var fileName = usePwsh ? "pwsh.exe" : "powershell.exe";
        var directory = ResolveDirectoryForPowerShell(shortcut.Directory);

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = TerminalLauncherArgs.ToPowerShellArguments(shortcut, directory),
            UseShellExecute = true,
        };
    }

    private static ProcessStartInfo CreateCmdStartInfo(TerminalShortcut shortcut, LaunchTarget target)
    {
        if (WslPathResolver.TryParse(shortcut.Directory, out var wslLocation))
        {
            return CreateWslProcessStartInfo(shortcut, target, wslLocation);
        }

        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = TerminalLauncherArgs.BuildCmdArguments(shortcut),
            UseShellExecute = true,
        };
    }

    private static ProcessStartInfo CreateWslStartInfo(TerminalShortcut shortcut, LaunchTarget target)
    {
        if (WslPathResolver.TryParse(shortcut.Directory, out var wslLocation))
        {
            return CreateWslProcessStartInfo(shortcut, target, wslLocation);
        }

        return CreateWslProcessStartInfo(shortcut, target, CreateLocationFromWindowsPath(shortcut.Directory, target));
    }

    private static ProcessStartInfo CreateWslProcessStartInfo(
        TerminalShortcut shortcut,
        LaunchTarget target,
        WslPathResolver.WslLocation wslLocation) =>
        new()
        {
            FileName = "wsl.exe",
            Arguments = TerminalLauncherArgs.ToWslArguments(shortcut, target, wslLocation),
            UseShellExecute = true,
        };

    private static string ToWslExecutableCommand(
        TerminalShortcut shortcut,
        LaunchTarget target,
        WslPathResolver.WslLocation wslLocation,
        bool interactiveShell = false) =>
        TerminalLauncherArgs.ToWslExecutableCommand(shortcut, target, wslLocation, interactiveShell);

    private static WslPathResolver.WslLocation CreateLocationFromWindowsPath(string directory, LaunchTarget target) =>
        new()
        {
            LinuxPath = directory,
            Distro = target.ProfileOrDistro,
        };

    private static string ResolveDirectoryForPowerShell(string directory)
    {
        if (WslPathResolver.TryParse(directory, out var wslLocation) && !string.IsNullOrWhiteSpace(wslLocation.UncPath))
        {
            return wslLocation.UncPath;
        }

        return directory;
    }

    private static ProcessStartInfo CreateWtStartInfo(IEnumerable<string> arguments, string hostExecutable) =>
        new()
        {
            FileName = hostExecutable,
            Arguments = string.Join(' ', arguments.Where(arg => !string.IsNullOrWhiteSpace(arg))),
            UseShellExecute = true,
        };

    private static bool IsWslProfile(LaunchTarget target)
    {
        if (target.Kind == LaunchTargetKind.Wsl)
        {
            return true;
        }

        var commandLine = target.WtCommandLine ?? string.Empty;
        return commandLine.Contains("wsl.exe", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("wslhost.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerShellProfile(LaunchTarget target)
    {
        if (target.Kind is LaunchTargetKind.PowerShell or LaunchTargetKind.Pwsh)
        {
            return true;
        }

        var commandLine = target.WtCommandLine ?? string.Empty;
        return commandLine.Contains("pwsh", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("powershell", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPowerShellPathForProfile(LaunchTarget target) =>
        (target.WtCommandLine ?? string.Empty).Contains("pwsh", StringComparison.OrdinalIgnoreCase)
            || target.Kind == LaunchTargetKind.Pwsh
            ? "pwsh.exe"
            : "powershell.exe";
}
