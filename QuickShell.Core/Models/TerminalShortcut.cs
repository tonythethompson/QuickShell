namespace QuickShell.Models;

internal sealed class TerminalShortcut
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Abbreviation { get; set; }

    public string Directory { get; set; } = string.Empty;

    public string? Command { get; set; }

    // "default" means use extension-level default terminal setting.

    public string Terminal { get; set; } = "default";

    // Optional Windows Terminal profile name for terminal=wt/default->wt.

    public string? WtProfile { get; set; }

    public bool RunAsAdmin { get; set; }

    public bool IsPinned { get; set; }

    // Lower number means higher in the favorites section.

    public int? PinOrder { get; set; }

    public DateTime? LastUsedUtc { get; set; }

    /// <summary>One or more terminal launches for this workspace. When empty on disk, synthesized from legacy fields on load.</summary>
    public List<WorkspaceEntry> Launches { get; set; } = [];

    /// <summary>Optional dev server URL opened in the browser when the workspace runs.</summary>
    public string? DevServerUrl { get; set; }

    /// <summary>When true, opens <see cref="DevServerUrl"/> whenever the full workspace runs.</summary>
    public bool OpenDevServerOnLaunch { get; set; }

    /// <summary>Optional repository URL opened from the workspace action menu.</summary>
    public string? RepoUrl { get; set; }

    /// <summary>
    /// One or more GUI companion apps. When empty on disk, synthesized from the legacy
    /// scalar companion fields on load (see <c>CompanionAppNormalization</c>).
    /// </summary>
    public List<CompanionAppEntry> CompanionApps { get; set; } = [];

    /// <summary>
    /// When true, opens the primary companion whenever the full workspace runs.
    /// Mirrored from the first <see cref="CompanionApps"/> entry; prefer per-entry
    /// <see cref="CompanionAppEntry.OpenOnLaunch"/> for multi-companion workspaces.
    /// </summary>
    public bool OpenCompanionAppOnLaunch { get; set; }

    /// <summary>
    /// Executable path for the primary companion app (editor, notes app, etc.).
    /// Mirrored from the first <see cref="CompanionApps"/> entry.
    /// </summary>
    public string? CompanionAppPath { get; set; }

    /// <summary>
    /// Optional arguments for the primary companion. Use <c>.</c> or <c>{folder}</c>
    /// for the workspace directory. Mirrored from the first <see cref="CompanionApps"/> entry.
    /// </summary>
    public string? CompanionAppArguments { get; set; }
}


