import { describe, expect, it } from "vitest";
import { createEmptyStoredData } from "../lib/schema";
import { summarizeImportConflicts } from "../lib/import-export";
import { parseWslDistroListOutput } from "../lib/terminal-catalog";
import { createMemoryStorageAdapter, QuickShellStorage } from "../lib/storage";
import { createStableId } from "../lib/ids";
import { normalizeWorkspace } from "../lib/validation";

describe("parseWslDistroListOutput", () => {
  it("parses UTF-16LE wsl -l -q output", () => {
    const text = "Ubuntu\0\r\nDebian\0\r\n";
    const buffer = Buffer.from(text, "utf16le");
    expect(parseWslDistroListOutput(buffer)).toEqual(["Ubuntu", "Debian"]);
  });
});

describe("summarizeImportConflicts", () => {
  it("flags rename conflicts against existing names", () => {
    const existing = createEmptyStoredData();
    existing.workspaces = [
      normalizeWorkspace({
        id: "existing",
        name: "Demo",
        directory: "C:\\Projects\\demo",
        terminal: "wt",
        command: "echo",
        runAsAdmin: false,
        isPinned: false,
        launches: [],
      }),
    ];

    const raw = JSON.stringify({
      version: 1,
      workspaces: [
        {
          id: "incoming",
          name: "Demo",
          directory: "C:\\Projects\\other",
          terminal: "wt",
          command: "echo",
          runAsAdmin: false,
          isPinned: false,
          launches: [],
        },
      ],
      settings: existing.settings,
    });

    const summary = summarizeImportConflicts(raw, existing);
    expect(summary.hasConflicts).toBe(true);
    expect(summary.renamed).toBe(1);
    expect(summary.imported).toBe(1);
  });
});

describe("moveFavorite", () => {
  it("swaps pinOrder among favorites and no-ops at edges", async () => {
    const storage = new QuickShellStorage(createMemoryStorageAdapter());
    const first = await storage.upsertWorkspace(
      normalizeWorkspace({
        id: createStableId(),
        name: "A",
        directory: "C:\\a",
        terminal: "wt",
        command: "echo",
        runAsAdmin: false,
        isPinned: false,
        launches: [],
      }),
    );
    const second = await storage.upsertWorkspace(
      normalizeWorkspace({
        id: createStableId(),
        name: "B",
        directory: "C:\\b",
        terminal: "wt",
        command: "echo",
        runAsAdmin: false,
        isPinned: false,
        launches: [],
      }),
    );
    await storage.setFavorite(first.id, true);
    await storage.setFavorite(second.id, true);

    const pinned = (await storage.getWorkspaces())
      .filter((w) => w.isPinned)
      .sort((l, r) => (l.pinOrder ?? Number.MAX_SAFE_INTEGER) - (r.pinOrder ?? Number.MAX_SAFE_INTEGER));
    expect(pinned.map((w) => w.id)).toEqual([first.id, second.id]);

    await storage.moveFavorite(second.id, "up");
    const afterUp = (await storage.getWorkspaces())
      .filter((w) => w.isPinned)
      .sort((l, r) => (l.pinOrder ?? Number.MAX_SAFE_INTEGER) - (r.pinOrder ?? Number.MAX_SAFE_INTEGER));
    expect(afterUp.map((w) => w.id)).toEqual([second.id, first.id]);

    await storage.moveFavorite(second.id, "up");
    const atTop = (await storage.getWorkspaces())
      .filter((w) => w.isPinned)
      .sort((l, r) => (l.pinOrder ?? Number.MAX_SAFE_INTEGER) - (r.pinOrder ?? Number.MAX_SAFE_INTEGER));
    expect(atTop.map((w) => w.id)).toEqual([second.id, first.id]);
  });

  it("orders null pinOrder like Favorites list then normalizes on move", async () => {
    const storage = new QuickShellStorage(createMemoryStorageAdapter());
    const zebra = await storage.upsertWorkspace(
      normalizeWorkspace({
        id: createStableId(),
        name: "Zebra",
        directory: "C:\\z",
        terminal: "wt",
        command: "echo",
        runAsAdmin: false,
        isPinned: true,
        pinOrder: null,
        launches: [],
      }),
    );
    const alpha = await storage.upsertWorkspace(
      normalizeWorkspace({
        id: createStableId(),
        name: "Alpha",
        directory: "C:\\a",
        terminal: "wt",
        command: "echo",
        runAsAdmin: false,
        isPinned: true,
        pinOrder: null,
        launches: [],
      }),
    );

    const before = (await storage.getWorkspaces())
      .filter((w) => w.isPinned)
      .sort((l, r) => {
        const leftOrder = l.pinOrder ?? Number.MAX_SAFE_INTEGER;
        const rightOrder = r.pinOrder ?? Number.MAX_SAFE_INTEGER;
        if (leftOrder !== rightOrder) {
          return leftOrder - rightOrder;
        }
        return l.name.localeCompare(r.name, undefined, { sensitivity: "base" });
      });
    expect(before.map((w) => w.id)).toEqual([alpha.id, zebra.id]);

    await storage.moveFavorite(zebra.id, "up");
    const after = await storage.getWorkspaces();
    const pinned = after
      .filter((w) => w.isPinned)
      .sort((l, r) => (l.pinOrder ?? Number.MAX_SAFE_INTEGER) - (r.pinOrder ?? Number.MAX_SAFE_INTEGER));
    expect(pinned.map((w) => w.id)).toEqual([zebra.id, alpha.id]);
    expect(pinned.every((w) => typeof w.pinOrder === "number")).toBe(true);
  });
});
