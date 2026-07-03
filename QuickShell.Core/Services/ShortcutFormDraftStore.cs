using QuickShell.Models;
using System.Text.Json.Serialization;

namespace QuickShell.Services;

internal sealed class ShortcutFormDraftData
{
    public string OriginalName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Abbreviation { get; set; } = string.Empty;

    public string Directory { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string LaunchTarget { get; set; } = "default";

    public string DevServerUrl { get; set; } = string.Empty;

    public string RepoUrl { get; set; } = string.Empty;

    public bool OpenDevServerOnLaunch { get; set; }

    public bool OpenCompanionAppOnLaunch { get; set; }

    public string CompanionAppPreset { get; set; } = CompanionAppCatalog.PresetNone;

    public string CompanionAppPath { get; set; } = string.Empty;

    public string CompanionAppArguments { get; set; } = string.Empty;

    public bool RunAsAdmin { get; set; }

    public List<ShortcutFormLaunchDraftData> Launches { get; set; } = [];

    public static ShortcutFormDraftData FromPersisted(PersistedShortcutEditDraft draft)
    {
        var data = new ShortcutFormDraftData
        {
            OriginalName = draft.OriginalName,
            Name = draft.Name,
            Abbreviation = draft.Abbreviation,
            Directory = draft.Directory,
            Command = draft.Command,
            LaunchTarget = draft.LaunchTarget,
            DevServerUrl = draft.DevServerUrl,
            RepoUrl = draft.RepoUrl,
            OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
            OpenCompanionAppOnLaunch = draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = draft.CompanionAppPreset,
            CompanionAppPath = draft.CompanionAppPath,
            CompanionAppArguments = draft.CompanionAppArguments,
            RunAsAdmin = draft.RunAsAdmin,
        };

        if (draft.Launches is { Count: > 0 })
        {
            data.Launches = draft.Launches
                .Select(launch => new ShortcutFormLaunchDraftData
                {
                    Id = launch.Id,
                    Label = launch.Label,
                    Command = launch.Command,
                    LaunchTarget = launch.LaunchTarget,
                    RunAsAdmin = launch.RunAsAdmin,
                    IsEnabled = launch.IsEnabled,
                })
                .ToList();
        }

        return data;
    }
}

internal sealed class ShortcutFormLaunchDraftData
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string LaunchTarget { get; set; } = "default";

    public bool RunAsAdmin { get; set; }

    public bool IsEnabled { get; set; } = true;
}

internal sealed class PersistedShortcutEditDraft
{
    public string OriginalName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Abbreviation { get; set; } = string.Empty;

    public string Directory { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string LaunchTarget { get; set; } = "default";

    public bool NameCustomized { get; set; }

    public string? AutoFilledName { get; set; }

    public bool RunAsAdmin { get; set; }

    public string DevServerUrl { get; set; } = string.Empty;

    public string RepoUrl { get; set; } = string.Empty;

    public bool OpenDevServerOnLaunch { get; set; }

    public bool OpenCompanionAppOnLaunch { get; set; }

    public string CompanionAppPreset { get; set; } = CompanionAppCatalog.PresetNone;

    public string CompanionAppPath { get; set; } = string.Empty;

    public string CompanionAppArguments { get; set; } = string.Empty;

    public List<PersistedShortcutLaunchDraft> Launches { get; set; } = [];
}

internal sealed class PersistedShortcutLaunchDraft
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string LaunchTarget { get; set; } = "default";

    public bool RunAsAdmin { get; set; }

    public bool IsEnabled { get; set; } = true;
}

internal sealed class ShortcutSaveResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public static ShortcutSaveResult Ok(string message) =>
        new() { Success = true, Message = message };

    public static ShortcutSaveResult Fail(string message) =>
        new() { Success = false, Message = message };
}

internal static class ShortcutFormSave
{
    /// <summary>
    /// Saves from the PowerToys Run simple editor. Creates use a single launch; edits update
    /// shared fields and the primary (first enabled) launch while preserving other launches
    /// and companion/link metadata.
    /// </summary>
    public static ShortcutSaveResult TrySaveRunEditor(
        TerminalShortcut? existing,
        string? originalName,
        string name,
        string abbreviation,
        string directory,
        string command,
        string launchTarget,
        bool runAsAdmin,
        IShortcutRepository shortcuts,
        Action? onSaved)
    {
        if (existing is null)
        {
            return TrySave(
                originalName,
                name,
                abbreviation,
                directory,
                command,
                launchTarget,
                runAsAdmin,
                shortcuts,
                onSaved);
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            return ShortcutSaveResult.Fail("Folder path is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = DeriveNameFromDirectory(directory);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return ShortcutSaveResult.Fail("Name is required.");
        }

        name = name.Trim();
        var resolvedName = shortcuts.ResolveAvailableName(name, originalName);
        var renamedForConflict = !string.Equals(resolvedName, name, StringComparison.OrdinalIgnoreCase);

        var shortcut = CloneShortcut(existing);
        shortcut.Name = resolvedName;
        shortcut.Abbreviation = string.IsNullOrWhiteSpace(abbreviation) ? null : abbreviation.Trim();
        shortcut.Directory = directory.Trim();

        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
        var primary = GetPrimaryLaunch(shortcut);
        primary.Command = string.IsNullOrWhiteSpace(command) ? null : command.Trim();
        primary.RunAsAdmin = runAsAdmin;

        var launchScratch = new TerminalShortcut();
        TerminalCatalog.ApplyLaunchTargetId(launchScratch, launchTarget);
        primary.Terminal = launchScratch.Terminal;
        primary.WtProfile = launchScratch.WtProfile;

        ShortcutLaunchNormalization.NormalizeShortcut(shortcut);

        if (!ShortcutValidation.TryValidate(shortcut, out var validationError))
        {
            return ShortcutSaveResult.Fail(validationError);
        }

        try
        {
            shortcuts.Upsert(shortcut, originalName);
            onSaved?.Invoke();

            var extraLaunches = shortcut.Launches.Count(entry => entry.IsEnabled) - 1;
            var preservedNote = extraLaunches > 0
                ? $" ({extraLaunches} other launch{(extraLaunches == 1 ? string.Empty : "es")} preserved)"
                : string.Empty;
            var message = renamedForConflict
                ? $"Saved workspace as '{resolvedName}' (name was already in use).{preservedNote}"
                : $"Saved workspace '{resolvedName}'.{preservedNote}";
            return ShortcutSaveResult.Ok(message);
        }
        catch (IOException)
        {
            return ShortcutSaveResult.Fail("Failed to save workspace: unable to write workspace data.");
        }
        catch (UnauthorizedAccessException)
        {
            return ShortcutSaveResult.Fail("Failed to save workspace: access to workspace storage was denied.");
        }
        catch (InvalidOperationException)
        {
            return ShortcutSaveResult.Fail("Failed to save workspace: workspace data is invalid.");
        }
    }

    public static WorkspaceEntry GetPrimaryLaunchForRunEditor(TerminalShortcut shortcut)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
        return GetPrimaryLaunch(shortcut);
    }

    public static string EncodeLaunchTargetForEntry(WorkspaceEntry entry)
    {
        var scratch = new TerminalShortcut
        {
            Terminal = entry.Terminal,
            WtProfile = entry.WtProfile,
        };
        return TerminalCatalog.EncodeLaunchTargetId(scratch);
    }

    public static ShortcutSaveResult TrySave(
        string? originalName,
        string name,
        string abbreviation,
        string directory,
        string command,
        string launchTarget,
        bool runAsAdmin,
        IShortcutRepository shortcuts,
        Action? onSaved) =>
        TrySave(
            originalName,
            name,
            abbreviation,
            directory,
            [
                new ShortcutFormLaunchInput
                {
                    Label = string.IsNullOrWhiteSpace(name) ? "Main" : name.Trim(),
                    Command = command,
                    LaunchTarget = launchTarget,
                    RunAsAdmin = runAsAdmin,
                    IsEnabled = true,
                },
            ],
            shortcuts,
            onSaved);

    public static ShortcutSaveResult TrySave(
        string? originalName,
        string name,
        string abbreviation,
        string directory,
        IReadOnlyList<ShortcutFormLaunchInput> launches,
        IShortcutRepository shortcuts,
        Action? onSaved,
        string? devServerUrl = null,
        string? repoUrl = null,
        bool openDevServerOnLaunch = false,
        bool openCompanionAppOnLaunch = false,
        string? companionAppPath = null,
        string? companionAppArguments = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return ShortcutSaveResult.Fail("Folder path is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = DeriveNameFromDirectory(directory);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return ShortcutSaveResult.Fail("Name is required.");
        }

        if (launches.Count == 0)
        {
            return ShortcutSaveResult.Fail("At least one launch entry is required.");
        }

        name = name.Trim();
        var resolvedName = shortcuts.ResolveAvailableName(name, originalName);
        var renamedForConflict = !string.Equals(resolvedName, name, StringComparison.OrdinalIgnoreCase);

        var existing = string.IsNullOrWhiteSpace(originalName)
            ? null
            : shortcuts.GetByName(originalName);

        var shortcut = new TerminalShortcut
        {
            Id = existing?.Id ?? string.Empty,
            Name = resolvedName,
            Abbreviation = string.IsNullOrWhiteSpace(abbreviation) ? null : abbreviation.Trim(),
            Directory = directory.Trim(),
            DevServerUrl = string.IsNullOrWhiteSpace(devServerUrl) ? null : devServerUrl.Trim(),
            RepoUrl = string.IsNullOrWhiteSpace(repoUrl) ? null : repoUrl.Trim(),
            OpenDevServerOnLaunch = openDevServerOnLaunch && !string.IsNullOrWhiteSpace(devServerUrl),
            OpenCompanionAppOnLaunch = openCompanionAppOnLaunch,
            CompanionAppPath = string.IsNullOrWhiteSpace(companionAppPath) ? null : companionAppPath.Trim(),
            CompanionAppArguments = string.IsNullOrWhiteSpace(companionAppArguments) ? null : companionAppArguments.Trim(),
            Launches = launches.Select((launch, index) =>
            {
                var entry = new WorkspaceEntry
                {
                    Id = string.IsNullOrWhiteSpace(launch.Id) ? Guid.NewGuid().ToString("N") : launch.Id,
                    Label = launch.Label.Trim(),
                    Command = string.IsNullOrWhiteSpace(launch.Command) ? null : launch.Command,
                    RunAsAdmin = launch.RunAsAdmin,
                    IsEnabled = launch.IsEnabled,
                    Order = index,
                };

                var scratch = new TerminalShortcut();
                TerminalCatalog.ApplyLaunchTargetId(scratch, launch.LaunchTarget);
                entry.Terminal = scratch.Terminal;
                entry.WtProfile = scratch.WtProfile;
                return entry;
            }).ToList(),
        };

        ShortcutLaunchNormalization.NormalizeShortcut(shortcut);

        if (!ShortcutValidation.TryValidate(shortcut, out var validationError))
        {
            return ShortcutSaveResult.Fail(validationError);
        }

        try
        {
            shortcuts.Upsert(shortcut, originalName);
            onSaved?.Invoke();
            var message = renamedForConflict
                ? $"Saved workspace as '{resolvedName}' (name was already in use)."
                : $"Saved workspace '{resolvedName}'.";
            return ShortcutSaveResult.Ok(message);
        }
        catch (IOException)
        {
            return ShortcutSaveResult.Fail("Failed to save workspace: unable to write workspace data.");
        }
        catch (UnauthorizedAccessException)
        {
            return ShortcutSaveResult.Fail("Failed to save workspace: access to workspace storage was denied.");
        }
        catch (InvalidOperationException)
        {
            return ShortcutSaveResult.Fail("Failed to save workspace: workspace data is invalid.");
        }
    }

    private static string DeriveNameFromDirectory(string directory)
    {
        var trimmed = directory.Trim().TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
    }

    private static WorkspaceEntry GetPrimaryLaunch(TerminalShortcut shortcut) =>
        shortcut.Launches
            .Where(entry => entry.IsEnabled)
            .OrderBy(entry => entry.Order)
            .FirstOrDefault()
        ?? shortcut.Launches.OrderBy(entry => entry.Order).First();

    private static TerminalShortcut CloneShortcut(TerminalShortcut source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Abbreviation = source.Abbreviation,
        Directory = source.Directory,
        Command = source.Command,
        Terminal = source.Terminal,
        WtProfile = source.WtProfile,
        RunAsAdmin = source.RunAsAdmin,
        IsPinned = source.IsPinned,
        PinOrder = source.PinOrder,
        LastUsedUtc = source.LastUsedUtc,
        Launches = source.Launches.Select(WorkspaceMapper.CloneEntry).ToList(),
        DevServerUrl = source.DevServerUrl,
        RepoUrl = source.RepoUrl,
        OpenCompanionAppOnLaunch = source.OpenCompanionAppOnLaunch,
        CompanionAppPath = source.CompanionAppPath,
        CompanionAppArguments = source.CompanionAppArguments,
    };
}

internal sealed class ShortcutFormLaunchInput
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string LaunchTarget { get; set; } = "default";

    public bool RunAsAdmin { get; set; }

    public bool IsEnabled { get; set; } = true;
}

[JsonSerializable(typeof(PersistedShortcutEditDraft))]
internal sealed partial class ShortcutFormDraftJsonContext : JsonSerializerContext;
