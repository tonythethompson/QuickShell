using System.Security.Cryptography;
using System.Text;
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
    /// <summary>
    /// Content-sensitive fingerprint of fragment files. Used to skip a full JSON parse
    /// when nothing on disk has changed.
    /// </summary>
    public static string ComputeFingerprint(IEnumerable<string>? roots = null)
    {
        var builder = new StringBuilder();
        foreach (var root in roots ?? GetDefaultRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory
                    .EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    builder.Append(file)
                        .Append('|')
                        .Append(info.LastWriteTimeUtc.Ticks)
                        .Append('|')
                        .Append(info.Length)
                        .Append('|')
                        .Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
                        .Append(';');
                }
                catch
                {
                    // Locked / deleted between enumerate and stat.
                }
            }
        }

        return builder.ToString();
    }

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

        var fragmentDirectory = Path.GetDirectoryName(file);

        foreach (var element in listNode.EnumerateArray())
        {
            // New profiles use "guid"; patch profiles use "updates" (same GUID target).
            var guid = element.TryGetProperty("guid", out var guidNode)
                ? guidNode.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(guid)
                && element.TryGetProperty("updates", out var updatesNode))
            {
                guid = updatesNode.GetString();
            }

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

            icon = ResolveFragmentIcon(icon, fragmentDirectory);

            // Layer non-null fields onto any earlier entry for this GUID so a later
            // commandline-only patch does not wipe a previously discovered icon.
            if (profiles.TryGetValue(normalized, out var existing))
            {
                profiles[normalized] = new TerminalFragmentProfile
                {
                    Commandline = !string.IsNullOrWhiteSpace(commandline)
                        ? commandline
                        : existing.Commandline,
                    Icon = !string.IsNullOrWhiteSpace(icon)
                        ? icon
                        : existing.Icon,
                };
            }
            else
            {
                profiles[normalized] = new TerminalFragmentProfile
                {
                    Commandline = commandline,
                    Icon = icon,
                };
            }
        }
    }

    /// <summary>
    /// Resolves relative fragment icons against the fragment JSON directory. Absolute paths,
    /// ms-appx URIs, and empty values are left unchanged.
    /// </summary>
    internal static string? ResolveFragmentIcon(string? icon, string? fragmentDirectory)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return icon;
        }

        var trimmed = icon.Trim();
        if (Path.IsPathRooted(trimmed)
            || trimmed.Contains("://", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(fragmentDirectory))
        {
            return trimmed;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(fragmentDirectory, trimmed));
        }
        catch
        {
            return trimmed;
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
