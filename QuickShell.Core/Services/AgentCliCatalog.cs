namespace QuickShell.Services;

/// <summary>
/// Known AI agent CLIs that can appear as launch-command suggestion pills.
/// Companions stay GUI-only; these are terminal commands.
/// </summary>
internal static class AgentCliCatalog
{
    public const int PathDetectedScore = 94;
    public const int MarkerFallbackScore = 72;

    /// <summary>Max agent pills merged into suggestions by default.</summary>
    public const int MaxDefaultAgentPills = 4;

    /// <summary>
    /// Test hook. When set, replaces PATH probing (command name without extension).
    /// </summary>
    internal static Func<string, bool>? IsCommandOnPathOverride { get; set; }

    public static IReadOnlyList<AgentCliDefinition> Definitions { get; } =
    [
        new(
            Id: "claude",
            Title: "Claude Code",
            Command: "claude",
            PathNames: ["claude"],
            MarkerRelativePaths: ["CLAUDE.md", ".claude"]),
        new(
            Id: "codex",
            Title: "Codex",
            Command: "codex",
            PathNames: ["codex"],
            MarkerRelativePaths: ["AGENTS.md", ".codex", "CODEX.md"]),
        new(
            Id: "opencode",
            Title: "OpenCode",
            Command: "opencode",
            PathNames: ["opencode"],
            MarkerRelativePaths: [".opencode", "opencode.json", "opencode.jsonc"]),
        new(
            Id: "gemini",
            Title: "Gemini",
            Command: "gemini",
            PathNames: ["gemini"],
            MarkerRelativePaths: ["GEMINI.md", ".gemini"]),
        new(
            Id: "copilot",
            Title: "GitHub Copilot",
            Command: "copilot",
            PathNames: ["copilot"],
            MarkerRelativePaths:
            [
                ".github/copilot-instructions.md",
                ".github/instructions",
                ".copilot",
            ]),
        new(
            Id: "cursor-agent",
            Title: "Cursor Agent",
            Command: "agent",
            PathNames: ["cursor-agent", "agent"],
            MarkerRelativePaths: [".cursor/cli.json", ".cursor/agent"]),
        new(
            Id: "kiro",
            Title: "Kiro",
            Command: "kiro-cli",
            PathNames: ["kiro-cli", "kiro"],
            MarkerRelativePaths: [".kiro", "KIRO.md"]),
        new(
            Id: "grok",
            Title: "Grok",
            Command: "grok",
            PathNames: ["grok"],
            MarkerRelativePaths: ["GROK.md", ".grok"]),
        new(
            Id: "pi",
            Title: "Pi",
            Command: "pi",
            PathNames: ["pi"],
            MarkerRelativePaths: ["PI.md", ".pi"]),
        new(
            Id: "kilocode",
            Title: "Kilo Code",
            Command: "kilocode",
            PathNames: ["kilocode", "kilo"],
            MarkerRelativePaths: [".kilocode", ".kilo"]),
        new(
            Id: "cmdc",
            Title: "Command Code",
            Command: "cmdc",
            PathNames: ["cmdc"],
            MarkerRelativePaths: [".cmdc", "COMMANDCODE.md"]),
        new(
            Id: "agy",
            Title: "Antigravity",
            Command: "agy",
            PathNames: ["agy"],
            MarkerRelativePaths: [".agy", "ANTIGRAVITY.md", ".antigravity"]),
        new(
            Id: "qwen",
            Title: "Qwen",
            Command: "qwen",
            PathNames: ["qwen"],
            MarkerRelativePaths: ["QWEN.md", ".qwen"]),
        new(
            Id: "hermes",
            Title: "Hermes",
            Command: "hermes",
            PathNames: ["hermes"],
            MarkerRelativePaths: ["HERMES.md", ".hermes"]),
        new(
            Id: "openclaw",
            Title: "OpenClaw",
            Command: "openclaw",
            PathNames: ["openclaw"],
            MarkerRelativePaths: [".openclaw", "OPENCLAW.md"]),
        new(
            Id: "cline",
            Title: "Cline",
            Command: "cline",
            PathNames: ["cline"],
            MarkerRelativePaths: [".cline", "CLINE.md"]),
        new(
            Id: "openhands",
            Title: "OpenHands",
            Command: "openhands",
            PathNames: ["openhands"],
            MarkerRelativePaths: [".openhands", "OPENHANDS.md"]),
        new(
            Id: "goose",
            Title: "Goose",
            Command: "goose",
            PathNames: ["goose"],
            MarkerRelativePaths: ["GOOSE.md", ".goose"]),
        new(
            Id: "aider",
            Title: "Aider",
            Command: "aider",
            PathNames: ["aider"],
            MarkerRelativePaths: [".aider", ".aider.conf.yml", ".aiderignore"]),
    ];

    public static bool IsCommandOnPath(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        if (IsCommandOnPathOverride is not null)
        {
            return IsCommandOnPathOverride(commandName);
        }

        return PathExecutableLookup.Exists(commandName + ".exe")
            || PathExecutableLookup.Exists(commandName + ".cmd")
            || PathExecutableLookup.Exists(commandName);
    }

    public static bool HasProjectMarker(string directory, AgentCliDefinition definition)
    {
        foreach (var relative in definition.MarkerRelativePaths)
        {
            var fullPath = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record AgentCliDefinition(
    string Id,
    string Title,
    string Command,
    IReadOnlyList<string> PathNames,
    IReadOnlyList<string> MarkerRelativePaths);
