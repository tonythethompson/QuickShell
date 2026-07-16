using QuickShell.Models;
using System.Diagnostics;

namespace QuickShell.Services;

internal static class CompanionAppLauncher
{
    public static bool IsConfigured(TerminalShortcut shortcut) =>
        CompanionAppNormalization.GetConfigured(shortcut).Count > 0;

    public static bool ShouldLaunchOnWorkspaceOpen(TerminalShortcut shortcut) =>
        CompanionAppNormalization.GetOpenOnLaunch(shortcut).Count > 0;

    /// <summary>Test hook: when set, invoked instead of <see cref="Process.Start"/>.</summary>
    internal static Func<string, string, string, bool>? StartProcessOverride { get; set; }

    /// <summary>Legacy full-shortcut override used by existing tests.</summary>
    internal static Func<TerminalShortcut, bool, bool>? TryLaunchOverride { get; set; }

    internal static bool LastLaunchAttempted { get; private set; }

    /// <summary>Number of companion processes started by the last <see cref="TryLaunch"/> call.</summary>
    internal static int LastLaunchCount { get; private set; }

    public static bool TryLaunch(TerminalShortcut shortcut, bool onDemand, out string? error)
    {
        error = null;
        // Always clear first so callers can observe "was this TryLaunch invoked?"
        // even when a prior test left LastLaunchAttempted true.
        LastLaunchAttempted = false;
        LastLaunchCount = 0;

        if (TryLaunchOverride is { } tryLaunchOverride)
        {
            // Only mark attempted when the override is actually asked to launch
            // (auto path with no open-on-launch companions is a no-op).
            CompanionAppNormalization.EnsureCompanionsFromLegacy(shortcut);
            if (!onDemand && CompanionAppNormalization.GetOpenOnLaunch(shortcut).Count == 0)
            {
                return true;
            }

            LastLaunchAttempted = true;
            LastLaunchCount = 1;
            return tryLaunchOverride(shortcut, onDemand);
        }

        CompanionAppNormalization.EnsureCompanionsFromLegacy(shortcut);

        IReadOnlyList<CompanionAppEntry> targets = onDemand
            ? CompanionAppNormalization.GetConfigured(shortcut)
            : CompanionAppNormalization.GetOpenOnLaunch(shortcut);

        if (!onDemand && targets.Count == 0)
        {
            return true;
        }

        if (targets.Count == 0)
        {
            error = "No companion app is configured for this workspace.";
            return false;
        }

        if (!Directory.Exists(shortcut.Directory))
        {
            error = $"Workspace folder not found: {shortcut.Directory}";
            return false;
        }

        LastLaunchAttempted = true;
        var failures = new List<string>();
        foreach (var entry in targets)
        {
            if (!TryLaunchEntry(entry, shortcut.Directory, out var entryError))
            {
                failures.Add(entryError ?? "Companion app could not be launched.");
                continue;
            }

            LastLaunchCount++;
            RememberPresetFromPath(entry.Path);
        }

        if (failures.Count == 0)
        {
            return true;
        }

        error = failures.Count == 1
            ? failures[0]
            : string.Join(" ", failures);
        // Auto path: soft fail if any failed (even if some succeeded).
        // On-demand: fail if any failed so the user sees the error.
        return false;
    }

    internal static bool TryLaunchEntry(CompanionAppEntry entry, string workspaceDirectory, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(entry.Path))
        {
            error = "No companion app is configured for this workspace.";
            return false;
        }

        if (!CompanionAppCatalog.TryResolveExecutablePath(entry.Path, out var executablePath))
        {
            error = $"Companion app not found: {entry.Path}";
            return false;
        }

        if (!Directory.Exists(workspaceDirectory))
        {
            error = $"Workspace folder not found: {workspaceDirectory}";
            return false;
        }

        try
        {
            var arguments = ExpandArguments(entry.Arguments, workspaceDirectory);
            if (StartProcessOverride is { } startOverride)
            {
                if (!startOverride(executablePath, arguments, workspaceDirectory))
                {
                    error = "Companion app could not be launched.";
                    return false;
                }

                return true;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = workspaceDirectory,
                UseShellExecute = true,
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = "Companion app could not be launched.";
            return false;
        }
    }

    internal static string ExpandArguments(string? arguments, string workspaceDirectory)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        var trimmed = arguments.Trim();
        if (string.Equals(trimmed, ".", StringComparison.Ordinal))
        {
            return QuoteIfNeeded(workspaceDirectory);
        }

        var solution = WorkspaceCompanionSignals.TryFindSolutionFile(workspaceDirectory);
        var expanded = trimmed
            .Replace("{folder}", QuoteIfNeeded(workspaceDirectory), StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{solution}",
                QuoteIfNeeded(solution ?? workspaceDirectory),
                StringComparison.OrdinalIgnoreCase);

        return expanded;
    }

    /// <summary>Display label for status/context UI (primary name, or "A + B" / "N companions").</summary>
    public static string BuildDisplaySummary(TerminalShortcut shortcut)
    {
        var configured = CompanionAppNormalization.GetConfigured(shortcut);
        if (configured.Count == 0)
        {
            return string.Empty;
        }

        if (configured.Count == 1)
        {
            return CompanionAppCatalog.GetDisplayName(configured[0].Path);
        }

        if (configured.Count == 2)
        {
            return $"{CompanionAppCatalog.GetDisplayName(configured[0].Path)} + {CompanionAppCatalog.GetDisplayName(configured[1].Path)}";
        }

        return $"{configured.Count} companions";
    }

    private static void RememberPresetFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var preset = CompanionAppCatalog.InferPresetFromPath(path);
        CompanionAppPreference.RememberPreset(preset);
    }

    private static string QuoteIfNeeded(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
}
