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



    /// <summary>Optional dev server URL opened from the workspace action menu.</summary>

    public string? DevServerUrl { get; set; }



    /// <summary>When true, opens <see cref="DevServerUrl"/> whenever the full workspace runs.</summary>

    public bool OpenDevServerOnLaunch { get; set; }



    /// <summary>Optional repository URL opened from the workspace action menu.</summary>

    public string? RepoUrl { get; set; }



    /// <summary>When true, opens <see cref="CompanionAppPath"/> whenever the full workspace runs.</summary>

    public bool OpenCompanionAppOnLaunch { get; set; }



    /// <summary>Executable path for the workspace companion app (editor, notes app, etc.).</summary>

    public string? CompanionAppPath { get; set; }



    /// <summary>Optional arguments. Use <c>.</c> or <c>{folder}</c> for the workspace directory.</summary>

    public string? CompanionAppArguments { get; set; }

}

