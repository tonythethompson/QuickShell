namespace QuickShell.Services;

internal static class WorkspaceFormTooltips
{
    public const string Directory =
        "Folder opened when you run this workspace. Browse or paste to pick a folder.";

    public const string DirectoryExample = "e.g. C:\\Projects\\MyApp";

    public const string Name =
        "Shown in your workspace list. Auto-filled from the folder name when you browse or paste.";

    public const string HomeKeyword =
        "Type at Command Palette home to jump straight to this workspace.";

    public const string HomeKeywordExample = "e.g. api";

    public const string DevServerUrl =
        "Opens in your browser when you run this workspace.";

    public const string DevServerUrlExample = "e.g. http://localhost:3000";

    public const string DevServerOnLaunch =
        "When enabled, opens the dev server URL in your browser whenever you run the workspace.";

    public const string RepoUrl =
        "Opens from the workspace action menu.";

    public const string RepoUrlExample = "e.g. https://github.com/you/your-repo";

    public const string CompanionAppPreset =
        "Optionally open an editor or other app with this workspace folder when you run the workspace.";

    public const string RunAsAdmin =
        "Launch elevated. Windows may show a UAC prompt each time.";

    public const string SuggestedCommands =
        "Click to add a launch row. Based on files in this folder.";
}
