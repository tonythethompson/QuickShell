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
    { id: "default", title: "Use QuickShell default", terminal: "default", wtProfile: null },
  ];

  if (executableExists("wt.exe")) {
    choices.push({ id: "wt", title: "Windows Terminal (default profile)", terminal: "wt", wtProfile: null });
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
    choices.push({ id: "pwsh", title: "PowerShell 7", terminal: "pwsh", wtProfile: null });
  }
  if (executableExists("powershell.exe")) {
    choices.push({ id: "powershell", title: "Windows PowerShell", terminal: "powershell", wtProfile: null });
  }
  if (executableExists("cmd.exe")) {
    choices.push({ id: "cmd", title: "Command Prompt", terminal: "cmd", wtProfile: null });
  }
  if (executableExists("wsl.exe")) {
    choices.push({ id: "wsl", title: "WSL", terminal: "wsl", wtProfile: null });
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

export function resetTerminalCatalogCacheForTests(): void {
  cachedChoices = null;
}

export function discoverDefaultProfileChoices(terminalApplication: string): TerminalChoice[] {
  if (terminalApplication === "wt" || terminalApplication === "it") {
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
  const systemRoot = process.env.SystemRoot ?? process.env.WINDIR ?? "C:\\Windows";
  const candidates = [path.join(systemRoot, "System32", command), path.join(systemRoot, "Sysnative", command)];

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
    const parsed = JSON.parse(readFileSync(settingsPath, "utf8")) as WtSettings;
    return (parsed.profiles?.list ?? [])
      .filter((profile) => profile.name && profile.hidden !== true)
      .map((profile) => profile.name as string);
  } catch {
    return [];
  }
}
