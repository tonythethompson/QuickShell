using QuickShell.Abstractions;
using QuickShell.Models;
using System.Diagnostics;

namespace QuickShell.Services;

internal readonly record struct ResolvedLaunch(TerminalShortcut Shortcut, LaunchTarget Target);

internal sealed record TerminalLaunchAttempt(
    string HostExecutable,
    string Arguments,
    string TargetDisplayName,
    string? ProfileOrDistro,
    bool RunAsAdmin,
    string? FallbackReason);

internal sealed class TerminalLauncher : ITerminalLauncher
{
    private readonly IProcessStarter _processStarter;

    public TerminalLauncher(IProcessStarter processStarter)
    {
        ArgumentNullException.ThrowIfNull(processStarter);
        _processStarter = processStarter;
    }

    public ResolvedLaunch Resolve(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId)
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
        return new ResolvedLaunch(launchShortcut, target);
    }

    public TerminalLaunchAttempt Open(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        bool runAsAdmin = false,
        bool runAsStandard = false)
    {
        var resolved = Resolve(shortcut, terminalApplicationId, defaultProfileId);
        var effectiveElevation = !runAsStandard && (runAsAdmin || shortcut.RunAsAdmin);
        return OpenResolved(resolved, effectiveElevation);
    }

    public TerminalLaunchAttempt OpenResolved(ResolvedLaunch resolved, bool effectiveElevation)
    {
        var startInfo = BuildStartInfo(resolved.Shortcut, resolved.Target);

        if (effectiveElevation)
        {
            startInfo.Verb = "runas";
        }

        StartProcess(startInfo);
        return ToAttempt(resolved, startInfo);
    }

    /// <summary>
    /// Launches multiple entries as tabs of a single Windows Terminal window/process.
    /// Elevation applies to the whole window, so callers must group entries by matching elevation first.
    /// </summary>
    public IReadOnlyList<TerminalLaunchAttempt> OpenGroup(
        IReadOnlyList<ResolvedLaunch> group,
        bool effectiveElevation,
        string? hostExecutableOverride = null)
    {
        if (group is not { Count: > 0 })
        {
            throw new ArgumentException("Group must contain at least one resolved launch.", nameof(group));
        }

        var hostExecutable = string.IsNullOrWhiteSpace(hostExecutableOverride)
            ? group[0].Target.HostExecutable
            : hostExecutableOverride;
        if (string.IsNullOrWhiteSpace(hostExecutable))
        {
            throw new ArgumentException("Resolved launch target has no host executable.", nameof(group));
        }

        var allArguments = new List<string>();

        for (var i = 0; i < group.Count; i++)
        {
            var target = group[i].Target;
            if (!CanOpenAsWindowsTerminalTab(target, hostExecutable))
            {
                throw new ArgumentException(
                    "All entries in a group must be launchable as tabs in the same Windows Terminal host.",
                    nameof(group));
            }

            if (i > 0)
            {
                allArguments.Add(";");
                allArguments.Add("new-tab");
            }

            allArguments.AddRange(BuildWindowsTerminalTabArguments(group[i].Shortcut, target));
        }

        var startInfo = CreateWtStartInfo(allArguments, hostExecutable);
        if (effectiveElevation)
        {
            startInfo.Verb = "runas";
        }

        StartProcess(startInfo);
        return group.Select(resolved => ToAttempt(resolved, startInfo)).ToArray();
    }

    private static ProcessStartInfo BuildStartInfo(TerminalShortcut shortcut, LaunchTarget target) => target.Kind switch
    {
        LaunchTargetKind.WindowsTerminal or LaunchTargetKind.IntelligentTerminal =>
            CreateWtStartInfo(BuildWindowsTerminalArguments(shortcut, target), target.HostExecutable),
        LaunchTargetKind.PowerShell => CreatePowerShellStartInfo(shortcut, usePwsh: false),
        LaunchTargetKind.Pwsh => CreatePowerShellStartInfo(shortcut, usePwsh: true),
        LaunchTargetKind.Cmd => CreateCmdStartInfo(shortcut, target),
        LaunchTargetKind.Wsl => CreateWslStartInfo(shortcut, target),
        _ => CreateWtStartInfo(BuildWindowsTerminalArguments(shortcut, target), target.HostExecutable),
    };

    private static bool CanOpenAsWindowsTerminalTab(LaunchTarget target, string hostExecutable) =>
        target.Kind switch
        {
            LaunchTargetKind.WindowsTerminal or LaunchTargetKind.IntelligentTerminal =>
                string.Equals(target.HostExecutable, hostExecutable, StringComparison.OrdinalIgnoreCase),
            LaunchTargetKind.PowerShell or LaunchTargetKind.Pwsh or LaunchTargetKind.Cmd or LaunchTargetKind.Wsl => true,
            _ => false,
        };

    private void StartProcess(ProcessStartInfo startInfo)
    {
        if (!_processStarter.TryStart(startInfo))
        {
            throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");
        }
    }

    private static TerminalLaunchAttempt ToAttempt(ResolvedLaunch resolved, ProcessStartInfo startInfo) =>
        new(
            startInfo.FileName,
            startInfo.Arguments,
            resolved.Target.DisplayName,
            resolved.Target.ProfileOrDistro,
            string.Equals(startInfo.Verb, "runas", StringComparison.OrdinalIgnoreCase),
            resolved.Target.FallbackReason);

    private static List<string> BuildWindowsTerminalArguments(TerminalShortcut shortcut, LaunchTarget target)
    {
        if (WslPathResolver.TryParse(shortcut.Directory, out var wslLocation))
        {
            return BuildWindowsTerminalArgumentsForWslDirectory(shortcut, target, wslLocation);
        }

        var arguments = new List<string>();

        if (!string.IsNullOrWhiteSpace(target.ProfileOrDistro))
        {
            arguments.Add($"-p \"{TerminalLauncherArgs.EscapeWindowsTerminalArg(target.ProfileOrDistro)}\"");
        }

        var omitDirectoryChange = false;
        if (!IsWslProfile(target))
        {
            arguments.Add($"-d \"{TerminalLauncherArgs.EscapeWindowsTerminalArg(shortcut.Directory)}\"");
            omitDirectoryChange = true;
        }

        if (!string.IsNullOrWhiteSpace(shortcut.Command) || IsWslProfile(target))
        {
            arguments.Add(BuildWindowsTerminalCommandSuffix(shortcut, target, omitDirectoryChange));
        }

        return arguments;
    }

    private static List<string> BuildWindowsTerminalTabArguments(TerminalShortcut shortcut, LaunchTarget target) =>
        target.Kind switch
        {
            LaunchTargetKind.WindowsTerminal or LaunchTargetKind.IntelligentTerminal =>
                BuildWindowsTerminalArguments(shortcut, target),
            LaunchTargetKind.PowerShell =>
                BuildPowerShellWindowsTerminalArguments(shortcut, "powershell.exe"),
            LaunchTargetKind.Pwsh =>
                BuildPowerShellWindowsTerminalArguments(shortcut, "pwsh.exe"),
            LaunchTargetKind.Cmd =>
                BuildCmdWindowsTerminalArguments(shortcut, target),
            LaunchTargetKind.Wsl =>
                BuildWslWindowsTerminalArguments(shortcut, target),
            _ => BuildWindowsTerminalArguments(shortcut, target),
        };

    private static List<string> BuildPowerShellWindowsTerminalArguments(TerminalShortcut shortcut, string executable)
    {
        if (WslPathResolver.TryParse(shortcut.Directory, out var wslLocation))
        {
            var directory = wslLocation.UncPath ?? shortcut.Directory;
            return [TerminalLauncherArgs.ToPowerShellExecutableCommand(shortcut, executable, directory)];
        }

        return
        [
            $"-d \"{TerminalLauncherArgs.EscapeWindowsTerminalArg(shortcut.Directory)}\"",
            TerminalLauncherArgs.ToWindowsTerminalPowerShellSuffix(shortcut, executable),
        ];
    }

    private static List<string> BuildCmdWindowsTerminalArguments(TerminalShortcut shortcut, LaunchTarget target)
    {
        if (WslPathResolver.TryParse(shortcut.Directory, out var wslLocation))
        {
            return [ToWslExecutableCommand(shortcut, target, wslLocation, interactiveShell: string.IsNullOrWhiteSpace(shortcut.Command))];
        }

        return
        [
            $"-d \"{TerminalLauncherArgs.EscapeWindowsTerminalArg(shortcut.Directory)}\"",
            TerminalLauncherArgs.BuildWindowsTerminalCmdSuffix(shortcut, omitDirectoryChange: true),
        ];
    }

    private static List<string> BuildWslWindowsTerminalArguments(TerminalShortcut shortcut, LaunchTarget target)
    {
        if (WslPathResolver.TryParse(shortcut.Directory, out var wslLocation))
        {
            return [ToWslExecutableCommand(shortcut, target, wslLocation, interactiveShell: string.IsNullOrWhiteSpace(shortcut.Command))];
        }

        return
        [
            ToWslExecutableCommand(
                shortcut,
                target,
                WslPathResolver.CreateLocationFromWindowsDirectory(shortcut.Directory, target),
                interactiveShell: string.IsNullOrWhiteSpace(shortcut.Command)),
        ];
    }

    private static List<string> BuildWindowsTerminalArgumentsForWslDirectory(
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
            return arguments;
        }

        if (IsPowerShellProfile(target))
        {
            var directory = wslLocation.UncPath ?? shortcut.Directory;
            arguments.Add(TerminalLauncherArgs.ToPowerShellExecutableCommand(shortcut, GetPowerShellPathForProfile(target), directory));
            return arguments;
        }

        arguments.Add(ToWslExecutableCommand(shortcut, target, wslLocation));
        return arguments;
    }

    private static string BuildWindowsTerminalCommandSuffix(
        TerminalShortcut shortcut,
        LaunchTarget target,
        bool omitDirectoryChange = false)
    {
        var command = shortcut.Command;

        if (WslPathResolver.TryParse(shortcut.Directory, out var wslLocation))
        {
            return TerminalLauncherArgs.ToWslExecutableCommand(shortcut, target, wslLocation, interactiveShell: string.IsNullOrWhiteSpace(command));
        }

        if (IsWslProfile(target))
        {
            return ToWslExecutableCommand(
                shortcut,
                target,
                WslPathResolver.CreateLocationFromWindowsDirectory(shortcut.Directory, target),
                interactiveShell: string.IsNullOrWhiteSpace(command));
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var commandLine = target.WtCommandLine ?? string.Empty;

        if (commandLine.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            var executable = TerminalLauncherArgs.TryExtractExecutableFromCommandLine(commandLine) ?? "pwsh.exe";
            return TerminalLauncherArgs.ToWindowsTerminalPowerShellSuffix(shortcut, executable);
        }

        if (commandLine.Contains("powershell", StringComparison.OrdinalIgnoreCase))
        {
            var executable = TerminalLauncherArgs.TryExtractExecutableFromCommandLine(commandLine) ?? "powershell.exe";
            return TerminalLauncherArgs.ToWindowsTerminalPowerShellSuffix(shortcut, executable);
        }

        if (IsNushellProfile(target))
        {
            var executable = TerminalLauncherArgs.TryExtractExecutableFromCommandLine(commandLine) ?? "nu.exe";
            return TerminalLauncherArgs.ToWindowsTerminalNushellSuffix(shortcut, executable);
        }

        if (TerminalLauncherArgs.IsPackageManagerCommand(command))
        {
            return TerminalLauncherArgs.BuildWindowsTerminalCmdSuffix(shortcut, omitDirectoryChange);
        }

        return TerminalLauncherArgs.BuildWindowsTerminalCmdSuffix(shortcut, omitDirectoryChange);
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

        return CreateWslProcessStartInfo(shortcut, target, WslPathResolver.CreateLocationFromWindowsDirectory(shortcut.Directory, target));
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

    private static bool IsNushellProfile(LaunchTarget target)
    {
        var commandLine = target.WtCommandLine ?? string.Empty;
        return commandLine.Contains("nu.exe", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("nushell", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPowerShellPathForProfile(LaunchTarget target) =>
        (target.WtCommandLine ?? string.Empty).Contains("pwsh", StringComparison.OrdinalIgnoreCase)
            || target.Kind == LaunchTargetKind.Pwsh
            ? "pwsh.exe"
            : "powershell.exe";
}
