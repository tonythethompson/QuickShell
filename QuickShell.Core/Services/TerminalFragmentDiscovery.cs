using System.Text.Json;

namespace QuickShell.Services;

/// <summary>
/// Holds fragment-provided values for a single Windows Terminal profile.
/// Windows Terminal fragments supply base properties (commandline, icon) that
/// are merged with the user's settings.json profile by guid.
/// </summary>
internal sealed class TerminalFragmentProfile
{
    public string? Commandline { get; init; }

    public string? Icon { get; init; }
}

/// <summary>
/// Discovers Windows Terminal fragment JSON files and loads their profile data.
/// Fragment files live under %ProgramData%\Microsoft\Windows Terminal\Fragments
/// and %LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments. Later roots override
/// earlier ones, matching Windows Terminal's own merge order.
/// </summary>
internal static class TerminalFragmentDiscovery
{
    public static IReadOnlyDictionary<string, TerminalFragmentProfile> LoadAll(IEnumerable<string>? roots = null)
    {
        var profiles = new Dictionary<string, TerminalFragmentProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots ?? GetDefaultRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory
                .EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    MergeFile(file, profiles);
                }
                catch
                {
                    // Ignore malformed or locked fragment files.
                }
            }
        }

        return profiles;
    }

    private static void MergeFile(string file, Dictionary<string, TerminalFragmentProfile> profiles)
    {
        using var stream = File.OpenRead(file);
        using var doc = JsonDocument.Parse(stream);

        if (!doc.RootElement.TryGetProperty("profiles", out var profilesNode))
        {
            return;
        }

        var listNode = profilesNode.ValueKind == JsonValueKind.Array
            ? profilesNode
            : (profilesNode.TryGetProperty("list", out var list) ? list : default);

        if (listNode.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in listNode.EnumerateArray())
        {
            var guid = element.TryGetProperty("guid", out var guidNode) ? guidNode.GetString() : null;
            if (string.IsNullOrWhiteSpace(guid))
            {
                continue;
            }

            var normalized = guid.Trim('{', '}');
            var commandline = element.TryGetProperty("commandline", out var commandNode)
                ? commandNode.GetString()
                : null;
            var icon = element.TryGetProperty("icon", out var iconNode)
                ? iconNode.GetString()
                : null;

            // Last writer wins, matching Windows Terminal's fragment override order.
            profiles[normalized] = new TerminalFragmentProfile
            {
                Commandline = commandline,
                Icon = icon,
            };
        }
    }

    private static IEnumerable<string> GetDefaultRoots()
    {
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Windows Terminal",
            "Fragments");

        var localAppData = Path.Combine(
            AppDataRoot.Current,
            "Microsoft",
            "Windows Terminal",
            "Fragments");

        // ProgramData is applied first; LocalAppData overrides it.
        if (Directory.Exists(programData))
        {
            yield return programData;
        }

        if (Directory.Exists(localAppData))
        {
            yield return localAppData;
        }
    }
}
