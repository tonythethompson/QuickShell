using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutCreateNavigationState
{
    private static TerminalShortcut? _seed;

    public static void SetSeed(TerminalShortcut seed) => _seed = seed;

    public static TerminalShortcut? TryTakeSeed()
    {
        var seed = _seed;
        _seed = null;
        return seed;
    }
}
