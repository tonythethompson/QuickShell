using QuickShell.Abstractions;
using QuickShell.Models;
using System.Diagnostics;

namespace QuickShell.Services;

internal sealed class CompanionAppLauncher : ICompanionAppLauncher
{
    private readonly IProcessStarter _processStarter;

    public CompanionAppLauncher(IProcessStarter processStarter)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    /// <summary>Whether the last <see cref="TryLaunch"/> attempted to start a companion process.</summary>
    internal bool LastLaunchAttempted { get; private set; }

    /// <summary>Number of companion processes started by the last <see cref="TryLaunch"/> call.</summary>
    internal int LastLaunchCount { get; private set; }

    public bool IsConfigured(TerminalShortcut shortcut) =>
        CompanionAppNormalization.GetConfigured(shortcut).Count > 0;

    public bool ShouldLaunchOnWorkspaceOpen(TerminalShortcut shortcut) =>
        CompanionAppNormalization.GetOpenOnLaunch(shortcut).Count > 0;

    public bool TryLaunch(TerminalShortcut shortcut, bool onDemand, out string? error)
    {
        var result = Launch(shortcut, onDemand);
        error = result.Error;
        return result.Success;
    }

    public CompanionLaunchResult Launch(TerminalShortcut shortcut, bool onDemand)
    {
        // Always clear first so callers can observe "was this TryLaunch invoked?"
        // even when a prior call left LastLaunchAttempted true.
        LastLaunchAttempted = false;
        LastLaunchCount = 0;

        CompanionAppNormalization.EnsureCompanionsFromLegacy(shortcut);

        IReadOnlyList<CompanionAppEntry> targets = onDemand
            ? CompanionAppNormalization.GetConfigured(shortcut)
            : CompanionAppNormalization.GetOpenOnLaunch(shortcut);

        if (!onDemand && targets.Count == 0)
        {
            return new CompanionLaunchResult(true, [], Error: null);
        }

        if (targets.Count == 0)
        {
            return new CompanionLaunchResult(
                false,
                [],
                "No companion app is configured for this workspace.");
        }

        if (!Directory.Exists(shortcut.Directory))
        {
            return new CompanionLaunchResult(
                false,
                [],
                $"Workspace folder not found: {shortcut.Directory}");
        }

        LastLaunchAttempted = true;
        var failures = new List<string>();
        var startedExecutables = new List<string>();
        foreach (var entry in targets)
        {
            if (!TryLaunchEntry(entry, shortcut.Directory, out var startedExecutable, out var entryError))
            {
                failures.Add(entryError ?? "Companion app could not be launched.");
                continue;
            }

            LastLaunchCount++;
            startedExecutables.Add(startedExecutable!);
            RememberPresetFromPath(entry.Path);
        }

        if (failures.Count == 0)
        {
            return new CompanionLaunchResult(true, startedExecutables, Error: null);
        }

        var error = failures.Count == 1
            ? failures[0]
            : string.Join(" ", failures);
        // Auto path: soft fail if any failed (even if some succeeded).
        // On-demand: fail if any failed so the user sees the error.
        return new CompanionLaunchResult(false, startedExecutables, error);
    }

    internal bool TryLaunchEntry(
        CompanionAppEntry entry,
        string workspaceDirectory,
        out string? startedExecutable,
        out string? error)
    {
        startedExecutable = null;
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
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = workspaceDirectory,
                UseShellExecute = true,
            };

            if (!_processStarter.TryStart(startInfo))
            {
                error = "Companion app could not be launched.";
                return false;
            }

            startedExecutable = Path.GetFileName(executablePath);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = "Companion app could not be launched.";
            return false;
        }
    }

    public static string ExpandArguments(string? arguments, string workspaceDirectory)
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
    public string BuildDisplaySummary(TerminalShortcut shortcut)
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
