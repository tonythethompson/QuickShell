using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification;

internal sealed class ProjectLayoutAnalyzer : IProjectLayoutAnalyzer
{
    internal static ProjectLayoutAnalyzer Default { get; } = new();

    public ProjectLayout Analyze(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return new ProjectLayout(rootPath, false, false, false, false, false, false, false);
        }

        return new ProjectLayout(
            RootPath: rootPath,
            HasGit: Directory.Exists(Path.Combine(rootPath, ".git")),
            HasDockerCompose: HasAnyFile(
                rootPath,
                "docker-compose.yml",
                "docker-compose.yaml",
                "compose.yml",
                "compose.yaml"),
            HasPackageJson: File.Exists(Path.Combine(rootPath, "package.json")),
            HasCsproj: Directory
                .EnumerateFiles(rootPath, "*.csproj", SearchOption.TopDirectoryOnly)
                .Any(),
            HasTaskfile: HasAnyFile(rootPath, "Taskfile.yml", "Taskfile.yaml"),
            HasMakefile: HasAnyFile(rootPath, "Makefile", "makefile"),
            HasJustfile: HasAnyFile(rootPath, "justfile", "Justfile"));
    }

    private static bool HasAnyFile(string rootPath, params string[] names)
    {
        foreach (var name in names)
        {
            if (File.Exists(Path.Combine(rootPath, name)))
            {
                return true;
            }
        }

        return false;
    }
}
