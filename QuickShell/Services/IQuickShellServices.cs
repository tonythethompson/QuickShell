using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

/// <summary>
/// Host-facing service facade for the CmdPal extension. Constructor-injectable
/// singleton so pages and commands can be unit-tested in isolation.
/// </summary>
internal interface IQuickShellServices
{
    IShortcutRepository Shortcuts { get; }

    IDraftStore Drafts { get; }

    QuickShellSettingsManager Settings { get; }

    IProjectAnalysisService ProjectAnalysis { get; }
}
