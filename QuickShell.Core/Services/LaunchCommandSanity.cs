namespace QuickShell.Services;

/// <summary>
/// Filters launch-command suggestions that look valid on disk but will not run from a
/// workspace terminal (VS Code variables, temp probe projects, etc.).
/// </summary>
internal static class LaunchCommandSanity
{
    /// <summary>
    /// Returns false when the command should not be offered as a suggestion pill.
    /// </summary>
    public static bool IsUsableSuggestion(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var text = command.Trim();

        // VS Code / Cursor task placeholders are not expanded by Quick Shell launches.
        if (text.Contains("${", StringComparison.Ordinal)
            || text.Contains("$(", StringComparison.Ordinal)
            || text.Contains("%workspaceFolder%", StringComparison.OrdinalIgnoreCase)
            || text.Contains("$workspaceFolder", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Ephemeral probe / scratch projects that pollute .NET monorepos.
        if (LooksLikeTempProjectReference(text))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Prefer real projects over temp probes when listing runnable .csproj files.
    /// </summary>
    public static bool IsUsableDotNetProjectFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(fileName.Trim());
        if (name.Length == 0)
        {
            return false;
        }

        if (name.StartsWith("tmp_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("temp_", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_probe", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".probe", StringComparison.OrdinalIgnoreCase)
            || name.Contains("serilog_probe", StringComparison.OrdinalIgnoreCase)
            || name.Contains("_tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeTempProjectReference(string command)
    {
        // e.g. dotnet watch --project tmp_serilog_probe.csproj
        // Bolt: Performance optimization - use command.AsSpan().Split(' ') to avoid string array allocations
        var commandSpan = command.AsSpan();
        foreach (var range in commandSpan.Split(' '))
        {
            var tokenSpan = commandSpan[range].Trim();
            if (tokenSpan.IsEmpty)
            {
                continue;
            }

            var fileSpan = tokenSpan.Trim(['"', '\'']);

            // Check extensions using spans to avoid allocations
            if (!fileSpan.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                && !fileSpan.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                && !fileSpan.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                && !fileSpan.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Fall back to string allocation only when necessary to call IsUsableDotNetProjectFileName
            var file = fileSpan.ToString();
            if (!IsUsableDotNetProjectFileName(file))
            {
                return true;
            }
        }

        return false;
    }
}
