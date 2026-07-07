using System.Text.RegularExpressions;

namespace QuickShell.Services;

internal enum DockerServiceRole
{
    Unknown,
    Services,
    Api,
    Frontend,
    Logs,
}

internal static partial class DockerComposeDiscovery
{
    private static readonly string[] ComposeFileNames =
    [
        "docker-compose.yml",
        "docker-compose.yaml",
        "compose.yml",
        "compose.yaml",
    ];

    private static readonly string[] ServicesKeywords =
    [
        "postgres",
        "postgresql",
        "mysql",
        "mariadb",
        "redis",
        "mongo",
        "mongodb",
        "db",
        "database",
        "memcached",
        "rabbitmq",
        "kafka",
        "elasticsearch",
        "mailhog",
        "minio",
    ];

    private static readonly string[] ApiKeywords =
    [
        "api",
        "backend",
        "server",
        "worker",
        "gateway",
    ];

    private static readonly string[] FrontendKeywords =
    [
        "web",
        "frontend",
        "client",
        "ui",
        "www",
        "app",
    ];

    public static IReadOnlyList<string> DiscoverServiceNames(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        foreach (var fileName in ComposeFileNames)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            if (TryParseServiceNames(path, out var services))
            {
                return services.Take(CommandSuggestionService.MaxDockerServices).ToList();
            }
        }

        return [];
    }

    public static DockerServiceRole ClassifyService(string serviceName)
    {
        var normalized = serviceName.Trim().ToLowerInvariant();
        if (ServicesKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal)))
        {
            return DockerServiceRole.Services;
        }

        if (ApiKeywords.Any(keyword => ContainsToken(normalized, keyword)))
        {
            return DockerServiceRole.Api;
        }

        if (FrontendKeywords.Any(keyword => ContainsToken(normalized, keyword)))
        {
            return DockerServiceRole.Frontend;
        }

        return DockerServiceRole.Unknown;
    }

    public static IEnumerable<WorkspaceSetupTask> BuildServiceSuggestions(string directory)
    {
        foreach (var service in DiscoverServiceNames(directory))
        {
            yield return new WorkspaceSetupTask($"Docker up {service}", $"docker compose up {service}");
            yield return new WorkspaceSetupTask($"Docker logs {service}", $"docker compose logs -f {service}");
        }
    }

    private static bool TryParseServiceNames(string path, out IReadOnlyList<string> services)
    {
        services = [];
        try
        {
            var lines = File.ReadAllLines(path);
            var inServices = false;
            var names = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                if (line.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                if (ServicesHeaderRegex().IsMatch(line))
                {
                    inServices = true;
                    continue;
                }

                if (!inServices)
                {
                    continue;
                }

                if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                {
                    break;
                }

                var match = ServiceNameRegex().Match(line);
                if (match.Success)
                {
                    names.Add(match.Groups["name"].Value);
                }
            }

            services = names;
            return names.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsToken(string value, string token) =>
        value.Equals(token, StringComparison.Ordinal)
        || value.Contains($"{token}-", StringComparison.Ordinal)
        || value.Contains($"-{token}", StringComparison.Ordinal)
        || value.Contains($"_{token}", StringComparison.Ordinal)
        || value.Contains($"{token}_", StringComparison.Ordinal);

    [GeneratedRegex(@"^services:\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ServicesHeaderRegex();

    [GeneratedRegex(@"^\s{2}(?<name>[A-Za-z0-9._-]+):\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNameRegex();
}
