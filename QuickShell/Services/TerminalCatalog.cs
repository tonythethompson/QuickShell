using System.Diagnostics;
using System.Text.Json;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;

namespace QuickShell.Services;

internal enum LaunchTargetKind
{
    Default,
    WindowsTerminal,
    IntelligentTerminal,
    PowerShell,
    Pwsh,
    Cmd,
    Wsl,
}

internal sealed class LaunchTarget
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required LaunchTargetKind Kind { get; init; }

    public string? ProfileOrDistro { get; init; }

    public string? WtCommandLine { get; init; }

    public string HostExecutable { get; init; } = "wt.exe";
}

internal static class TerminalCatalog
{
    private static readonly object Sync = new();
    private static IReadOnlyList<LaunchTarget>? _cached;
    private static Dictionary<string, LaunchTarget>? _byId;
    private static ExecutableAvailability? _executables;
    private static string? _cachedFormChoicesJson;
    private static bool _cachedFormChoicesIncludeDefault;
    private static string? _cachedFormApplicationId;

    public static IReadOnlyList<LaunchTarget> GetLaunchTargets(bool includeDefaultChoice = false)
    {
        EnsureCached();

        if (!includeDefaultChoice)
        {
            return _cached!;
        }

        return
        [
            new LaunchTarget
            {
                Id = "default",
                DisplayName = "Default (from settings)",
                Kind = LaunchTargetKind.Default,
            },
            .. _cached!,
        ];
    }

    public static void InvalidateCache()
    {
        lock (Sync)
        {
            _cached = null;
            _byId = null;
            _executables = null;
            _cachedFormChoicesJson = null;
        }

        WtProfilesService.InvalidateCache();
    }

    public static List<ChoiceSetSetting.Choice> GetTerminalApplicationChoices()
    {
        var choices = new List<ChoiceSetSetting.Choice>
        {
            new("Let Windows choose", TerminalHostIds.LetWindowsChoose),
            new("Windows Terminal", TerminalHostIds.WindowsTerminal),
            new("Windows Console Host", TerminalHostIds.WindowsConsoleHost),
        };

        if (HasTerminalApplication(TerminalHostIds.IntelligentTerminal))
        {
            choices.Add(new ChoiceSetSetting.Choice("Intelligent Terminal", TerminalHostIds.IntelligentTerminal));
        }

        return choices;
    }

    public static List<ChoiceSetSetting.Choice> GetDefaultProfileChoices(string terminalApplicationId)
    {
        if (!TerminalHostIds.UsesWindowsTerminalProfiles(terminalApplicationId))
        {
            return GetConsoleHostProfileChoices();
        }

        var effectiveApp = TerminalHostIds.ResolveEffectiveApplication(terminalApplicationId);
        var choices = new List<ChoiceSetSetting.Choice>
        {
            new("Default profile for this app", TerminalHostIds.DefaultProfile),
        };

        foreach (var profile in WtProfilesService.GetProfilesForApplication(effectiveApp))
        {
            choices.Add(new ChoiceSetSetting.Choice(profile.Name, profile.Name));
        }

        return choices;
    }

    private static List<ChoiceSetSetting.Choice> GetConsoleHostProfileChoices()
    {
        EnsureCached();
        var choices = new List<ChoiceSetSetting.Choice>
        {
            new("Default profile for this app", TerminalHostIds.DefaultProfile),
        };

        if (_executables!.PowerShell)
        {
            choices.Add(new ChoiceSetSetting.Choice("PowerShell", "powershell"));
        }

        if (_executables.Pwsh)
        {
            choices.Add(new ChoiceSetSetting.Choice("PowerShell 7", "pwsh"));
        }

        if (_executables.Cmd)
        {
            choices.Add(new ChoiceSetSetting.Choice("Command Prompt", "cmd"));
        }

        return choices;
    }

    public static List<ChoiceSetSetting.Choice> GetSettingsChoices() =>
        GetTerminalApplicationChoices();

    public static bool HasTerminalApplication(string terminalApplicationId)
    {
        if (terminalApplicationId.Equals(TerminalHostIds.LetWindowsChoose, StringComparison.OrdinalIgnoreCase)
            || terminalApplicationId.Equals(TerminalHostIds.WindowsConsoleHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (WtProfilesService.GetProfilesForApplication(terminalApplicationId).Count > 0)
        {
            return true;
        }

        EnsureCached();
        return terminalApplicationId.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            ? _executables!.IntelligentTerminal
            : _executables!.WindowsTerminal;
    }

    public static IReadOnlyList<WtProfileInfo> GetProfilesForApplication(string terminalApplicationId) =>
        WtProfilesService.GetProfilesForApplication(terminalApplicationId);

    public static string GetDisplayName(TerminalShortcut shortcut)
    {
        var id = EncodeLaunchTargetId(shortcut);
        if (id.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return "Default";
        }

        EnsureCached();
        if (_byId!.TryGetValue(id, out var target))
        {
            return target.DisplayName;
        }

        return FormatFallback(shortcut);
    }

    public static string EncodeLaunchTargetId(TerminalShortcut shortcut)
    {
        var terminal = (shortcut.Terminal ?? "default").Trim().ToLowerInvariant();
        return terminal switch
        {
            "default" => "default",
            "it" => string.IsNullOrWhiteSpace(shortcut.WtProfile) ? "it" : $"it:{shortcut.WtProfile}",
            "wt" => string.IsNullOrWhiteSpace(shortcut.WtProfile) ? "wt" : $"wt:{shortcut.WtProfile}",
            "wsl" => string.IsNullOrWhiteSpace(shortcut.WtProfile) ? "wsl" : $"wsl:{shortcut.WtProfile}",
            "powershell" => "powershell",
            "pwsh" or "powershell7" => "pwsh",
            "cmd" => "cmd",
            _ => "default",
        };
    }

    public static void ApplyLaunchTargetId(TerminalShortcut shortcut, string? launchTargetId)
    {
        var id = (launchTargetId ?? "default").Trim();
        if (id.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            shortcut.Terminal = "default";
            shortcut.WtProfile = null;
            return;
        }

        if (id.Equals("wt", StringComparison.OrdinalIgnoreCase))
        {
            shortcut.Terminal = "wt";
            shortcut.WtProfile = null;
            return;
        }

        if (id.StartsWith("wt:", StringComparison.OrdinalIgnoreCase))
        {
            shortcut.Terminal = "wt";
            shortcut.WtProfile = id[3..];
            return;
        }

        if (id.Equals("it", StringComparison.OrdinalIgnoreCase))
        {
            shortcut.Terminal = "it";
            shortcut.WtProfile = null;
            return;
        }

        if (id.StartsWith("it:", StringComparison.OrdinalIgnoreCase))
        {
            shortcut.Terminal = "it";
            shortcut.WtProfile = id[3..];
            return;
        }

        if (id.StartsWith("wsl:", StringComparison.OrdinalIgnoreCase))
        {
            shortcut.Terminal = "wsl";
            shortcut.WtProfile = id[4..];
            return;
        }

        shortcut.Terminal = id.ToLowerInvariant() switch
        {
            "powershell7" => "pwsh",
            _ => id.ToLowerInvariant(),
        };
        shortcut.WtProfile = null;
    }

    public static LaunchTarget Resolve(string? launchTargetId)
    {
        var id = NormalizeLaunchTargetId(launchTargetId);
        if (id.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            id = "wt";
        }

        EnsureCached();
        if (_byId!.TryGetValue(id, out var target))
        {
            return target;
        }

        return _byId.TryGetValue("wt", out var fallback)
            ? fallback
            : new LaunchTarget
            {
                Id = "wt",
                DisplayName = "Windows Terminal",
                Kind = LaunchTargetKind.WindowsTerminal,
            };
    }

    public static LaunchTarget ResolveForShortcut(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId)
    {
        var id = EncodeLaunchTargetId(shortcut);
        if (id.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveDefaultTarget(terminalApplicationId, defaultProfileId);
        }

        if (IsProfileLaunch(id, shortcut))
        {
            return ResolveProfileTarget(terminalApplicationId, shortcut.WtProfile, id);
        }

        return Resolve(id.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? NormalizeLaunchTargetId(defaultProfileId)
            : id);
    }

    private static bool IsProfileLaunch(string id, TerminalShortcut shortcut) =>
        id.Equals("wt", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("wt:", StringComparison.OrdinalIgnoreCase)
        || id.Equals("it", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("it:", StringComparison.OrdinalIgnoreCase)
        || shortcut.Terminal is "wt" or "it";

    private static LaunchTarget ResolveDefaultTarget(string terminalApplicationId, string defaultProfileId)
    {
        if (!TerminalHostIds.UsesWindowsTerminalProfiles(terminalApplicationId))
        {
            if (defaultProfileId.Equals(TerminalHostIds.DefaultProfile, StringComparison.OrdinalIgnoreCase))
            {
                return Resolve("powershell");
            }

            if (IsStandaloneShellId(defaultProfileId))
            {
                return Resolve(defaultProfileId);
            }

            return Resolve(NormalizeLaunchTargetId(defaultProfileId));
        }

        if (IsStandaloneShellId(defaultProfileId))
        {
            return Resolve(defaultProfileId);
        }

        var effectiveApp = TerminalHostIds.ResolveEffectiveApplication(terminalApplicationId);
        var profileName = defaultProfileId.Equals(TerminalHostIds.DefaultProfile, StringComparison.OrdinalIgnoreCase)
            ? null
            : defaultProfileId;

        var prefix = TerminalHostIds.ProfileIdPrefix(effectiveApp);
        return ResolveProfileTarget(
            effectiveApp,
            profileName,
            profileName is null ? prefix : $"{prefix}:{profileName}");
    }

    private static LaunchTarget ResolveProfileTarget(string terminalApplicationId, string? profileName, string fallbackId)
    {
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var prefix = TerminalHostIds.ProfileIdPrefix(terminalApplicationId);
            var explicitId = $"{prefix}:{profileName}";
            EnsureCached();
            if (_byId!.TryGetValue(explicitId, out var explicitTarget))
            {
                return new LaunchTarget
                {
                    Id = explicitTarget.Id,
                    DisplayName = explicitTarget.DisplayName,
                    Kind = explicitTarget.Kind,
                    ProfileOrDistro = explicitTarget.ProfileOrDistro,
                    WtCommandLine = explicitTarget.WtCommandLine,
                    HostExecutable = TerminalHostIds.HostExecutable(terminalApplicationId),
                };
            }
        }

        EnsureCached();
        var hostExecutable = TerminalHostIds.HostExecutable(terminalApplicationId);
        var kind = terminalApplicationId.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            ? LaunchTargetKind.IntelligentTerminal
            : LaunchTargetKind.WindowsTerminal;

        if (string.IsNullOrWhiteSpace(profileName))
        {
            return new LaunchTarget
            {
                Id = TerminalHostIds.ProfileIdPrefix(terminalApplicationId),
                DisplayName = $"{TerminalHostIds.SourceLabel(terminalApplicationId)} (default profile)",
                Kind = kind,
                HostExecutable = hostExecutable,
            };
        }

        var profile = WtProfilesService.GetProfilesForApplication(terminalApplicationId)
            .FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        return new LaunchTarget
        {
            Id = fallbackId,
            DisplayName = profile?.Name ?? profileName,
            Kind = kind,
            ProfileOrDistro = profileName,
            WtCommandLine = profile?.Commandline,
            HostExecutable = hostExecutable,
        };
    }

    private static bool IsStandaloneShellId(string id) =>
        IsStandaloneShellLaunchTarget(id);

    public static bool IsStandaloneShellLaunchTarget(string? launchTargetId)
    {
        var id = NormalizeLaunchTargetId(launchTargetId);
        return id is "powershell" or "pwsh" or "cmd" || id.StartsWith("wsl:", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildFormChoicesJson(bool includeDefaultChoice, string terminalApplicationId)
    {
        lock (Sync)
        {
            if (_cachedFormChoicesJson is not null
                && _cachedFormChoicesIncludeDefault == includeDefaultChoice
                && string.Equals(_cachedFormApplicationId, terminalApplicationId, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedFormChoicesJson;
            }
        }

        var choiceTargets = new List<LaunchTarget>();
        if (includeDefaultChoice)
        {
            choiceTargets.Add(new LaunchTarget
            {
                Id = "default",
                DisplayName = "Default (from settings)",
                Kind = LaunchTargetKind.Default,
            });
        }

        var appLabel = TerminalHostIds.SourceLabel(terminalApplicationId);
        var prefix = TerminalHostIds.ProfileIdPrefix(terminalApplicationId);
        var profileKind = terminalApplicationId.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            ? LaunchTargetKind.IntelligentTerminal
            : LaunchTargetKind.WindowsTerminal;

        foreach (var profile in WtProfilesService.GetProfilesForApplication(terminalApplicationId))
        {
            choiceTargets.Add(new LaunchTarget
            {
                Id = $"{prefix}:{profile.Name}",
                DisplayName = profile.Name,
                Kind = profileKind,
                ProfileOrDistro = profile.Name,
                WtCommandLine = profile.Commandline,
                HostExecutable = TerminalHostIds.HostExecutable(terminalApplicationId),
            });
        }

        if (choiceTargets.Count == (includeDefaultChoice ? 1 : 0))
        {
            choiceTargets.Add(new LaunchTarget
            {
                Id = prefix,
                DisplayName = $"{appLabel} (default profile)",
                Kind = profileKind,
                HostExecutable = TerminalHostIds.HostExecutable(terminalApplicationId),
            });
        }

        EnsureCached();
        foreach (var target in _cached!.Where(t => t.Kind is LaunchTargetKind.PowerShell or LaunchTargetKind.Pwsh or LaunchTargetKind.Cmd or LaunchTargetKind.Wsl))
        {
            choiceTargets.Add(target);
        }

        var choices = choiceTargets
            .Select(t => $"{{ \"title\": \"{EscapeJson(t.DisplayName)}\", \"value\": \"{EscapeJson(t.Id)}\" }}");

        var json = "[" + string.Join(',', choices) + "]";
        lock (Sync)
        {
            _cachedFormChoicesIncludeDefault = includeDefaultChoice;
            _cachedFormApplicationId = terminalApplicationId;
            _cachedFormChoicesJson = json;
            return _cachedFormChoicesJson;
        }
    }

    public static LaunchTarget ResolveForShortcut(TerminalShortcut shortcut, string defaultLaunchTargetId) =>
        ResolveForShortcut(shortcut, TerminalHostIds.WindowsTerminal, defaultLaunchTargetId);

    public static string NormalizeLaunchTargetId(string? launchTargetId)
    {
        var value = (launchTargetId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "wt";
        }

        if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return "default";
        }

        if (value.Equals("windows-terminal", StringComparison.OrdinalIgnoreCase))
        {
            return "wt";
        }

        if (value.Equals("powershell7", StringComparison.OrdinalIgnoreCase))
        {
            return "pwsh";
        }

        if (value.Equals("intelligent-terminal", StringComparison.OrdinalIgnoreCase)
            || value.Equals("intelligentterminal", StringComparison.OrdinalIgnoreCase))
        {
            return TerminalHostIds.IntelligentTerminal;
        }

        if (value.StartsWith("it:", StringComparison.OrdinalIgnoreCase)
            || value.Equals("it", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (value.StartsWith("wt:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("wsl:", StringComparison.OrdinalIgnoreCase)
            || value is "wt" or "powershell" or "pwsh" or "cmd")
        {
            return value;
        }

        return value.ToLowerInvariant() switch
        {
            "powershell" => "powershell",
            "pwsh" => "pwsh",
            "cmd" => "cmd",
            _ => "wt",
        };
    }

    public static string BuildFormChoicesJson(bool includeDefaultChoice) =>
        BuildFormChoicesJson(includeDefaultChoice, TerminalHostIds.WindowsTerminal);

    private static void EnsureCached()
    {
        lock (Sync)
        {
            if (_cached is not null)
            {
                return;
            }

            _executables ??= ExecutableAvailability.Discover();
            _cached = DiscoverLaunchTargets(_executables);
            _byId = _cached.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static List<LaunchTarget> DiscoverLaunchTargets(ExecutableAvailability executables)
    {
        var targets = new List<LaunchTarget>();
        var profiles = WtProfilesService.GetProfiles();

        foreach (var profile in profiles.Where(p => TerminalHostIds.IsSupportedProfilePrefix(p.IdPrefix)))
        {
            targets.Add(new LaunchTarget
            {
                Id = $"{profile.IdPrefix}:{profile.Name}",
                DisplayName = $"{profile.SourceLabel} · {profile.Name}",
                Kind = profile.IdPrefix.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
                    ? LaunchTargetKind.IntelligentTerminal
                    : LaunchTargetKind.WindowsTerminal,
                ProfileOrDistro = profile.Name,
                WtCommandLine = profile.Commandline,
                HostExecutable = profile.HostExecutable,
            });
        }

        if (executables.WindowsTerminal || profiles.Any(p => TerminalHostIds.IsWindowsTerminalProfilePrefix(p.IdPrefix)))
        {
            targets.Add(new LaunchTarget
            {
                Id = TerminalHostIds.WindowsTerminal,
                DisplayName = "Windows Terminal (default profile)",
                Kind = LaunchTargetKind.WindowsTerminal,
                HostExecutable = "wt.exe",
            });
        }

        if (executables.IntelligentTerminal || profiles.Any(p => p.IdPrefix == TerminalHostIds.IntelligentTerminal))
        {
            targets.Add(new LaunchTarget
            {
                Id = TerminalHostIds.IntelligentTerminal,
                DisplayName = "Intelligent Terminal (default profile)",
                Kind = LaunchTargetKind.IntelligentTerminal,
                HostExecutable = "wtai.exe",
            });
        }

        if (executables.PowerShell)
        {
            targets.Add(new LaunchTarget
            {
                Id = "powershell",
                DisplayName = "PowerShell",
                Kind = LaunchTargetKind.PowerShell,
            });
        }

        if (executables.Pwsh)
        {
            targets.Add(new LaunchTarget
            {
                Id = "pwsh",
                DisplayName = "PowerShell 7",
                Kind = LaunchTargetKind.Pwsh,
            });
        }

        if (executables.Cmd)
        {
            targets.Add(new LaunchTarget
            {
                Id = "cmd",
                DisplayName = "Command Prompt",
                Kind = LaunchTargetKind.Cmd,
            });
        }

        if (!executables.WindowsTerminal && !executables.IntelligentTerminal)
        {
            foreach (var distro in executables.WslDistros)
            {
                targets.Add(new LaunchTarget
                {
                    Id = $"wsl:{distro}",
                    DisplayName = $"WSL · {distro}",
                    Kind = LaunchTargetKind.Wsl,
                    ProfileOrDistro = distro,
                });
            }
        }

        if (targets.Count == 0)
        {
            targets.Add(new LaunchTarget
            {
                Id = "cmd",
                DisplayName = "Command Prompt",
                Kind = LaunchTargetKind.Cmd,
            });
        }

        return targets;
    }

    private static string FormatFallback(TerminalShortcut shortcut)
    {
        var terminal = (shortcut.Terminal ?? "default").Trim();
        if (!string.IsNullOrWhiteSpace(shortcut.WtProfile))
        {
            return $"{terminal} · {shortcut.WtProfile}";
        }

        return terminal;
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class ExecutableAvailability
    {
        public bool WindowsTerminal { get; init; }

        public bool IntelligentTerminal { get; init; }

        public bool PowerShell { get; init; }

        public bool Pwsh { get; init; }

        public bool Cmd { get; init; }

        public string[] WslDistros { get; init; } = [];

        public static ExecutableAvailability Discover()
        {
            var locations = TerminalSettingsDiscovery.DiscoverLocations();
            var wt = IsOnPath("wt.exe")
                || locations.Any(location =>
                    location.HostExecutable.Equals("wt.exe", StringComparison.OrdinalIgnoreCase));
            var intelligentTerminal = IsOnPath("wtai.exe")
                || locations.Any(location =>
                    location.HostExecutable.Equals("wtai.exe", StringComparison.OrdinalIgnoreCase));
            return new ExecutableAvailability
            {
                WindowsTerminal = wt,
                IntelligentTerminal = intelligentTerminal,
                PowerShell = IsOnPath("powershell.exe"),
                Pwsh = IsOnPath("pwsh.exe"),
                Cmd = IsOnPath("cmd.exe"),
                WslDistros = wt || intelligentTerminal ? [] : GetWslDistros(),
            };
        }

        private static bool IsOnPath(string fileName)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return false;
                }

                if (!process.WaitForExit(1500))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Best effort.
                    }

                    return false;
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string[] GetWslDistros()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = "-l -q",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return [];
                }

                var output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(3000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Best effort.
                    }

                    return [];
                }

                if (process.ExitCode != 0)
                {
                    return [];
                }

                return output
                    .Replace("\0", string.Empty)
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }
    }
}
