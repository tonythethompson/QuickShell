import { createStableId } from "./ids";
import { migrateStoredData } from "./migration";
import type { StoredData, Workspace } from "./schema";
import { createEmptyStoredData } from "./schema";

type UnknownRecord = Record<string, unknown>;

export type ImportResult = {
  data: StoredData;
  imported: number;
  skipped: number;
  renamed: number;
};

export function exportStoredData(data: StoredData): string {
  return JSON.stringify(data, null, 2);
}

export function parseImportPayload(raw: string): ImportResult {
  const parsed = JSON.parse(raw) as unknown;
  return importParsedPayload(parsed);
}

export function importParsedPayload(parsed: unknown, existing?: StoredData): ImportResult {
  if (Array.isArray(parsed)) {
    return importShortcutArray(parsed, existing);
  }

  if (!parsed || typeof parsed !== "object") {
    throw new Error("Import file must be a JSON object or workspace array.");
  }

  const record = parsed as UnknownRecord;

  if (Array.isArray(record.shortcuts)) {
    return importShortcutArray(record.shortcuts, existing);
  }

  if (Array.isArray(record.workspaces)) {
    const migrated = migrateStoredData(normalizeRecordKeys(record));
    return mergeImportedData(migrated, existing);
  }

  const migrated = migrateStoredData(normalizeRecordKeys(record));
  if (migrated.workspaces.length === 0) {
    throw new Error("No workspaces found in import file.");
  }
  return mergeImportedData(migrated, existing);
}

function importShortcutArray(items: unknown[], existing?: StoredData): ImportResult {
  const normalizedItems = items
    .map((item) => normalizeRecordKeys(item))
    .filter((item) => {
      if (!item || typeof item !== "object") {
        return false;
      }
      const record = item as UnknownRecord;
      if (record.type === "separator") {
        return false;
      }
      return true;
    });

  const migrated = migrateStoredData({
    version: 1,
    workspaces: normalizedItems,
    settings: existing?.settings ?? createEmptyStoredData().settings,
  });

  return mergeImportedData(migrated, existing);
}

function mergeImportedData(imported: StoredData, existing?: StoredData): ImportResult {
  const base = existing ?? createEmptyStoredData();
  const names = new Set(base.workspaces.map((workspace) => workspace.name.toLowerCase()));
  const ids = new Set(base.workspaces.map((workspace) => workspace.id));
  let renamed = 0;
  let skipped = 0;
  const merged: Workspace[] = [...base.workspaces];

  for (const workspace of imported.workspaces) {
    let next = workspace;
    if (ids.has(workspace.id)) {
      next = { ...next, id: createStableId() };
    }

    if (names.has(next.name.toLowerCase())) {
      const suffixed = `${workspace.name} (imported)`;
      if (names.has(suffixed.toLowerCase())) {
        skipped += 1;
        continue;
      }
      next = { ...next, name: suffixed };
      renamed += 1;
    }

    names.add(next.name.toLowerCase());
    ids.add(next.id);
    merged.push(next);
  }

  return {
    data: {
      version: imported.version,
      settings: { ...base.settings, ...imported.settings },
      workspaces: merged,
    },
    imported: merged.length - base.workspaces.length,
    skipped,
    renamed,
  };
}

function normalizeRecordKeys(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map((item) => normalizeRecordKeys(item));
  }
  if (!value || typeof value !== "object") {
    return value;
  }

  const record = value as UnknownRecord;
  const normalized: UnknownRecord = {};
  for (const [key, nested] of Object.entries(record)) {
    const normalizedKey = key.length > 0 ? key[0].toLowerCase() + key.slice(1) : key;
    normalized[normalizedKey] = normalizeRecordKeys(nested);
  }
  return normalized;
}
