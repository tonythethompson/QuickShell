namespace QuickShell.Services;

internal static class QuickShellRuntimeServices
{
    public static QuickShellSettingsManager? Settings { get; private set; }

    public static ShortcutRepository Shortcuts { get; } = new();

    public static ShortcutDraftStore Drafts { get; } = new(Shortcuts);

    internal static void Initialize(QuickShellSettingsManager settings) => Settings = settings;

    public static void Dispose()
    {
        Drafts.Dispose();
        Shortcuts.Dispose();
    }
}
