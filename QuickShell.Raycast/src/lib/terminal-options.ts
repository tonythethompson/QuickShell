import type { QuickShellSettings, TerminalApplication } from "./schema";
import { discoverDefaultProfileChoices } from "./terminal-catalog";

export type TerminalChoice = { id: string; title: string };

export const WORKSPACE_TERMINAL_CHOICES: TerminalChoice[] = [
  { id: "default", title: "Use QuickShell default" },
  { id: "wt", title: "Windows Terminal" },
  { id: "powershell", title: "Windows PowerShell" },
  { id: "pwsh", title: "PowerShell 7" },
  { id: "cmd", title: "Command Prompt" },
  { id: "wsl", title: "WSL" },
];

export const TERMINAL_APPLICATION_CHOICES: TerminalChoice[] = [
  { id: "system", title: "Let Windows choose" },
  { id: "wt", title: "Windows Terminal" },
  { id: "conhost", title: "Windows Console Host" },
  { id: "it", title: "Intelligent Terminal" },
];

const CONHOST_PROFILE_CHOICES: TerminalChoice[] = [
  { id: "__default__", title: "Default profile for this app" },
  { id: "powershell", title: "PowerShell" },
  { id: "pwsh", title: "PowerShell 7" },
  { id: "cmd", title: "Command Prompt" },
];

export function getDefaultProfileChoices(terminalApplication: TerminalApplication): TerminalChoice[] {
  if (terminalApplication === "conhost") {
    return CONHOST_PROFILE_CHOICES;
  }
  return discoverDefaultProfileChoices(terminalApplication);
}

export function getWorkspaceProfileChoices(terminal: string): TerminalChoice[] {
  if (terminal === "wt" || terminal === "wsl") {
    return discoverDefaultProfileChoices("wt").filter((choice) => choice.id !== "__default__" || terminal === "wt");
  }
  return [];
}

export function normalizeDefaultProfile(terminalApplication: TerminalApplication, profile: string): string {
  const choices = getDefaultProfileChoices(terminalApplication);
  if (choices.some((choice) => choice.id === profile)) {
    return profile;
  }
  return "__default__";
}

export function settingsSummary(settings: QuickShellSettings): string {
  const app =
    TERMINAL_APPLICATION_CHOICES.find((choice) => choice.id === settings.terminalApplication)?.title ??
    settings.terminalApplication;
  const profile = settings.defaultProfile === "__default__" ? "default profile" : settings.defaultProfile;
  const multiLaunch =
    settings.multiLaunchPresentation === "separateWindows" ? "separate windows" : "tabs";
  return `${app} • ${profile} • ${multiLaunch}`;
}
