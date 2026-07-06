import type { LaunchEntry, QuickShellSettings, StoredData, Workspace } from "./schema";
import { STORAGE_KEY, createEmptyStoredData } from "./schema";
import { createStableId, ensureStableId } from "./ids";
import { migrateStoredData } from "./migration";
import { normalizeWorkspace, validateWorkspace, validateWorkspaceCount } from "./validation";

export type StorageAdapter = {
  getItem: (key: string) => Promise<string | undefined>;
  setItem: (key: string, value: string) => Promise<void>;
};

export class QuickShellStorage {
  private cache: StoredData | null = null;

  constructor(private readonly adapter: StorageAdapter) {}

  async load(): Promise<StoredData> {
    if (this.cache) {
      return this.cloneData(this.cache);
    }

    const raw = await this.adapter.getItem(STORAGE_KEY);
    if (!raw) {
      this.cache = createEmptyStoredData();
      return this.cloneData(this.cache);
    }

    try {
      const parsed = JSON.parse(raw) as unknown;
      this.cache = migrateStoredData(parsed);
      return this.cloneData(this.cache);
    } catch {
      this.cache = createEmptyStoredData();
      return this.cloneData(this.cache);
    }
  }

  async save(data: StoredData): Promise<void> {
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
    await this.adapter.setItem(STORAGE_KEY, JSON.stringify(normalized));
  }

  async getWorkspaces(): Promise<Workspace[]> {
    const data = await this.load();
    return data.workspaces.map((workspace) => ({ ...workspace, launches: [...workspace.launches] }));
  }

  async getSettings(): Promise<QuickShellSettings> {
    const data = await this.load();
    return { ...data.settings };
  }

  async upsertWorkspace(workspace: Workspace): Promise<Workspace> {
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
    const data = await this.load();
    data.workspaces = data.workspaces.filter((workspace) => workspace.id !== workspaceId);
    await this.save(data);
  }

  async duplicateWorkspace(workspaceId: string): Promise<Workspace> {
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
    const data = await this.load();
    const workspace = data.workspaces.find((item) => item.id === workspaceId);
    if (!workspace) {
      throw new Error("Workspace not found.");
    }
    workspace.lastUsedUtc = usedAt.toISOString();
    await this.save(data);
  }

  async updateSettings(settings: QuickShellSettings): Promise<void> {
    const data = await this.load();
    data.settings = { ...settings };
    await this.save(data);
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
    return command ? `${workspace.directory} • ${launch.label}: ${command}` : `${workspace.directory} • ${launch.label}`;
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
