using System.Threading.Tasks;

namespace QuickShell.Services;

internal static class QuickShellRuntimeServices
{
    public static QuickShellSettingsManager? Settings { get; private set; }

    public static ShortcutRepository Shortcuts { get; } = new();

    public static ShortcutDraftStore Drafts { get; } = new(Shortcuts);

    internal static void Initialize(QuickShellSettingsManager settings)
    {
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
        Drafts.Dispose();
        Shortcuts.Dispose();
    }
}
