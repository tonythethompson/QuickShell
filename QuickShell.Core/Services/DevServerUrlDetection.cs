using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickShell.Services;

internal static partial class DevServerUrlDetection
{
    public static string? TryDetectDevServerUrl(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var packageJsonPath = Path.Combine(directory, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var script = ReadScript(root, "dev") ?? ReadScript(root, "start");
            if (script is not null)
            {
                var portFromScript = TryExtractPort(script);
                if (portFromScript is not null)
                {
                    return ToLocalhostUrl(portFromScript.Value);
                }
            }

            var port = InferDefaultPort(root, script);
            return port is null ? null : ToLocalhostUrl(port.Value);
        }
        catch
        {
            return null;
        }
    }

    public static string? TryInferTaskType(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var packageJsonPath = Path.Combine(directory, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var script = ReadScript(root, "dev") ?? ReadScript(root, "start");
            return HasFrontendFrameworkSignal(root, script) ? TaskTypeCatalog.Frontend : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasFrontendFrameworkSignal(JsonElement root, string? script)
    {
        if (script is not null)
        {
            if (script.Contains("vite", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (script.Contains("next", StringComparison.OrdinalIgnoreCase)
                || script.Contains("react-scripts", StringComparison.OrdinalIgnoreCase)
                || script.Contains("nuxt", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return HasDependency(root, "vite")
            || HasDependency(root, "next")
            || HasDependency(root, "react-scripts")
            || HasDependency(root, "nuxt");
    }

    public static string? TryDetectDevLaunchCommand(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var packageJsonPath = Path.Combine(directory, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var scriptName = ReadScript(root, "dev") is not null
                ? "dev"
                : ReadScript(root, "start") is not null
                    ? "start"
                    : null;
            if (scriptName is null)
            {
                return null;
            }

            return FormatPackageScriptCommand(directory, scriptName);
        }
        catch
        {
            return null;
        }
    }

    internal static string FormatPackageScriptCommand(string directory, string scriptName)
    {
        return DetectPackageManager(directory) switch
        {
            PackageManagerKind.Pnpm => $"pnpm {scriptName}",
            PackageManagerKind.Yarn => $"yarn {scriptName}",
            PackageManagerKind.Bun => $"bun run {scriptName}",
            PackageManagerKind.Npm when string.Equals(scriptName, "start", StringComparison.Ordinal) => "npm start",
            _ => $"npm run {scriptName}",
        };
    }

    private enum PackageManagerKind
    {
        Npm,
        Pnpm,
        Yarn,
        Bun,
    }

    private static PackageManagerKind DetectPackageManager(string directory)
    {
        if (File.Exists(Path.Combine(directory, "pnpm-lock.yaml")))
        {
            return PackageManagerKind.Pnpm;
        }

        if (File.Exists(Path.Combine(directory, "bun.lockb"))
            || File.Exists(Path.Combine(directory, "bun.lock")))
        {
            return PackageManagerKind.Bun;
        }

        if (File.Exists(Path.Combine(directory, "yarn.lock")))
        {
            return PackageManagerKind.Yarn;
        }

        return PackageManagerKind.Npm;
    }

    private static string? ReadScript(JsonElement root, string scriptName)
    {
        if (!root.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!scripts.TryGetProperty(scriptName, out var script) || script.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = script.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? TryExtractPort(string script)
    {
        var explicitPort = ExplicitPortRegex().Match(script);
        if (explicitPort.Success && int.TryParse(explicitPort.Groups[1].Value, out var port))
        {
            return port;
        }

        var localhostPort = LocalhostPortRegex().Match(script);
        if (localhostPort.Success && int.TryParse(localhostPort.Groups[1].Value, out port))
        {
            return port;
        }

        return null;
    }

    private static int? InferDefaultPort(JsonElement root, string? script)
    {
        if (script is not null)
        {
            if (script.Contains("vite", StringComparison.OrdinalIgnoreCase))
            {
                return 5173;
            }

            if (script.Contains("next", StringComparison.OrdinalIgnoreCase)
                || script.Contains("react-scripts", StringComparison.OrdinalIgnoreCase)
                || script.Contains("nuxt", StringComparison.OrdinalIgnoreCase))
            {
                return 3000;
            }
        }

        if (HasDependency(root, "vite"))
        {
            return 5173;
        }

        if (HasDependency(root, "next") || HasDependency(root, "react-scripts"))
        {
            return 3000;
        }

        return script is null ? null : 3000;
    }

    private static bool HasDependency(JsonElement root, string packageName)
    {
        foreach (var propertyName in new[] { "dependencies", "devDependencies", "peerDependencies" })
        {
            if (!root.TryGetProperty(propertyName, out var section) || section.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (section.TryGetProperty(packageName, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static string ToLocalhostUrl(int port) => $"http://localhost:{port}";

    [GeneratedRegex(@"(?:--port|-p|=)\s*(\d{2,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitPortRegex();

    [GeneratedRegex(@"localhost:(\d{2,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalhostPortRegex();
}
