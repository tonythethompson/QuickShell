import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { WORKSPACE_TERMINAL_CHOICES, type TerminalChoice } from "./terminal-options";

export type DiscoveredTerminalChoice = TerminalChoice & {
  terminal: string;
  wtProfile?: string | null;
};

type WtSettings = {
  profiles?: {
    list?: Array<{ name?: string; hidden?: boolean }>;
  };
};

let cachedChoices: DiscoveredTerminalChoice[] | null = null;

export function discoverWorkspaceTerminalChoices(): DiscoveredTerminalChoice[] {
  if (cachedChoices) {
    return cachedChoices;
  }

  if (process.platform !== "win32") {
    cachedChoices = WORKSPACE_TERMINAL_CHOICES.map((choice) => ({
      ...choice,
      terminal: choice.id,
      wtProfile: null,
    }));
    return cachedChoices;
  }

  const choices: DiscoveredTerminalChoice[] = [
    {
      id: "default",
      title: "Use QuickShell default",
      terminal: "default",
      wtProfile: null,
    },
  ];

  if (executableExists("wt.exe")) {
    choices.push({
      id: "wt",
      title: "Windows Terminal (default profile)",
      terminal: "wt",
      wtProfile: null,
    });
    for (const profile of readWindowsTerminalProfiles()) {
      choices.push({
        id: `wt:${profile}`,
        title: `Windows Terminal · ${profile}`,
        terminal: "wt",
        wtProfile: profile,
      });
    }
  }

  if (executableExists("pwsh.exe")) {
    choices.push({
      id: "pwsh",
      title: "PowerShell 7",
      terminal: "pwsh",
      wtProfile: null,
    });
  }
  if (executableExists("powershell.exe")) {
    choices.push({
      id: "powershell",
      title: "Windows PowerShell",
      terminal: "powershell",
      wtProfile: null,
    });
  }
  if (executableExists("cmd.exe")) {
    choices.push({
      id: "cmd",
      title: "Command Prompt",
      terminal: "cmd",
      wtProfile: null,
    });
  }
  if (executableExists("wsl.exe")) {
    choices.push({ id: "wsl", title: "WSL (default distro)", terminal: "wsl", wtProfile: null });
    for (const distro of listWslDistros()) {
      choices.push({
        id: `wsl:${distro}`,
        title: `WSL · ${distro}`,
        terminal: "wsl",
        wtProfile: distro,
      });
    }
  }

  cachedChoices =
    choices.length > 1
      ? choices
      : WORKSPACE_TERMINAL_CHOICES.map((choice) => ({
          ...choice,
          terminal: choice.id,
          wtProfile: null,
        }));
  return cachedChoices;
}

export function invalidateTerminalCatalogCache(): void {
  cachedChoices = null;
}

/** @deprecated Prefer invalidateTerminalCatalogCache in production code. */
export function resetTerminalCatalogCacheForTests(): void {
  invalidateTerminalCatalogCache();
}

export function discoverDefaultProfileChoices(terminalApplication: string): TerminalChoice[] {
  if (terminalApplication === "wt" || terminalApplication === "it" || terminalApplication === "system") {
    const profiles = readWindowsTerminalProfiles();
    return [
      { id: "__default__", title: "Default profile for this app" },
      ...profiles.map((profile) => ({ id: profile, title: profile })),
    ];
  }

  const choices: TerminalChoice[] = [{ id: "__default__", title: "Default profile for this app" }];
  if (executableExists("powershell.exe")) {
    choices.push({ id: "powershell", title: "PowerShell" });
  }
  if (executableExists("pwsh.exe")) {
    choices.push({ id: "pwsh", title: "PowerShell 7" });
  }
  if (executableExists("cmd.exe")) {
    choices.push({ id: "cmd", title: "Command Prompt" });
  }
  return choices.length > 1
    ? choices
    : [
        { id: "__default__", title: "Default profile for this app" },
        { id: "powershell", title: "PowerShell" },
        { id: "pwsh", title: "PowerShell 7" },
        { id: "cmd", title: "Command Prompt" },
      ];
}

export function choiceForTerminalState(
  terminal: string,
  wtProfile?: string | null,
  choices: DiscoveredTerminalChoice[] = discoverWorkspaceTerminalChoices(),
): string {
  if (wtProfile) {
    const profileMatch = choices.find((choice) => choice.terminal === terminal && choice.wtProfile === wtProfile);
    if (profileMatch) {
      return profileMatch.id;
    }
  }
  const match = choices.find((choice) => choice.terminal === terminal && !choice.wtProfile);
  return match?.id ?? "default";
}

function executableExists(command: string): boolean {
  const candidates: string[] = [];
  const pathEnv = process.env.PATH ?? process.env.Path ?? "";
  for (const entry of pathEnv.split(path.delimiter)) {
    if (entry) {
      candidates.push(path.join(entry, command));
    }
  }

  const systemRoot = process.env.SystemRoot ?? process.env.WINDIR ?? "C:\\Windows";
  candidates.push(path.join(systemRoot, "System32", command), path.join(systemRoot, "Sysnative", command));

  if (command === "wt.exe") {
    const localAppData = process.env.LOCALAPPDATA;
    if (localAppData) {
      candidates.push(
        path.join(localAppData, "Microsoft", "WindowsApps", "wt.exe"),
        path.join(localAppData, "Microsoft", "WindowsApps", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "wt.exe"),
      );
    }
  }

  return candidates.some((candidate) => existsSync(candidate));
}

/** True when the host executable is resolvable on PATH / known install locations. */
export function terminalHostExecutableExists(hostExecutable: string): boolean {
  const trimmed = hostExecutable.trim();
  if (!trimmed) {
    return false;
  }
  if (existsSync(trimmed)) {
    return true;
  }
  return executableExists(path.basename(trimmed));
}

export function parseWslDistroListOutput(buffer: Buffer): string[] {
  const text = buffer.toString("utf16le").replace(/^\uFEFF/, "");
  return text
    .split(/\r?\n/)
    .map((line) => line.replace(/\0/g, "").trim())
    .filter(Boolean);
}

function listWslDistros(): string[] {
  try {
    const output = execFileSync("wsl.exe", ["-l", "-q"], {
      windowsHide: true,
      encoding: "buffer",
      timeout: 3000,
    });
    return parseWslDistroListOutput(output);
  } catch {
    return [];
  }
}

function readWindowsTerminalProfiles(): string[] {
  const settingsPaths = [
    process.env.LOCALAPPDATA
      ? path.join(
          process.env.LOCALAPPDATA,
          "Packages",
          "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
          "LocalState",
          "settings.json",
        )
      : null,
    process.env.LOCALAPPDATA
      ? path.join(process.env.LOCALAPPDATA, "Microsoft", "Windows Terminal", "settings.json")
      : null,
  ].filter((value): value is string => Boolean(value));

  for (const settingsPath of settingsPaths) {
    const profiles = parseWtProfiles(settingsPath);
    if (profiles.length > 0) {
      return profiles;
    }
  }

  return ["PowerShell", "Command Prompt"];
}

function parseWtProfiles(settingsPath: string): string[] {
  if (!existsSync(settingsPath)) {
    return [];
  }

  try {
    const parsed = parseJsonc(readFileSync(settingsPath, "utf8")) as WtSettings;
    return (parsed.profiles?.list ?? [])
      .filter((profile) => profile.name && profile.hidden !== true)
      .map((profile) => profile.name as string);
  } catch {
    return [];
  }
}

function parseJsonc(raw: string): unknown {
  const withoutComments = stripJsoncComments(raw);
  const withoutTrailingCommas = withoutComments.replace(/,\s*([}\]])/g, "$1");
  return JSON.parse(withoutTrailingCommas);
}

function stripJsoncComments(raw: string): string {
  let result = "";
  let inString = false;
  let escaped = false;

  for (let index = 0; index < raw.length; index += 1) {
    const char = raw[index];
    const next = raw[index + 1];

    if (inString) {
      result += char;
      if (escaped) {
        escaped = false;
        continue;
      }
      if (char === "\\") {
        escaped = true;
        continue;
      }
      if (char === '"') {
        inString = false;
      }
      continue;
    }

    if (char === '"') {
      inString = true;
      result += char;
      continue;
    }

    if (char === "/" && next === "/") {
      while (index < raw.length && raw[index] !== "\n") {
        index += 1;
      }
      continue;
    }

    if (char === "/" && next === "*") {
      index += 2;
      while (index < raw.length - 1 && !(raw[index] === "*" && raw[index + 1] === "/")) {
        index += 1;
      }
      index += 1;
      continue;
    }

    result += char;
  }

  return result;
}

export function parseJsoncForTests(raw: string): unknown {
  return parseJsonc(raw);
}
