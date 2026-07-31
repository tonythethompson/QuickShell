using System.Diagnostics;

namespace QuickShell.Services;

internal static class WslPathResolver
{
    internal sealed class WslLocation
    {
        public required string LinuxPath { get; init; }

        public string? Distro { get; init; }

        public string? UncPath { get; init; }
    }

    public static bool TryParse(string? path, out WslLocation location)
    {
        location = null!;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim();

        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            location = new WslLocation
            {
                LinuxPath = trimmed,
            };
            return true;
        }

        if (!trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseUncRemainder(trimmed[@"\\wsl.localhost\".Length..], trimmed, out location);
        }

        if (trimmed.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseUncRemainder(trimmed[@"\\wsl$\".Length..], trimmed, out location);
        }

        return false;
    }

    public static WslLocation CreateLocationFromWindowsDirectory(string directory, LaunchTarget target)
    {
        if (TryParse(directory, out var parsed))
        {
            return parsed;
        }

        return new WslLocation
        {
            LinuxPath = ConvertWindowsPathToLinuxPath(directory),
        };
    }

    internal static string ConvertWindowsPathToLinuxPath(string directory)
    {
        var trimmed = directory.Trim();
        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return trimmed.TrimEnd('/');
        }

        var normalized = trimmed.Replace('/', '\\');
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            var drive = char.ToLowerInvariant(normalized[0]);
            var remainder = normalized[2..].TrimStart('\\').Replace('\\', '/').TrimEnd('/');
            return string.IsNullOrEmpty(remainder)
                ? $"/mnt/{drive}"
                : $"/mnt/{drive}/{remainder}";
        }

        return trimmed;
    }

    public static bool DirectoryExists(WslLocation location)
    {
        if (!string.IsNullOrWhiteSpace(location.UncPath) && Directory.Exists(location.UncPath))
        {
            return true;
        }

        var distro = location.Distro ?? "Ubuntu";
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = $"-d \"{distro}\" -e test -d \"{EscapeShell(location.LinuxPath)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveDistro(WslLocation location, LaunchTarget target) =>
        location.Distro
        ?? ExtractDistroFromCommandLine(target.WtCommandLine)
        ?? target.ProfileOrDistro
        ?? "Ubuntu";

    private static bool TryParseUncRemainder(string remainder, string fullUnc, out WslLocation location)
    {
        location = null!;
        // Bolt: Performance optimization - avoid string.Split(), Linq Skip(), and string.Join() allocations.
        var span = remainder.AsSpan();
        string? distro = null;
        var linuxPathBuilder = new System.Text.StringBuilder();

        foreach (var range in span.Split('\\'))
        {
            var part = span[range];
            if (part.IsEmpty)
            {
                continue;
            }

            if (distro is null)
            {
                distro = part.ToString();
            }
            else
            {
                linuxPathBuilder.Append('/');
                linuxPathBuilder.Append(part);
            }
        }

        if (linuxPathBuilder.Length == 0 || distro is null)
        {
            return false;
        }

        location = new WslLocation
        {
            Distro = distro,
            LinuxPath = linuxPathBuilder.ToString(),
            UncPath = fullUnc,
        };

        return true;
    }

    private static string? ExtractDistroFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        return ExtractFlagValue(commandLine, "-d ")
            ?? ExtractFlagValue(commandLine, "--distribution ")
            ?? ExtractFlagValue(commandLine, "--distribution=");
    }

    private static string? ExtractFlagValue(string commandLine, string marker)
    {
        var index = commandLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var remainder = commandLine[(index + marker.Length)..].Trim();
        if (remainder.Length == 0)
        {
            return null;
        }

        if (remainder.StartsWith('"'))
        {
            var endQuote = remainder.IndexOf('"', 1);
            return endQuote > 0 ? remainder[1..endQuote] : remainder.Trim('"');
        }

        if (remainder.StartsWith('\''))
        {
            var endQuote = remainder.IndexOf('\'', 1);
            return endQuote > 0 ? remainder[1..endQuote] : remainder.Trim('\'');
        }

        var end = remainder.IndexOf(' ');
        return (end < 0 ? remainder : remainder[..end]).Trim();
    }

    private static string EscapeShell(string value) => value.Replace("\"", "\\\"");
}
