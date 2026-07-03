using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutLaunchNormalization
{
    public const int MaxLaunchCount = 50;
    public const int MaxLabelLength = 120;

    public static void EnsureLaunchesFromLegacy(TerminalShortcut shortcut)
    {
        if (shortcut.Launches is { Count: > 0 })
        {
            NormalizeLaunchOrders(shortcut);
            return;
        }

        var label = string.IsNullOrWhiteSpace(shortcut.Name) ? "Main" : shortcut.Name.Trim();
        shortcut.Launches =
        [
            new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = label,
                Terminal = string.IsNullOrWhiteSpace(shortcut.Terminal) ? "default" : shortcut.Terminal,
                WtProfile = shortcut.WtProfile,
                Command = shortcut.Command,
                RunAsAdmin = shortcut.RunAsAdmin,
                IsEnabled = true,
                Order = 0,
            },
        ];
    }

    public static void MirrorLegacyFieldsFromFirstLaunch(TerminalShortcut shortcut)
    {
        if (shortcut.Launches is not { Count: > 0 })
        {
            return;
        }

        var first = shortcut.Launches
            .Where(entry => entry.IsEnabled)
            .OrderBy(entry => entry.Order)
            .FirstOrDefault()
            ?? shortcut.Launches.OrderBy(entry => entry.Order).First();
        shortcut.Command = first.Command;
        shortcut.Terminal = first.Terminal;
        shortcut.WtProfile = first.WtProfile;
        shortcut.RunAsAdmin = first.RunAsAdmin;
    }

    public static void NormalizeLaunchOrders(TerminalShortcut shortcut)
    {
        if (shortcut.Launches is not { Count: > 0 })
        {
            return;
        }

        var ordered = shortcut.Launches.OrderBy(entry => entry.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        shortcut.Launches = ordered;
    }

    public static void NormalizeShortcut(TerminalShortcut shortcut)
    {
        EnsureLaunchesFromLegacy(shortcut);

        foreach (var entry in shortcut.Launches)
        {
            var terminal = (entry.Terminal ?? string.Empty).Trim().ToLowerInvariant();
            entry.Terminal = terminal switch
            {
                "wt" or "windows-terminal" => "wt",
                "it" or "intelligent-terminal" => "it",
                "wsl" => "wsl",
                "powershell" => "powershell",
                "pwsh" or "powershell7" => "pwsh",
                "cmd" => "cmd",
                "default" or "" => "default",
                _ => "default",
            };

            entry.WtProfile = string.IsNullOrWhiteSpace(entry.WtProfile) ? null : entry.WtProfile.Trim();
            entry.Label = (entry.Label ?? string.Empty).Trim();
        }

        NormalizeLaunchOrders(shortcut);
        MirrorLegacyFieldsFromFirstLaunch(shortcut);
    }

    public static IReadOnlyList<WorkspaceEntry> GetEnabledLaunches(TerminalShortcut shortcut) =>
        shortcut.Launches
            .Where(entry => entry.IsEnabled)
            .OrderBy(entry => entry.Order)
            .ToList();

    public static TerminalShortcut ToLaunchShortcut(WorkspaceEntry entry, TerminalShortcut workspace) =>
        new()
        {
            Name = entry.Label,
            Directory = workspace.Directory,
            Command = entry.Command,
            Terminal = entry.Terminal,
            WtProfile = entry.WtProfile,
            RunAsAdmin = entry.RunAsAdmin,
        };

    public static TerminalShortcut WorkspaceToShortcut(Workspace workspace)
    {
        var shortcut = new TerminalShortcut
        {
            Id = workspace.Id,
            Name = workspace.Name,
            Abbreviation = workspace.Abbreviation,
            Directory = workspace.Directory,
            IsPinned = workspace.IsPinned,
            PinOrder = workspace.PinOrder,
            Launches = workspace.Entries.Select(WorkspaceMapper.CloneEntry).ToList(),
        };

        NormalizeShortcut(shortcut);
        return shortcut;
    }

    public static bool TryValidateLaunches(TerminalShortcut shortcut, out string error)
    {
        if (shortcut.Launches is null || shortcut.Launches.Count == 0)
        {
            error = "At least one launch entry is required.";
            return false;
        }

        if (shortcut.Launches.Count > MaxLaunchCount)
        {
            error = $"At most {MaxLaunchCount} launch entries are supported.";
            return false;
        }

        var enabledCount = shortcut.Launches.Count(entry => entry.IsEnabled);
        if (enabledCount == 0)
        {
            error = "At least one enabled launch entry is required.";
            return false;
        }

        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in shortcut.Launches)
        {
            if (string.IsNullOrWhiteSpace(entry.Label))
            {
                error = "Launch label is required.";
                return false;
            }

            if (entry.Label.Length > MaxLabelLength)
            {
                error = $"Launch label must be {MaxLabelLength} characters or fewer.";
                return false;
            }

            if (!ShortcutValidation.TryValidateCommand(entry.Command, out error))
            {
                return false;
            }

            if (!ShortcutValidation.TryValidateWtProfile(entry.WtProfile, out error))
            {
                return false;
            }

            if (!labels.Add(entry.Label.Trim()))
            {
                error = $"Duplicate launch label '{entry.Label}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.Id) || !ShortcutCommandIds.IsStableShortcutId(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString("N");
            }
        }

        error = string.Empty;
        return true;
    }
}
