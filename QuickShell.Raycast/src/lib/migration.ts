import {
  DEFAULT_SETTINGS,
  SCHEMA_VERSION,
  type LaunchEntry,
  type QuickShellSettings,
  type StoredData,
  type Workspace,
  createEmptyStoredData,
} from "./schema";
import { ensureStableId } from "./ids";
import { normalizeLaunches, normalizeWorkspace } from "./validation";

type UnknownRecord = Record<string, unknown>;

export function migrateStoredData(raw: unknown): StoredData {
  if (!raw || typeof raw !== "object") {
    return createEmptyStoredData();
  }

  const record = raw as UnknownRecord;
  const version = typeof record.version === "number" ? record.version : 0;

  if (version > SCHEMA_VERSION) {
    throw new Error(`Unsupported QuickShell data version: ${version}`);
  }

  const workspaces = Array.isArray(record.workspaces)
    ? record.workspaces
        .map((item) => migrateWorkspace(item))
        .filter((workspace): workspace is Workspace => workspace !== null)
    : [];

  const settings = migrateSettings(record.settings);

  const data: StoredData = {
    version: SCHEMA_VERSION,
    workspaces,
    settings,
  };

  return data;
}

function migrateSettings(raw: unknown): QuickShellSettings {
  if (!raw || typeof raw !== "object") {
    return { ...DEFAULT_SETTINGS };
  }

  const record = raw as UnknownRecord;
  const terminalApplication = parseTerminalApplication(record.terminalApplication);
  const defaultProfile =
    typeof record.defaultProfile === "string" && record.defaultProfile.trim()
      ? record.defaultProfile.trim()
      : DEFAULT_SETTINGS.defaultProfile;

  let recentWorkspaceCount = DEFAULT_SETTINGS.recentWorkspaceCount;
  if (typeof record.recentWorkspaceCount === "number") {
    recentWorkspaceCount = normalizeRecentCount(record.recentWorkspaceCount);
  } else if (typeof record.recentWorkspaceCount === "string") {
    const parsed = Number.parseInt(record.recentWorkspaceCount, 10);
    if (!Number.isNaN(parsed)) {
      recentWorkspaceCount = normalizeRecentCount(parsed);
    }
  }

  return {
    terminalApplication,
    defaultProfile,
    recentWorkspaceCount,
  };
}

function migrateWorkspace(raw: unknown): Workspace | null {
  if (!raw || typeof raw !== "object") {
    return null;
  }

  const record = raw as UnknownRecord;
  const name = typeof record.name === "string" ? record.name.trim() : "";
  const directory = typeof record.directory === "string" ? record.directory.trim() : "";
  if (!name || !directory) {
    return null;
  }

  const launches = Array.isArray(record.launches)
    ? record.launches
        .map((entry) => migrateLaunchEntry(entry))
        .filter((entry): entry is LaunchEntry => entry !== null)
    : Array.isArray(record.entries)
      ? record.entries
          .map((entry) => migrateLaunchEntry(entry))
          .filter((entry): entry is LaunchEntry => entry !== null)
      : [];

  const workspace: Workspace = {
    id: ensureStableId(typeof record.id === "string" ? record.id : undefined),
    name,
    abbreviation: typeof record.abbreviation === "string" ? record.abbreviation : null,
    directory,
    isPinned: Boolean(record.isPinned),
    pinOrder: typeof record.pinOrder === "number" ? record.pinOrder : null,
    lastUsedUtc: typeof record.lastUsedUtc === "string" ? record.lastUsedUtc : null,
    terminal: typeof record.terminal === "string" ? record.terminal : "default",
    wtProfile: typeof record.wtProfile === "string" ? record.wtProfile : null,
    command: typeof record.command === "string" ? record.command : null,
    runAsAdmin: Boolean(record.runAsAdmin),
    launches,
  };

  return normalizeWorkspace({
    ...workspace,
    launches: normalizeLaunches(workspace.launches, workspace),
  });
}

function migrateLaunchEntry(raw: unknown): LaunchEntry | null {
  if (!raw || typeof raw !== "object") {
    return null;
  }

  const record = raw as UnknownRecord;
  const label = typeof record.label === "string" ? record.label.trim() : "";
  if (!label) {
    return null;
  }

  return {
    id: ensureStableId(typeof record.id === "string" ? record.id : undefined),
    label,
    terminal: typeof record.terminal === "string" ? record.terminal : "default",
    wtProfile: typeof record.wtProfile === "string" ? record.wtProfile : null,
    command: typeof record.command === "string" ? record.command : null,
    runAsAdmin: Boolean(record.runAsAdmin),
    isEnabled: record.isEnabled !== false,
    order: typeof record.order === "number" ? record.order : 0,
    taskType: typeof record.taskType === "string" ? record.taskType : "none",
  };
}

function parseTerminalApplication(value: unknown): QuickShellSettings["terminalApplication"] {
  if (value === "system" || value === "wt" || value === "conhost" || value === "it") {
    return value;
  }
  return DEFAULT_SETTINGS.terminalApplication;
}

export function normalizeRecentCount(value: number): number {
  if (value <= 0) {
    return 0;
  }
  return 8;
}

export function isRecentSectionEnabled(count: number): boolean {
  return normalizeRecentCount(count) > 0;
}

export function clampRecentDisplayCount(count: number): number {
  const normalized = normalizeRecentCount(count);
  return normalized <= 0 ? 0 : Math.min(normalized, 8);
}
