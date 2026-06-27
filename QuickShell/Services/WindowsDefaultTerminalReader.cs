using System.Text.Json;

namespace QuickShell.Services;

internal static class WindowsDefaultTerminalReader
{
    public static string ReadApplicationId()
    {
        foreach (var location in TerminalSettingsDiscovery.DiscoverLocations())
        {
            try
            {
                using var stream = File.OpenRead(location.SettingsPath);
                using var document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("defaultTerminalApplication", out var property))
                {
                    continue;
                }

                var value = property.GetString()?.Trim();
                return value switch
                {
                    "conhost" => TerminalHostIds.WindowsConsoleHost,
                    "terminal" => TerminalHostIds.WindowsTerminal,
                    _ => TerminalHostIds.WindowsTerminal,
                };
            }
            catch
            {
                // Try the next settings file.
            }
        }

        return TerminalHostIds.WindowsTerminal;
    }
}
