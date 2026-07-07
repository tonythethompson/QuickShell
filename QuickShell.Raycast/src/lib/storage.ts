import type { LaunchEntry, QuickShellSettings, StoredData, Workspace } from "./schema";
import { STORAGE_KEY, createEmptyStoredData } from "./schema";
import { createStableId, ensureStableId } from "./ids";
import { importParsedPayload, type ImportResult } from "./import-export";
import { migrateStoredData } from "./migration";
import { normalizeWorkspace, validateWorkspace, validateWorkspaceCount } from "./validation";

export type StorageAdapter = {
  getItem: (key: string) => Promise<string | undefined>;
  setItem: (key: string, value: string) => Promise<void>;
};

const MAX_HISTORY_ENTRIES = 25;
const RECENT_WRITE_DEBOUNCE_MS = 500;

export class QuickShellStorage {
  private cache: StoredData | null = null;
  private undoHistory: StoredData[] = [];
  private redoHistory: StoredData[] = [];
  private recentWriteTimer: ReturnType<typeof setTimeout> | null = null;
  private recentWriteDirty = false;

  constructor(
    private readonly adapter: StorageAdapter,
    private readonly settingsProvider?: () => QuickShellSettings,
  ) {}

  async load(): Promise<StoredData> {
    await this.ensureLoaded();
    return this.cloneData(this.cache!);
  }

  canUndo(): boolean {
    return this.undoHistory.length > 0;
  }

  canRedo(): boolean {
    return this.redoHistory.length > 0;
  }

  async undo(): Promise<boolean> {
    await this.flushRecentWrites();
    if (this.undoHistory.length === 0) {
      return false;
    }

    if (this.cache) {
      this.redoHistory.push(this.cloneData(this.cache));
    }

    const previous = this.undoHistory.pop();
    if (!previous) {
      return false;
    }

    this.cache = this.cloneData(previous);
    await this.persistCache({ recordHistory: false });
    return true;
  }

  async redo(): Promise<boolean> {
    await this.flushRecentWrites();
    if (this.redoHistory.length === 0) {
      return false;
    }

    if (this.cache) {
      this.undoHistory.push(this.cloneData(this.cache));
    }

    const next = this.redoHistory.pop();
    if (!next) {
      return false;
    }

    this.cache = this.cloneData(next);
    await this.persistCache({ recordHistory: false });
    return true;
  }

  async exportJson(): Promise<string> {
    const data = await this.load();
    const settings = await this.getSettings();
    return JSON.stringify({ ...data, settings }, null, 2);
  }

  async importJson(raw: string, mode: "merge" | "replace" = "merge"): Promise<ImportResult> {
    await this.flushRecentWrites();
    const existing = mode === "merge" ? await this.load() : createEmptyStoredData();
    const result = importParsedPayload(JSON.parse(raw) as unknown, existing);
    await this.save(result.data);
    return result;
  }

  async save(data: StoredData, options?: { recordHistory?: boolean }): Promise<void> {
    const recordHistory = options?.recordHistory ?? true;
    if (recordHistory && this.cache) {
      this.pushUndoSnapshot(this.cache);
    }

    const normalized: StoredData = {
      version: data.version,
      settings: { ...data.settings },
      workspaces: data.workspaces.map((workspace) => normalizeWorkspace({ ...workspace })),
    };

    const countResult = validateWorkspaceCount(normalized.workspaces.length);
    if (!countResult.ok) {
      throw new Error(countResult.message);
    }

    for (const workspace of normalized.workspaces) {
      const validation = validateWorkspace(workspace);
      if (!validation.ok) {
        throw new Error(`${workspace.name || workspace.id}: ${validation.message}`);
      }
    }

    this.cache = normalized;
    await this.persistCache({ recordHistory: false });
  }

  async getWorkspaces(): Promise<Workspace[]> {
    await this.ensureLoaded();
    return this.cache!.workspaces.map((workspace) => ({ ...workspace, launches: [...workspace.launches] }));
  }

  async getSettings(): Promise<QuickShellSettings> {
    if (this.settingsProvider) {
      return { ...this.settingsProvider() };
    }

    await this.ensureLoaded();
    return { ...this.cache!.settings };
  }

  async upsertWorkspace(workspace: Workspace): Promise<Workspace> {
    await this.flushRecentWrites();
    const data = await this.load();
    const normalized = normalizeWorkspace({
      ...workspace,
      id: ensureStableId(workspace.id),
      launches: workspace.launches.map((launch) => ({
        ...launch,
        id: ensureStableId(launch.id),
      })),
    });

    const validation = validateWorkspace(normalized);
    if (!validation.ok) {
      throw new Error(validation.message);
    }

    const index = data.workspaces.findIndex((item) => item.id === normalized.id);
    if (index >= 0) {
      data.workspaces[index] = normalized;
    } else {
      const countResult = validateWorkspaceCount(data.workspaces.length + 1);
      if (!countResult.ok) {
        throw new Error(countResult.message);
      }
      data.workspaces.push(normalized);
    }

    await this.save(data);
    return normalized;
  }

  async deleteWorkspace(workspaceId: string): Promise<void> {
    await this.flushRecentWrites();
    const data = await this.load();
    data.workspaces = data.workspaces.filter((workspace) => workspace.id !== workspaceId);
    await this.save(data);
  }

  async duplicateWorkspace(workspaceId: string): Promise<Workspace> {
    await this.flushRecentWrites();
    const data = await this.load();
    const source = data.workspaces.find((workspace) => workspace.id === workspaceId);
    if (!source) {
      throw new Error("Workspace not found.");
    }

    const duplicate: Workspace = normalizeWorkspace({
      ...source,
      id: createStableId(),
      name: `${source.name} Copy`,
      abbreviation: source.abbreviation ? `${source.abbreviation}-copy` : null,
      isPinned: false,
      pinOrder: null,
      lastUsedUtc: null,
      launches: source.launches.map((launch) => ({
        ...launch,
        id: createStableId(),
      })),
    });

    return this.upsertWorkspace(duplicate);
  }

  async setFavorite(workspaceId: string, isPinned: boolean): Promise<Workspace> {
    await this.flushRecentWrites();
    const data = await this.load();
    const workspace = data.workspaces.find((item) => item.id === workspaceId);
    if (!workspace) {
      throw new Error("Workspace not found.");
    }

    workspace.isPinned = isPinned;
    if (isPinned) {
      const maxPinOrder = data.workspaces
        .filter((item) => item.isPinned && item.id !== workspaceId)
        .reduce((max, item) => Math.max(max, item.pinOrder ?? 0), 0);
      workspace.pinOrder = maxPinOrder + 1;
    } else {
      workspace.pinOrder = null;
    }

    await this.save(data);
    return { ...workspace };
  }

  async markWorkspaceUsed(workspaceId: string, usedAt = new Date()): Promise<void> {
    await this.ensureLoaded();
    const workspace = this.cache!.workspaces.find((item) => item.id === workspaceId);
    if (!workspace) {
      throw new Error("Workspace not found.");
    }
    workspace.lastUsedUtc = usedAt.toISOString();
    this.recentWriteDirty = true;
    this.scheduleRecentWriteFlush();
  }

  async flushRecentWrites(): Promise<void> {
    if (this.recentWriteTimer) {
      clearTimeout(this.recentWriteTimer);
      this.recentWriteTimer = null;
    }
    if (!this.recentWriteDirty || !this.cache) {
      return;
    }
    this.recentWriteDirty = false;
    await this.persistCache({ recordHistory: false });
  }

  async updateSettings(settings: QuickShellSettings): Promise<void> {
    if (this.settingsProvider) {
      throw new Error("Settings are managed in Raycast extension preferences.");
    }

    await this.flushRecentWrites();
    const data = await this.load();
    data.settings = { ...settings };
    await this.save(data);
  }

  private async ensureLoaded(): Promise<void> {
    if (this.cache) {
      return;
    }

    const raw = await this.adapter.getItem(STORAGE_KEY);
    if (!raw) {
      this.cache = createEmptyStoredData();
      return;
    }

    try {
      const parsed = JSON.parse(raw) as unknown;
      this.cache = migrateStoredData(parsed);
    } catch {
      this.cache = createEmptyStoredData();
    }
  }

  private async persistCache(options?: { recordHistory?: boolean }): Promise<void> {
    if (!this.cache) {
      return;
    }
    await this.adapter.setItem(STORAGE_KEY, JSON.stringify(this.cache));
    if (options?.recordHistory) {
      // no-op: history is recorded in save()
    }
  }

  private pushUndoSnapshot(data: StoredData): void {
    this.undoHistory.push(this.cloneData(data));
    if (this.undoHistory.length > MAX_HISTORY_ENTRIES) {
      this.undoHistory.shift();
    }
    this.redoHistory = [];
  }

  private scheduleRecentWriteFlush(): void {
    if (this.recentWriteTimer) {
      clearTimeout(this.recentWriteTimer);
    }
    this.recentWriteTimer = setTimeout(() => {
      void this.flushRecentWrites();
    }, RECENT_WRITE_DEBOUNCE_MS);
  }

  private cloneData(data: StoredData): StoredData {
    return {
      version: data.version,
      settings: { ...data.settings },
      workspaces: data.workspaces.map((workspace) => ({
        ...workspace,
        launches: workspace.launches.map((launch) => ({ ...launch })),
      })),
    };
  }
}

export function createMemoryStorageAdapter(initial?: StoredData): StorageAdapter {
  const memory = new Map<string, string>();
  if (initial) {
    memory.set(STORAGE_KEY, JSON.stringify(initial));
  }
  return {
    async getItem(key: string) {
      return memory.get(key);
    },
    async setItem(key: string, value: string) {
      memory.set(key, value);
    },
  };
}

export function workspaceSubtitle(workspace: Workspace, launch?: LaunchEntry): string {
  if (launch) {
    const command = launch.command?.trim();
    return command
      ? `${workspace.directory} • ${launch.label}: ${command}`
      : `${workspace.directory} • ${launch.label}`;
  }

  const enabledLaunches = workspace.launches.filter((entry) => entry.isEnabled);
  if (enabledLaunches.length === 1) {
    const command = enabledLaunches[0].command?.trim();
    return command ? `${workspace.directory} • ${command}` : workspace.directory;
  }

  if (enabledLaunches.length > 1) {
    return `${workspace.directory} • ${enabledLaunches.length} launches`;
  }

  return workspace.directory;
}
