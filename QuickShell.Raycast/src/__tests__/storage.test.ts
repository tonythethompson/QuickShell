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
    const loaded = await storage.getWorkspaces();
    expect(loaded[0].lastUsedUtc).toBe("2026-07-06T12:00:00.000Z");
  });
});
