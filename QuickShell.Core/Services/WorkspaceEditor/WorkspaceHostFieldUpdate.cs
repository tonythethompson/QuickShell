namespace QuickShell.Services.WorkspaceEditor;

/// <summary>
/// Typed field snapshot for non-Adaptive-Card hosts (e.g. PowerToys Run WPF).
/// </summary>
internal sealed class WorkspaceHostFieldUpdate
{
    public string Name { get; init; } = string.Empty;

    public string Abbreviation { get; init; } = string.Empty;

    public string Directory { get; init; } = string.Empty;

    public string DevServerUrl { get; init; } = string.Empty;

    public bool OpenDevServerOnLaunch { get; init; }

    public string RepoUrl { get; init; } = string.Empty;

    public IReadOnlyList<LaunchRowDraft> Commands { get; init; } = [];

    public IReadOnlyList<CompanionAppFormRow> Companions { get; init; } = [];
}
