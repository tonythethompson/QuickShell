namespace QuickShell.Services;

/// <summary>One companion app row in create/edit forms (CmdPal, Run, drafts).</summary>
internal sealed class CompanionAppFormRow
{
    public string Id { get; set; } = string.Empty;

    public string Preset { get; set; } = CompanionAppCatalog.PresetNone;

    public string Path { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public bool OpenOnLaunch { get; set; }

    public CompanionAppFormRow Clone() => new()
    {
        Id = Id,
        Preset = Preset,
        Path = Path,
        Arguments = Arguments,
        OpenOnLaunch = OpenOnLaunch,
    };

    public static CompanionAppFormRow Empty() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Preset = CompanionAppCatalog.PresetNone,
    };

    public static CompanionAppFormRow FromFormState(
        CompanionAppCatalog.CompanionAppFormState state,
        string? id = null) =>
        new()
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            Preset = state.Preset,
            Path = state.Path,
            Arguments = state.Arguments,
            OpenOnLaunch = state.LaunchOnWorkspaceOpen,
        };
}
