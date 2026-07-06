import { describe, expect, it } from "vitest";
import type { Workspace } from "../lib/schema";
import {
  additionalLaunchCount,
  buildWorkspaceFromFormState,
  filterWorkspacesForEdit,
  workspaceFormStateFromWorkspace,
} from "../lib/workspace-form-state";

const multiLaunchWorkspace: Workspace = {
  id: "1",
  name: "Full stack",
  abbreviation: "fs",
  directory: "C:\\Projects\\fullstack",
  isPinned: false,
  pinOrder: null,
  lastUsedUtc: null,
  terminal: "wt",
  wtProfile: null,
  command: "dotnet run",
  runAsAdmin: false,
  launches: [
    {
      id: "1a",
      label: "API",
      terminal: "wt",
      wtProfile: null,
      command: "dotnet run",
      runAsAdmin: false,
      isEnabled: true,
      order: 0,
      taskType: "none",
    },
    {
      id: "1b",
      label: "Web",
      terminal: "wt",
      wtProfile: null,
      command: "npm run dev",
      runAsAdmin: false,
      isEnabled: true,
      order: 1,
      taskType: "none",
    },
  ],
};

describe("workspace-form-state", () => {
  it("preserves additional launches when editing the primary launch", () => {
    const next = buildWorkspaceFromFormState(multiLaunchWorkspace, {
      name: "Full stack",
      abbreviation: "fs",
      directory: "C:\\Projects\\fullstack",
      terminal: "wt",
      wtProfile: "",
      command: "dotnet watch run",
      isPinned: false,
      runAsAdmin: false,
      launchLabel: "API",
    });

    expect(next.launches).toHaveLength(2);
    expect(next.launches[0].command).toBe("dotnet watch run");
    expect(next.launches[1].label).toBe("Web");
    expect(next.launches[1].command).toBe("npm run dev");
  });

  it("derives form state from the first enabled launch", () => {
    const state = workspaceFormStateFromWorkspace(multiLaunchWorkspace);
    expect(state.command).toBe("dotnet run");
    expect(state.launchLabel).toBe("API");
  });

  it("filters workspaces for edit by name and launch text", () => {
    const results = filterWorkspacesForEdit(
      [
        multiLaunchWorkspace,
        {
          ...multiLaunchWorkspace,
          id: "2",
          name: "Docs",
          abbreviation: "docs",
          directory: "C:\\Projects\\docs",
          launches: [],
        },
      ],
      "npm",
    );

    expect(results).toHaveLength(1);
    expect(results[0].id).toBe("1");
  });

  it("counts additional enabled launches", () => {
    expect(additionalLaunchCount(multiLaunchWorkspace)).toBe(1);
  });
});
