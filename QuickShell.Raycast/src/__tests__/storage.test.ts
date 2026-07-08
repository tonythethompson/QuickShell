import { describe, expect, it } from "vitest";
import { QuickShellStorage, createMemoryStorageAdapter } from "../lib/storage";
import { createStableId } from "../lib/ids";
import { normalizeWorkspace } from "../lib/validation";

describe("storage", () => {
  it("persists and reloads workspaces", async () => {
    const storage = new QuickShellStorage(createMemoryStorageAdapter());
    const workspace = normalizeWorkspace({
      id: createStableId(),
      name: "Demo",
      abbreviation: "demo",
      directory: "C:\\Projects\\Demo",
      isPinned: false,
      pinOrder: null,
      lastUsedUtc: null,
      terminal: "default",
      wtProfile: null,
      command: "npm run dev",
      runAsAdmin: false,
      launches: [
        {
          id: createStableId(),
          label: "Web",
          terminal: "default",
          wtProfile: null,
          command: "npm run dev",
          runAsAdmin: false,
          isEnabled: true,
          order: 0,
          taskType: "none",
        },
      ],
    });

    await storage.upsertWorkspace(workspace);
    const loaded = await storage.getWorkspaces();
    expect(loaded).toHaveLength(1);
    expect(loaded[0].name).toBe("Demo");
  });

  it("marks workspace usage for recents", async () => {
    const storage = new QuickShellStorage(createMemoryStorageAdapter());
    const id = createStableId();
    await storage.upsertWorkspace(
      normalizeWorkspace({
        id,
        name: "Demo",
        abbreviation: null,
        directory: "C:\\Projects\\Demo",
        isPinned: false,
        pinOrder: null,
        lastUsedUtc: null,
        terminal: "default",
        wtProfile: null,
        command: null,
        runAsAdmin: false,
        launches: [
          {
            id: createStableId(),
            label: "Launch",
            terminal: "default",
            wtProfile: null,
            command: null,
            runAsAdmin: false,
            isEnabled: true,
            order: 0,
            taskType: "none",
          },
        ],
      }),
    );

    await storage.markWorkspaceUsed(id, new Date("2026-07-06T12:00:00.000Z"));
    await storage.flushRecentWrites();
    const loaded = await storage.getWorkspaces();
    expect(loaded[0].lastUsedUtc).toBe("2026-07-06T12:00:00.000Z");
  });

  it("persists settings updates", async () => {
    const storage = new QuickShellStorage(createMemoryStorageAdapter());
    await storage.updateSettings({
      terminalApplication: "conhost",
      defaultProfile: "pwsh",
      recentWorkspaceCount: 0,
    });

    const settings = await storage.getSettings();
    expect(settings.terminalApplication).toBe("conhost");
    expect(settings.defaultProfile).toBe("pwsh");
    expect(settings.recentWorkspaceCount).toBe(0);
  });

  it("does not record undo history when save validation fails", async () => {
    const storage = new QuickShellStorage(createMemoryStorageAdapter());
    const id = createStableId();
    await storage.upsertWorkspace(
      normalizeWorkspace({
        id,
        name: "Before",
        abbreviation: null,
        directory: "C:\\Projects\\Before",
        isPinned: false,
        pinOrder: null,
        lastUsedUtc: null,
        terminal: "default",
        wtProfile: null,
        command: null,
        runAsAdmin: false,
        launches: [
          {
            id: createStableId(),
            label: "Launch",
            terminal: "default",
            wtProfile: null,
            command: null,
            runAsAdmin: false,
            isEnabled: true,
            order: 0,
            taskType: "none",
          },
        ],
      }),
    );

    await expect(
      storage.save({
        version: 1,
        settings: {
          terminalApplication: "wt",
          defaultProfile: "__default__",
          recentWorkspaceCount: 8,
        },
        workspaces: [
          normalizeWorkspace({
            id,
            name: "",
            abbreviation: null,
            directory: "C:\\Projects\\Before",
            isPinned: false,
            pinOrder: null,
            lastUsedUtc: null,
            terminal: "default",
            wtProfile: null,
            command: null,
            runAsAdmin: false,
            launches: [
              {
                id: createStableId(),
                label: "Launch",
                terminal: "default",
                wtProfile: null,
                command: null,
                runAsAdmin: false,
                isEnabled: true,
                order: 0,
                taskType: "none",
              },
            ],
          }),
        ],
      }),
    ).rejects.toThrow();

    expect(await storage.getWorkspaces()).toHaveLength(1);
    await storage.undo();
    expect(await storage.getWorkspaces()).toHaveLength(0);
    expect(storage.canUndo()).toBe(false);
  });

  it("supports undo after workspace changes", async () => {
    const storage = new QuickShellStorage(createMemoryStorageAdapter());
    const id = createStableId();
    await storage.upsertWorkspace(
      normalizeWorkspace({
        id,
        name: "Before",
        abbreviation: null,
        directory: "C:\\Projects\\Before",
        isPinned: false,
        pinOrder: null,
        lastUsedUtc: null,
        terminal: "default",
        wtProfile: null,
        command: null,
        runAsAdmin: false,
        launches: [
          {
            id: createStableId(),
            label: "Launch",
            terminal: "default",
            wtProfile: null,
            command: null,
            runAsAdmin: false,
            isEnabled: true,
            order: 0,
            taskType: "none",
          },
        ],
      }),
    );

    await storage.deleteWorkspace(id);
    expect(await storage.getWorkspaces()).toHaveLength(0);
    await storage.undo();
    expect(await storage.getWorkspaces()).toHaveLength(1);
    expect((await storage.getWorkspaces())[0].name).toBe("Before");
  });
});
