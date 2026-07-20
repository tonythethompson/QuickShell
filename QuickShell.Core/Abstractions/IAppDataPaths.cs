namespace QuickShell.Abstractions;

/// <summary>
/// The root app-data directory QuickShell services resolve their storage paths under.
/// Production resolves to <c>%LOCALAPPDATA%</c>; tests inject a temp root instead of
/// mutating the process-wide environment variable.
/// </summary>
internal interface IAppDataPaths
{
    string Root { get; }
}
