using System.Threading.Tasks;

namespace QuickShell.Services;

/// <summary>
/// Compatibility shim for CmdPal pages/commands. Instances are owned by the
/// composition root (<see cref="Composition.QuickShellServiceCollectionExtensions"/>)
/// when seeded via <see cref="Attach"/>.
/// </summary>
internal static class QuickShellRuntimeServices
{
    private static bool _ownedByServiceProvider;

    public static QuickShellSettingsManager? Settings { get; private set; }

    public static ShortcutRepository Shortcuts { get; private set; } = null!;

    public static ShortcutDraftStore Drafts { get; private set; } = null!;

    /// <summary>
    /// Seeds the shim with DI-resolved singletons. Call once before <see cref="Initialize"/>.
    /// </summary>
    internal static void Attach(ShortcutRepository shortcuts, ShortcutDraftStore drafts, bool ownedByServiceProvider = true)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(drafts);

        Shortcuts = shortcuts;
        Drafts = drafts;
        _ownedByServiceProvider = ownedByServiceProvider;
    }

    internal static void Initialize(QuickShellSettingsManager settings)
    {
        if (Shortcuts is null || Drafts is null)
        {
            throw new InvalidOperationException(
                $"{nameof(QuickShellRuntimeServices)} must be seeded via {nameof(Attach)} before {nameof(Initialize)}.");
        }

        Settings = settings;
        BeginShortcutPreload();
    }

    private static void BeginShortcutPreload() => _ = PreloadShortcutsAsync();

    private static async Task PreloadShortcutsAsync()
    {
        try
        {
            await Shortcuts.PreloadAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort warm-up; synchronous access still loads on demand.
        }
    }

    public static void Dispose()
    {
        Settings = null;

        // When instances come from IServiceProvider, the provider owns disposal.
        if (_ownedByServiceProvider)
        {
            return;
        }

        Drafts?.Dispose();
        Shortcuts?.Dispose();
    }
}
