namespace QuickShell.Abstractions;

internal interface IWorkspaceCompanionSignals
{
    bool HasGitRepository(string directory);
    bool HasVisualStudioSolution(string directory);
    string? TryFindSolutionFile(string directory);
    bool HasSublimeProject(string directory);
    bool HasJetBrainsProject(string directory);
    bool HasZedProject(string directory);
    bool HasKiroProject(string directory);
    bool HasWindsurfProject(string directory);
    bool HasAntigravityProject(string directory);
    bool HasPackageJson(string directory);
    bool HasPyprojectToml(string directory);
    bool HasGoMod(string directory);
    bool HasCMakeProject(string directory);
    bool HasGradleOrAndroidProject(string directory);
    bool HasDotNetProject(string directory);
}
