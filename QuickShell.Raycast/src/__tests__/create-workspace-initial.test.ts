import { describe, expect, it } from "vitest";
import { createBlankWorkspace, createWorkspaceFromDirectory } from "../lib/create-workspace-initial";

describe("create-workspace-initial", () => {
  it("returns an empty workspace when directory is missing", () => {
    const workspace = createWorkspaceFromDirectory(undefined);
    expect(workspace.directory).toBe("");
    expect(workspace.name).toBe("");
  });

  it("prefills name and directory from a path argument", () => {
    const workspace = createWorkspaceFromDirectory("C:\\Projects\\QuickShell");
    expect(workspace.directory).toBe("C:\\Projects\\QuickShell");
    expect(workspace.name).toBe("QuickShell");
    expect(workspace.id).not.toBe(createBlankWorkspace().id);
  });
});
