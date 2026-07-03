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
