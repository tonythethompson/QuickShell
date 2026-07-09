namespace QuickShell.Classification;

internal sealed record ProjectLayout(
    string RootPath,
    bool HasGit,
    bool HasDockerCompose,
    bool HasPackageJson,
    bool HasCsproj,
    bool HasTaskfile,
    bool HasMakefile,
    bool HasJustfile);
