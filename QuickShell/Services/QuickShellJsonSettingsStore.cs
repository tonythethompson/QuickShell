using Microsoft.CommandPalette.Extensions.Toolkit;

namespace QuickShell.Services;

internal sealed class QuickShellJsonSettingsStore : JsonSettingsManager
{
    public QuickShellJsonSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickShell");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "settings.json");
    }
}
