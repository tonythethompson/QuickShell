using Microsoft.CommandPalette.Extensions.Toolkit;

namespace QuickShell.Services;

internal sealed class QuickShellJsonSettingsStore : JsonSettingsManager
{
    public QuickShellJsonSettingsStore()
        : this(configDirectory: null)
    {
    }

    internal QuickShellJsonSettingsStore(string? configDirectory)
    {
        var directory = configDirectory
            ?? Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickShell");
        Directory.CreateDirectory(directory);
        FilePath = Path.Join(directory, "settings.json");
    }
}
