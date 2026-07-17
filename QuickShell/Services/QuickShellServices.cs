using System.Text.Json;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

/// <summary>
/// CmdPal host facade seeded from the composition root at provider startup.
/// Pages and commands receive this instance through constructor injection.
/// </summary>
internal sealed class QuickShellServices : IQuickShellServices
{
    public IShortcutRepository Shortcuts { get; }

    public IDraftStore Drafts { get; }

    public QuickShellSettingsManager Settings { get; }

    public IProjectAnalysisService ProjectAnalysis { get; }

    public IQuickShellLifetime Lifetime { get; }

    public QuickShellServices(
        IShortcutRepository shortcuts,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService projectAnalysis,
        IQuickShellLifetime lifetime)
    {
        Shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        Drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ProjectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        settings.InitializeServices(this);
        BeginShortcutPreload();
    }

    private void BeginShortcutPreload() => _ = PreloadShortcutsAsync();

    private async Task PreloadShortcutsAsync()
    {
        try
        {
            await Shortcuts.PreloadAsync(Lifetime.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or OperationCanceledException)
        {
            // Best effort warm-up; synchronous access still loads on demand.
        }
    }
}
