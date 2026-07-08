export const SCHEMA_VERSION = 1;

export const STORAGE_KEY = "quickshell-data";

export type TerminalApplication = "system" | "wt" | "conhost" | "it";

export type LaunchEntry = {
  id: string;
  label: string;
  terminal: string;
  wtProfile?: string | null;
  command?: string | null;
  runAsAdmin: boolean;
  isEnabled: boolean;
  order: number;
  taskType?: string;
};

export type Workspace = {
  id: string;
  name: string;
  abbreviation?: string | null;
  directory: string;
  isPinned: boolean;
  pinOrder?: number | null;
  lastUsedUtc?: string | null;
  terminal: string;
  wtProfile?: string | null;
  command?: string | null;
  runAsAdmin: boolean;
  launches: LaunchEntry[];
  devServerUrl?: string | null;
  openDevServerOnLaunch?: boolean;
  repoUrl?: string | null;
  openCompanionAppOnLaunch?: boolean;
  companionAppPath?: string | null;
  companionAppArguments?: string | null;
};

export type MultiLaunchPresentation = "singleWindowTabs" | "separateWindows";

export type QuickShellSettings = {
  terminalApplication: TerminalApplication;
  defaultProfile: string;
  recentWorkspaceCount: number;
  multiLaunchPresentation: MultiLaunchPresentation;
};

export type StoredData = {
  version: number;
  workspaces: Workspace[];
  settings: QuickShellSettings;
};

export const DEFAULT_SETTINGS: QuickShellSettings = {
  terminalApplication: "wt",
  defaultProfile: "__default__",
  recentWorkspaceCount: 8,
  multiLaunchPresentation: "singleWindowTabs",
};

export const DEFAULT_TERMINAL = "default";

export const TASK_TYPES = ["none", "api", "frontend", "services", "logs", "test", "build"] as const;

export type TaskType = (typeof TASK_TYPES)[number];

export function createEmptyStoredData(): StoredData {
  return {
    version: SCHEMA_VERSION,
    workspaces: [],
    settings: { ...DEFAULT_SETTINGS },
  };
}
