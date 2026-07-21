import { describe, expect, it } from "vitest";
import { COMPANION_PRESETS, resolveCompanionPreset } from "../lib/companion-catalog";
import { buildWorkspaceFromFormState, workspaceFormStateFromWorkspace } from "../lib/workspace-form-state";
import type { Workspace } from "../lib/schema";

describe("companion-catalog", () => {
  it("lists presets with stable ids and default arguments", () => {
    expect(COMPANION_PRESETS.length).toBeGreaterThan(5);
    expect(COMPANION_PRESETS.every((preset) => preset.id && preset.title && preset.candidatePaths.length > 0)).toBe(
      true,
    );
    expect(resolveCompanionPreset("missing-preset")).toBeNull();
  });
});

describe("multi companion form state", () => {
  it("round-trips multiple companions through form state", () => {
    const workspace: Workspace = {
      id: "ws-1",
      name: "Demo",
      directory: "C:\\Projects\\demo",
      terminal: "wt",
      command: "npm run dev",
      runAsAdmin: false,
      isPinned: false,
      launches: [
        {
          id: "l1",
          label: "Dev",
          terminal: "wt",
          command: "npm run dev",
          runAsAdmin: false,
          isEnabled: true,
          order: 0,
        },
      ],
      companionApps: [
        {
          id: "c1",
          path: "C:\\Apps\\Code.exe",
          arguments: ".",
          openOnLaunch: true,
          order: 0,
        },
        {
          id: "c2",
          path: "C:\\Apps\\Fork.exe",
          arguments: "{folder}",
          openOnLaunch: false,
          order: 1,
        },
      ],
    };

    const state = workspaceFormStateFromWorkspace(workspace);
    expect(state.companions).toHaveLength(2);
    expect(state.companions[1].path).toBe("C:\\Apps\\Fork.exe");

    const saved = buildWorkspaceFromFormState(workspace, {
      ...state,
      companions: [...state.companions, { id: "c3", path: "C:\\Apps\\Cursor.exe", arguments: ".", openOnLaunch: true }],
    });

    expect(saved.companionApps).toHaveLength(3);
    expect(saved.companionAppPath).toBe("C:\\Apps\\Code.exe");
    expect(saved.openCompanionAppOnLaunch).toBe(true);
  });
});
