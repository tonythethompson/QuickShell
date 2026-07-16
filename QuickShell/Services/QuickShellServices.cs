using System.Text.Json;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

/// <summary>
/// CmdPal host facade seeded from the composition root at provider startup.
/// Pages and commands resolve shared singletons through <see cref="Current"/>.
/// </summary>
internal sealed class QuickShellServices
{
    private static QuickShellServices? _current;

    public static QuickShellServices Current =>
        _current ?? throw new InvalidOperationException(
            $"{nameof(QuickShellServices)} has not been initialized by the CmdPal provider.");

    internal static void Bind(QuickShellServices instance) =>
        _current = instance ?? throw new ArgumentNullException(nameof(instance));

    internal static void Unbind() => _current = null;

    public ShortcutRepository Shortcuts { get; }

    public ShortcutDraftStore Drafts { get; }

    public QuickShellSettingsManager Settings { get; }

    public IProjectAnalysisService ProjectAnalysis { get; }

    public IQuickShellLifetime Lifetime { get; }

    public QuickShellServices(
        ShortcutRepository shortcuts,
        ShortcutDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService projectAnalysis,
        IQuickShellLifetime? lifetime = null)
    {
        Shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        Drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ProjectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        Lifetime = lifetime ?? new QuickShellLifetime();
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
