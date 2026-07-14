using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification;

internal sealed class ProjectLayoutAnalyzer : IProjectLayoutAnalyzer
{
    internal static ProjectLayoutAnalyzer Default { get; } = new();

    public ProjectLayout Analyze(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return Empty(rootPath);
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
            HasCsproj: HasAnyTopLevelFile(rootPath, "*.csproj"),
            HasTaskfile: HasAnyFile(rootPath, "Taskfile.yml", "Taskfile.yaml"),
            HasMakefile: HasAnyFile(rootPath, "Makefile", "makefile"),
            HasJustfile: HasAnyFile(rootPath, "justfile", "Justfile"),
            HasCargoToml: File.Exists(Path.Combine(rootPath, "Cargo.toml")),
            HasPyprojectToml: File.Exists(Path.Combine(rootPath, "pyproject.toml")),
            HasRequirementsTxt: File.Exists(Path.Combine(rootPath, "requirements.txt")),
            HasSetupPy: File.Exists(Path.Combine(rootPath, "setup.py")),
            HasGoMod: File.Exists(Path.Combine(rootPath, "go.mod")),
            HasPomXml: File.Exists(Path.Combine(rootPath, "pom.xml")),
            HasGradleBuild: HasAnyFile(rootPath, "build.gradle", "build.gradle.kts"),
            HasDenoJson: HasAnyFile(rootPath, "deno.json", "deno.jsonc"),
            HasProcfile: File.Exists(Path.Combine(rootPath, "Procfile")),
            HasGemfile: File.Exists(Path.Combine(rootPath, "Gemfile")),
            HasMixExs: File.Exists(Path.Combine(rootPath, "mix.exs")),
            HasVsCodeDirectory: Directory.Exists(Path.Combine(rootPath, ".vscode")),
            HasDevContainerDirectory: Directory.Exists(Path.Combine(rootPath, ".devcontainer")),
            HasDevContainerJson: File.Exists(Path.Combine(rootPath, "devcontainer.json")),
            HasCodeWorkspace: HasAnyTopLevelFile(rootPath, "*.code-workspace"),
            HasCursorDirectory: Directory.Exists(Path.Combine(rootPath, ".cursor")),
            HasObsidianDirectory: Directory.Exists(Path.Combine(rootPath, ".obsidian")),
            HasZedDirectory: Directory.Exists(Path.Combine(rootPath, ".zed")),
            HasIdeaDirectory: Directory.Exists(Path.Combine(rootPath, ".idea")),
            HasSublimeProject: HasAnyTopLevelFile(rootPath, "*.sublime-project"),
            HasSolutionFile: HasAnyTopLevelFile(rootPath, "*.sln")
                || Directory.Exists(Path.Combine(rootPath, ".vs")));
    }

    private static ProjectLayout Empty(string rootPath) =>
        new(
            rootPath,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false);

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

    private static bool HasAnyTopLevelFile(string rootPath, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(rootPath, pattern, SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }
}
