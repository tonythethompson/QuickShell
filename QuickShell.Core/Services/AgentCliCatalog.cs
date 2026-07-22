namespace QuickShell.Services;

/// <summary>
/// Known AI agent CLIs that can appear as launch-command suggestion pills.
/// Companions stay GUI-only; these are terminal commands.
/// </summary>
internal static class AgentCliCatalog
{
    // Keep below typical Build/API/Test/Frontend scores (~50–68) so agents
    // fill leftover slots instead of crowding out project command pills.
    public const int PathDetectedScore = 42;
    public const int MarkerFallbackScore = 28;

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
        new(
            Id: "amp",
            Title: "Amp",
            Command: "amp",
            PathNames: ["amp"],
            MarkerRelativePaths: ["AGENT.md", ".amp"]),
        new(
            Id: "auggie",
            Title: "Auggie",
            Command: "auggie",
            PathNames: ["auggie"],
            MarkerRelativePaths: [".augment", ".augment-guidelines", ".augment/rules"]),
        new(
            Id: "autohand",
            Title: "AutoHand",
            Command: "autohand",
            PathNames: ["autohand", "autohand-code"],
            MarkerRelativePaths: [".autohand", "AUTOHAND.md"]),
        new(
            Id: "continue",
            Title: "Continue",
            Command: "cn",
            PathNames: ["cn"],
            MarkerRelativePaths: [".continue"]),
        new(
            Id: "crush",
            Title: "Crush",
            Command: "crush",
            PathNames: ["crush"],
            MarkerRelativePaths: ["crush.json", ".crush.json", ".crush", "CRUSH.md", ".crushignore"]),
        new(
            Id: "devin",
            Title: "Devin",
            Command: "devin",
            PathNames: ["devin"],
            MarkerRelativePaths: [".devin", "DEVIN.md", ".windsurf", ".windsurfrules"]),
        new(
            Id: "droid",
            Title: "Droid",
            Command: "droid",
            PathNames: ["droid"],
            MarkerRelativePaths: [".factory", "FACTORY.md"]),
        new(
            Id: "jules",
            Title: "Jules",
            Command: "jules",
            PathNames: ["jules"],
            MarkerRelativePaths: [".jules", "JULES.md"]),
        new(
            Id: "kimi",
            Title: "Kimi Code",
            Command: "kimi",
            PathNames: ["kimi"],
            MarkerRelativePaths: [".kimi", "KIMI.md"]),
        new(
            Id: "plandex",
            Title: "Plandex",
            Command: "plandex",
            PathNames: ["plandex", "pdx"],
            MarkerRelativePaths: [".plandex", ".plandex-dev"]),
        new(
            Id: "roo",
            Title: "Roo Code",
            Command: "roo",
            PathNames: ["roo"],
            MarkerRelativePaths: [".roo", ".roo-code", "ROO.md"]),
        new(
            Id: "vellum",
            Title: "Vellum",
            Command: "vellum",
            PathNames: ["vellum"],
            MarkerRelativePaths: [".vellum", "vellum.lock.json"]),
        new(
            Id: "warp",
            Title: "Warp",
            Command: "oz",
            PathNames: ["oz", "oz-preview", "warp-cli"],
            MarkerRelativePaths: [".warp", "WARP.md"]),
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
