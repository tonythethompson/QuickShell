namespace QuickShell.Services;

internal static class WorkspaceCompanionSignals
{
    public static bool HasGitRepository(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git"));

    public static bool HasVisualStudioSolution(string directory)
    {
        if (Directory.Exists(Path.Combine(directory, ".vs")))
        {
            return true;
        }

        return TryFindSolutionFile(directory) is not null;
    }

    public static string? TryFindSolutionFile(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(directory, "*.sln", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static bool HasSublimeProject(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.sublime-project", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }

    public static bool HasJetBrainsProject(string directory) =>
        Directory.Exists(Path.Combine(directory, ".idea"));

    public static bool HasZedProject(string directory) =>
        Directory.Exists(Path.Combine(directory, ".zed"));

    public static bool HasKiroProject(string directory) =>
        Directory.Exists(Path.Combine(directory, ".kiro"));

    public static bool HasWindsurfProject(string directory) =>
        Directory.Exists(Path.Combine(directory, ".windsurf"))
        || Directory.Exists(Path.Combine(directory, ".codeium"));

    public static bool HasAntigravityProject(string directory) =>
        Directory.Exists(Path.Combine(directory, ".antigravity"))
        || Directory.Exists(Path.Combine(directory, ".agy"));

    public static bool HasPackageJson(string directory) =>
        File.Exists(Path.Combine(directory, "package.json"));

    public static bool HasPyprojectToml(string directory) =>
        File.Exists(Path.Combine(directory, "pyproject.toml"))
        || File.Exists(Path.Combine(directory, "Pipfile"))
        || File.Exists(Path.Combine(directory, "requirements.txt"));

    public static bool HasGoMod(string directory) =>
        File.Exists(Path.Combine(directory, "go.mod"));

    public static bool HasCMakeProject(string directory) =>
        File.Exists(Path.Combine(directory, "CMakeLists.txt"));

    public static bool HasGradleOrAndroidProject(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        if (File.Exists(Path.Combine(directory, "build.gradle"))
            || File.Exists(Path.Combine(directory, "build.gradle.kts"))
            || File.Exists(Path.Combine(directory, "settings.gradle"))
            || File.Exists(Path.Combine(directory, "settings.gradle.kts"))
            || File.Exists(Path.Combine(directory, "AndroidManifest.xml"))
            || File.Exists(Path.Combine(directory, "app", "src", "main", "AndroidManifest.xml")))
        {
            return true;
        }

        return false;
    }

    public static bool HasDotNetProject(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        if (TryFindSolutionFile(directory) is not null)
        {
            return true;
        }

        if (File.Exists(Path.Combine(directory, "global.json")))
        {
            return true;
        }

        try
        {
            return Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateFiles(directory, "*.fsproj", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }
}
