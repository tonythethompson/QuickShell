using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickShell.Abstractions;
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

    public string? FallbackReason { get; init; }
}

internal sealed class TerminalCatalog : ITerminalCatalog
{
    private sealed class CatalogSnapshot
    {
        public required IReadOnlyList<LaunchTarget> Targets { get; init; }

        public required Dictionary<string, LaunchTarget> ById { get; init; }

        public required ExecutableAvailability Executables { get; init; }
    }

    private readonly object _sync = new();
    private readonly IWtProfilesService _profiles;

    private CatalogSnapshot? _snapshot;
    private string? _cachedFormChoicesJson;
    private bool _cachedFormChoicesIncludeDefault;
    private string? _cachedFormApplicationId;
    private string? _cachedFingerprint;

    public const string SameAsPreviousLaunchTargetId = ITerminalCatalog.SameAsPreviousLaunchTargetId;

    public const string SameAsPreviousDisplayName = ITerminalCatalog.SameAsPreviousDisplayName;

    public TerminalCatalog(IWtProfilesService profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }


    string ITerminalCatalog.EncodeLaunchTargetId(TerminalShortcut shortcut) =>
        EncodeLaunchTargetId(shortcut);

    void ITerminalCatalog.ApplyLaunchTargetId(TerminalShortcut shortcut, string? launchTargetId) =>
        ApplyLaunchTargetId(shortcut, launchTargetId);

    bool ITerminalCatalog.IsStandaloneShellLaunchTarget(string? launchTargetId) =>
        IsStandaloneShellLaunchTarget(launchTargetId);

    string ITerminalCatalog.NormalizeLaunchTargetId(string? launchTargetId) =>
        NormalizeLaunchTargetId(launchTargetId);

    string ITerminalCatalog.ResolveEffectiveLaunchTargetId(
        IReadOnlyList<WorkspaceEntry> orderedLaunches,
        int index) =>
        ResolveEffectiveLaunchTargetId(orderedLaunches, index);

    WorkspaceEntry ITerminalCatalog.ResolveLaunchEntry(
        WorkspaceEntry entry,
        IReadOnlyList<WorkspaceEntry> orderedLaunches,
        int index) =>
        ResolveLaunchEntry(entry, orderedLaunches, index);


    public IReadOnlyList<LaunchTarget> GetLaunchTargets(bool includeDefaultChoice = false)
    {
        var snapshot = EnsureCached();

        if (!includeDefaultChoice)
        {
            return snapshot.Targets;
        }

        return
        [
            new LaunchTarget
            {
                Id = "default",
                DisplayName = "Default (from settings)",
                Kind = LaunchTargetKind.Default,
            },
            .. snapshot.Targets,
        ];
    }

    public void InvalidateCache()
    {
        lock (_sync)
        {
            _snapshot = null;
            _cachedFormChoicesJson = null;
            _cachedFingerprint = null;
        }

        _profiles.InvalidateCache();
    }

    /// <summary>
    /// Returns a stable fingerprint of the current terminal catalog snapshot.
    /// The value changes when installed terminals, Windows Terminal profiles, or WSL distros change.
    /// </summary>
    public string GetFingerprint()
    {
        lock (_sync)
        {
            if (_cachedFingerprint is not null)
            {
                return _cachedFingerprint;
            }

            var snapshot = EnsureCached();
            _cachedFingerprint = ComputeFingerprint(snapshot);
            return _cachedFingerprint;
        }
    }

    private static string ComputeFingerprint(CatalogSnapshot snapshot)
    {
        var builder = new StringBuilder();
        foreach (var target in snapshot.Targets.OrderBy(static t => t.Id, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(target.Id).Append('\n');
            builder.Append((int)target.Kind).Append('\n');
            builder.Append(target.DisplayName).Append('\n');
            builder.Append(target.ProfileOrDistro).Append('\n');
            builder.Append(target.WtCommandLine).Append('\n');
            builder.Append(target.HostExecutable).Append('\n');
            builder.Append(target.FallbackReason).Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public IReadOnlyList<string> GetDefaultProfileIds(string terminalApplicationId)
    {
        if (!TerminalHostIds.UsesWindowsTerminalProfiles(terminalApplicationId))
        {
            return GetConsoleHostProfileIds();
        }

        var effectiveApp = TerminalHostIds.ResolveEffectiveApplication(terminalApplicationId);
        var ids = new List<string> { TerminalHostIds.DefaultProfile };
        foreach (var profile in _profiles.GetProfilesForApplication(effectiveApp))
        {
            ids.Add(profile.Name);
        }

        return ids;
    }

    private List<string> GetConsoleHostProfileIds()
    {
        var snapshot = EnsureCached();
        var ids = new List<string> { TerminalHostIds.DefaultProfile };

        if (snapshot.Executables.PowerShell)
        {
            ids.Add("powershell");
        }

        if (snapshot.Executables.Pwsh)
        {
            ids.Add("pwsh");
        }

        if (snapshot.Executables.Cmd)
        {
            ids.Add("cmd");
        }

        return ids;
    }

    public bool HasTerminalApplication(string terminalApplicationId)
    {
        if (terminalApplicationId.Equals(TerminalHostIds.LetWindowsChoose, StringComparison.OrdinalIgnoreCase)
            || terminalApplicationId.Equals(TerminalHostIds.WindowsConsoleHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_profiles.GetProfilesForApplication(terminalApplicationId).Count > 0)
        {
            return true;
        }

        var snapshot = EnsureCached();
        return terminalApplicationId.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            ? snapshot.Executables.IntelligentTerminal
            : snapshot.Executables.WindowsTerminal;
    }

    public IReadOnlyList<WtProfileInfo> GetProfilesForApplication(string terminalApplicationId) =>
        _profiles.GetProfilesForApplication(terminalApplicationId);

    public string GetDisplayName(TerminalShortcut shortcut)
    {
        var id = EncodeLaunchTargetId(shortcut);
        if (id.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return "Default";
        }

        var snapshot = EnsureCached();
        if (snapshot.ById.TryGetValue(id, out var target))
        {
            return target.DisplayName;
        }

        return FormatFallback(shortcut);
    }

    /// <summary>
    /// Profile-only label for workspace list subtitles (no terminal host name).
    /// </summary>
    public string GetProfileLabel(TerminalShortcut shortcut)
    {
        var id = EncodeLaunchTargetId(shortcut);
        if (id.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return "Default";
        }

        if (id.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || id.Equals("powershell7", StringComparison.OrdinalIgnoreCase))
        {
            return "PowerShell 7";
        }

        if (id.Equals("powershell", StringComparison.OrdinalIgnoreCase))
        {
            return "PowerShell";
        }

        if (id.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            return "Command Prompt";
        }

        if (TryParsePrefixedProfileId(id, out var profileName))
        {
            return profileName;
        }

        if (!string.IsNullOrWhiteSpace(shortcut.WtProfile))
        {
            return shortcut.WtProfile;
        }

        // Common host identifiers can be labeled without building the catalog,
        // keeping first-list rendering off the expensive discovery path.
        if (id.Equals(TerminalHostIds.WindowsTerminal, StringComparison.OrdinalIgnoreCase))
        {
            return "Windows Terminal";
        }

        if (id.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase))
        {
            return "Intelligent Terminal";
        }

        if (id.Equals("wsl", StringComparison.OrdinalIgnoreCase))
        {
            return "WSL";
        }

        // Avoid forcing a catalog build during first paint; the staged warmup will
        // populate the snapshot and a later list refresh can show richer labels.
        if (_snapshot is null)
        {
            return FormatFallback(shortcut);
        }

        var snapshot = EnsureCached();
        if (snapshot.ById.TryGetValue(id, out var target)
            && !string.IsNullOrWhiteSpace(target.ProfileOrDistro))
        {
            return target.ProfileOrDistro;
        }

        return FormatFallback(shortcut);
    }

    private static bool TryParsePrefixedProfileId(string id, out string profileName)
    {
        foreach (var prefix in new[] { "wt:", "it:", "wsl:" })
        {
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                profileName = id[prefix.Length..];
                return !string.IsNullOrWhiteSpace(profileName);
            }
        }

        profileName = string.Empty;
        return false;
    }

    public static string EncodeLaunchTargetId(TerminalShortcut shortcut)
    {
        var terminal = (shortcut.Terminal ?? "default").Trim().ToLowerInvariant();
        return terminal switch
        {
            "default" => "default",
            SameAsPreviousLaunchTargetId => SameAsPreviousLaunchTargetId,
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
        if (id.Equals(SameAsPreviousLaunchTargetId, StringComparison.OrdinalIgnoreCase))
        {
            shortcut.Terminal = SameAsPreviousLaunchTargetId;
            shortcut.WtProfile = null;
            return;
        }

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

    public LaunchTarget Resolve(string? launchTargetId)
    {
        var id = NormalizeLaunchTargetId(launchTargetId);
        if (id.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            id = "wt";
        }

        var snapshot = EnsureCached();
        if (snapshot.ById.TryGetValue(id, out var target))
        {
            return target;
        }

        return snapshot.ById.TryGetValue("wt", out var fallback)
            ? WithFallbackReason(fallback, $"Launch target '{id}' was not found; using Windows Terminal.")
            : new LaunchTarget
            {
                Id = "wt",
                DisplayName = "Windows Terminal",
                Kind = LaunchTargetKind.WindowsTerminal,
                FallbackReason = $"Launch target '{id}' was not found; using Windows Terminal.",
            };
    }

    public LaunchTarget ResolveForShortcut(
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

    private LaunchTarget ResolveDefaultTarget(string terminalApplicationId, string defaultProfileId)
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

    private LaunchTarget ResolveProfileTarget(string terminalApplicationId, string? profileName, string fallbackId)
    {
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var prefix = TerminalHostIds.ProfileIdPrefix(terminalApplicationId);
            var explicitId = $"{prefix}:{profileName}";
            var explicitSnapshot = EnsureCached();
            if (explicitSnapshot.ById.TryGetValue(explicitId, out var explicitTarget))
            {
                return new LaunchTarget
                {
                    Id = explicitTarget.Id,
                    DisplayName = explicitTarget.DisplayName,
                    Kind = explicitTarget.Kind,
                    ProfileOrDistro = explicitTarget.ProfileOrDistro,
                    WtCommandLine = explicitTarget.WtCommandLine,
                    HostExecutable = TerminalHostIds.HostExecutable(terminalApplicationId),
                    FallbackReason = explicitTarget.FallbackReason,
                };
            }
        }

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

        var profile = _profiles.GetProfilesForApplication(terminalApplicationId)
            .FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        return new LaunchTarget
        {
            Id = fallbackId,
            DisplayName = profile?.Name ?? profileName,
            Kind = kind,
            ProfileOrDistro = profileName,
            WtCommandLine = profile?.Commandline,
            HostExecutable = hostExecutable,
            FallbackReason = profile is null
                ? $"Profile '{profileName}' was not found; {TerminalHostIds.SourceLabel(terminalApplicationId)} may fall back or fail."
                : null,
        };
    }

    private static LaunchTarget WithFallbackReason(LaunchTarget target, string fallbackReason) =>
        new()
        {
            Id = target.Id,
            DisplayName = target.DisplayName,
            Kind = target.Kind,
            ProfileOrDistro = target.ProfileOrDistro,
            WtCommandLine = target.WtCommandLine,
            HostExecutable = target.HostExecutable,
            FallbackReason = fallbackReason,
        };

    private static bool IsStandaloneShellId(string id) =>
        IsStandaloneShellLaunchTarget(id);

    public static bool IsStandaloneShellLaunchTarget(string? launchTargetId)
    {
        var id = NormalizeLaunchTargetId(launchTargetId);
        return id is "powershell" or "pwsh" or "cmd" || id.StartsWith("wsl:", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildFormChoicesJson(bool includeDefaultChoice, string terminalApplicationId)
    {
        lock (_sync)
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

        choiceTargets.Add(new LaunchTarget
        {
            Id = SameAsPreviousLaunchTargetId,
            DisplayName = SameAsPreviousDisplayName,
            Kind = LaunchTargetKind.Default,
        });

        var appLabel = TerminalHostIds.SourceLabel(terminalApplicationId);
        var prefix = TerminalHostIds.ProfileIdPrefix(terminalApplicationId);
        var profileKind = terminalApplicationId.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            ? LaunchTargetKind.IntelligentTerminal
            : LaunchTargetKind.WindowsTerminal;

        foreach (var profile in _profiles.GetProfilesForApplication(terminalApplicationId))
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

        var snapshot = EnsureCached();
        foreach (var target in snapshot.Targets.Where(t => t.Kind is LaunchTargetKind.PowerShell or LaunchTargetKind.Pwsh or LaunchTargetKind.Cmd or LaunchTargetKind.Wsl))
        {
            choiceTargets.Add(target);
        }

        var choices = choiceTargets
            .Select(t => $"{{ \"title\": \"{EscapeJson(t.DisplayName)}\", \"value\": \"{EscapeJson(t.Id)}\" }}");

        var json = "[" + string.Join(',', choices) + "]";
        lock (_sync)
        {
            _cachedFormChoicesIncludeDefault = includeDefaultChoice;
            _cachedFormApplicationId = terminalApplicationId;
            _cachedFormChoicesJson = json;
            return _cachedFormChoicesJson;
        }
    }

    public LaunchTarget ResolveForShortcut(TerminalShortcut shortcut, string defaultLaunchTargetId) =>
        ResolveForShortcut(shortcut, TerminalHostIds.WindowsTerminal, defaultLaunchTargetId);

    public static string ResolveEffectiveLaunchTargetId(
        IReadOnlyList<WorkspaceEntry> orderedLaunches,
        int index)
    {
        for (var i = index; i >= 0; i--)
        {
            var scratch = new TerminalShortcut
            {
                Terminal = orderedLaunches[i].Terminal,
                WtProfile = orderedLaunches[i].WtProfile,
            };
            var id = EncodeLaunchTargetId(scratch);
            if (!id.Equals(SameAsPreviousLaunchTargetId, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return "default";
    }

    public static WorkspaceEntry ResolveLaunchEntry(
        WorkspaceEntry entry,
        IReadOnlyList<WorkspaceEntry> orderedLaunches,
        int index)
    {
        var scratch = new TerminalShortcut();
        ApplyLaunchTargetId(scratch, ResolveEffectiveLaunchTargetId(orderedLaunches, index));
        return new WorkspaceEntry
        {
            Id = entry.Id,
            Label = entry.Label,
            Terminal = scratch.Terminal,
            WtProfile = scratch.WtProfile,
            Command = entry.Command,
            RunAsAdmin = entry.RunAsAdmin,
            IsEnabled = entry.IsEnabled,
            Order = entry.Order,
            TaskType = entry.TaskType,
        };
    }

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

        if (value.Equals(SameAsPreviousLaunchTargetId, StringComparison.OrdinalIgnoreCase))
        {
            return SameAsPreviousLaunchTargetId;
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

    public string BuildFormChoicesJson(bool includeDefaultChoice) =>
        BuildFormChoicesJson(includeDefaultChoice, TerminalHostIds.WindowsTerminal);

    private CatalogSnapshot EnsureCached()
    {
        lock (_sync)
        {
            if (_snapshot is not null)
            {
                return _snapshot;
            }

            var executables = ExecutableAvailability.Discover();
            var targets = DiscoverLaunchTargets(executables);
            var byId = targets.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            _snapshot = new CatalogSnapshot { Targets = targets, ById = byId, Executables = executables };
            return _snapshot;
        }
    }

    private List<LaunchTarget> DiscoverLaunchTargets(ExecutableAvailability executables)
    {
        var targets = new List<LaunchTarget>();
        var profiles = _profiles.GetProfiles();

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
