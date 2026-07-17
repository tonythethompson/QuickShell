using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class WorkspaceCompanionSignalsInstance : IWorkspaceCompanionSignals
{
    public bool HasGitRepository(string d) => WorkspaceCompanionSignals.HasGitRepository(d);
    public bool HasVisualStudioSolution(string d) => WorkspaceCompanionSignals.HasVisualStudioSolution(d);
    public string? TryFindSolutionFile(string d) => WorkspaceCompanionSignals.TryFindSolutionFile(d);
    public bool HasSublimeProject(string d) => WorkspaceCompanionSignals.HasSublimeProject(d);
    public bool HasJetBrainsProject(string d) => WorkspaceCompanionSignals.HasJetBrainsProject(d);
    public bool HasZedProject(string d) => WorkspaceCompanionSignals.HasZedProject(d);
    public bool HasKiroProject(string d) => WorkspaceCompanionSignals.HasKiroProject(d);
    public bool HasWindsurfProject(string d) => WorkspaceCompanionSignals.HasWindsurfProject(d);
    public bool HasAntigravityProject(string d) => WorkspaceCompanionSignals.HasAntigravityProject(d);
    public bool HasPackageJson(string d) => WorkspaceCompanionSignals.HasPackageJson(d);
    public bool HasPyprojectToml(string d) => WorkspaceCompanionSignals.HasPyprojectToml(d);
    public bool HasGoMod(string d) => WorkspaceCompanionSignals.HasGoMod(d);
    public bool HasCMakeProject(string d) => WorkspaceCompanionSignals.HasCMakeProject(d);
    public bool HasGradleOrAndroidProject(string d) => WorkspaceCompanionSignals.HasGradleOrAndroidProject(d);
    public bool HasDotNetProject(string d) => WorkspaceCompanionSignals.HasDotNetProject(d);
}
