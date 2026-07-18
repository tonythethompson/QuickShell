import type {
  LaunchEntry,
  QuickShellSettings,
  StoredData,
  StoredWorkspace,
  Workspace,
  WorkspaceSecurityMetadata,
} from "./schema";
import { STORAGE_KEY, createEmptyStoredData } from "./schema";
import { createStableId, ensureStableId } from "./ids";
import { importParsedPayload, type ImportResult } from "./import-export";
import { migrateStoredData } from "./migration";
import { normalizeWorkspace, validateWorkspace, validateWorkspaceCount } from "./validation";
import { matchesReviewToken, type WorkspaceReviewToken } from "./security";

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

    this.cache = this.preserveCurrentTrust(this.cloneData(previous), this.cache);
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

    this.cache = this.preserveCurrentTrust(this.cloneData(next), this.cache);
    await this.persistCache({ recordHistory: false });
    return true;
  }

  async exportJson(): Promise<string> {
    const data = await this.load();
    const settings = await this.getSettings();
    const { workspaceSecurity: _workspaceSecurity, ...portable } = data;
    return JSON.stringify({ ...portable, settings }, null, 2);
  }

  async importJson(raw: string, mode: "merge" | "replace" = "merge"): Promise<ImportResult> {
    await this.flushRecentWrites();
    const existing = mode === "merge" ? await this.load() : createEmptyStoredData();
    const result = importParsedPayload(JSON.parse(raw) as unknown, existing);
    await this.save(result.data);
    return result;
  }

  async save(data: StoredData, options?: {
    recordHistory?: boolean;
    preserveSecurity?: boolean;
    allowSubmittedSecurity?: boolean;
  }): Promise<void> {
    const recordHistory = options?.recordHistory ?? true;
    const preserveSecurity = options?.preserveSecurity ?? true;
    const allowSubmittedSecurity = options?.allowSubmittedSecurity ?? false;

    const normalized: StoredData = {
      version: data.version,
      settings: { ...data.settings },
      workspaces: data.workspaces.map((workspace) => normalizeWorkspace({ ...workspace })),
      workspaceSecurity: {},
    };

    for (const workspace of normalized.workspaces) {
      const prior = this.cache?.workspaceSecurity?.[workspace.id];
      const submitted = data.workspaceSecurity?.[workspace.id];
      if (preserveSecurity && prior) {
        const previousWorkspace = this.cache?.workspaces.find((candidate) => candidate.id === workspace.id);
        const changed = JSON.stringify(previousWorkspace) !== JSON.stringify(workspace);
        normalized.workspaceSecurity![workspace.id] = {
          ...prior,
          revision: changed ? prior.revision + 1 : prior.revision,
        };
      } else if (allowSubmittedSecurity && submitted) {
        normalized.workspaceSecurity![workspace.id] = { ...submitted };
      } else {
        normalized.workspaceSecurity![workspace.id] = { isTrusted: false, revision: 1 };
      }
    }

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

    if (recordHistory && this.cache) {
      this.pushUndoSnapshot(this.cache);
    }

    this.cache = normalized;
    await this.persistCache({ recordHistory: false });
  }

  async getWorkspaces(): Promise<Workspace[]> {
    await this.ensureLoaded();
    return this.cache!.workspaces.map((workspace) => this.cloneWorkspace(workspace));
  }

  async getStoredWorkspace(workspaceId: string): Promise<StoredWorkspace | null> {
    await this.ensureLoaded();
    const content = this.cache!.workspaces.find((workspace) => workspace.id === workspaceId);
    if (!content) {
      return null;
    }
    const security = this.cache!.workspaceSecurity?.[workspaceId] ?? { isTrusted: true, revision: 1 };
    return {
      content: this.cloneWorkspace(content),
      security: { ...security },
      revision: security.revision,
    };
  }

  async getWorkspaceSecurity(workspaceId: string): Promise<WorkspaceSecurityMetadata | null> {
    const stored = await this.getStoredWorkspace(workspaceId);
    return stored ? { ...stored.security } : null;
  }

  async grantTrust(workspaceId: string, reviewToken: WorkspaceReviewToken): Promise<"granted" | "already" | "changed" | "invalid" | "missing"> {
    await this.flushRecentWrites();
    const data = await this.load();
    const workspace = data.workspaces.find((candidate) => candidate.id === workspaceId);
    if (!workspace) {
      return "missing";
    }
    const security = data.workspaceSecurity?.[workspaceId] ?? { isTrusted: true, revision: 1 };
    const currentStored: StoredWorkspace = { content: workspace, security, revision: security.revision };
    if (security.isTrusted) {
      return "already";
    }
    if (!matchesReviewToken(currentStored, reviewToken)) {
      return "changed";
    }
    const validation = validateWorkspace(workspace);
    if (!validation.ok) {
      return "invalid";
    }
    data.workspaceSecurity = { ...(data.workspaceSecurity ?? {}) };
    data.workspaceSecurity[workspaceId] = { isTrusted: true, revision: security.revision + 1 };
    await this.save(data, { preserveSecurity: false, allowSubmittedSecurity: true });
    return "granted";
  }

  async revokeTrust(workspaceId: string): Promise<"revoked" | "already" | "missing"> {
    await this.flushRecentWrites();
    const data = await this.load();
    const workspace = data.workspaces.find((candidate) => candidate.id === workspaceId);
    if (!workspace) {
      return "missing";
    }
    const security = data.workspaceSecurity?.[workspaceId] ?? { isTrusted: true, revision: 1 };
    if (!security.isTrusted) {
      return "already";
    }
    data.workspaceSecurity = { ...(data.workspaceSecurity ?? {}) };
    data.workspaceSecurity[workspaceId] = { isTrusted: false, revision: security.revision + 1 };
    await this.save(data, { preserveSecurity: false, allowSubmittedSecurity: true });
    return "revoked";
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
      data.workspaceSecurity = { ...(data.workspaceSecurity ?? {}) };
      data.workspaceSecurity[normalized.id] = { isTrusted: true, revision: 1 };
    }

    await this.save(data, { allowSubmittedSecurity: true });
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

    const sourceSecurity = data.workspaceSecurity?.[workspaceId] ?? { isTrusted: true, revision: 1 };
    const result = await this.upsertWorkspace(duplicate);
    if (!sourceSecurity.isTrusted) {
      const updated = await this.load();
      updated.workspaceSecurity = { ...(updated.workspaceSecurity ?? {}) };
      updated.workspaceSecurity[result.id] = { ...sourceSecurity };
      await this.save(updated, { preserveSecurity: false });
    }
    return result;
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
      void this.flushRecentWrites().catch(() => {
        this.recentWriteDirty = true;
      });
    }, RECENT_WRITE_DEBOUNCE_MS);
  }

  private cloneData(data: StoredData): StoredData {
    return {
      version: data.version,
      settings: { ...data.settings },
      workspaces: data.workspaces.map((workspace) => ({
        ...workspace,
        launches: workspace.launches.map((launch) => ({ ...launch })),
        companionApps: workspace.companionApps?.map((entry) => ({ ...entry })),
      })),
      workspaceSecurity: data.workspaceSecurity
        ? Object.fromEntries(Object.entries(data.workspaceSecurity).map(([id, security]) => [id, { ...security }]))
        : {},
    };
  }

  private cloneWorkspace(workspace: Workspace): Workspace {
    return {
      ...workspace,
      launches: workspace.launches.map((launch) => ({ ...launch })),
      companionApps: workspace.companionApps?.map((entry) => ({ ...entry })),
    };
  }

  private preserveCurrentTrust(next: StoredData, current: StoredData | null): StoredData {
    const currentSecurity = current?.workspaceSecurity ?? {};
    next.workspaceSecurity = Object.fromEntries(
      next.workspaces.map((workspace) => [
        workspace.id,
        currentSecurity[workspace.id]
          ? { ...currentSecurity[workspace.id] }
          : { isTrusted: false, revision: 1 },
      ]),
    );
    return next;
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
