import { describe, expect, it } from "vitest";
import type { Workspace } from "../lib/schema";
import { DEFAULT_SETTINGS } from "../lib/schema";
import { assessWorkspaceHealth, formatHealthIssues } from "../lib/workspace-health";

const workspace: Workspace = {
  id: "1",
  name: "Frontend",
  abbreviation: "fe",
  directory: "C:\\Projects\\web",
  isPinned: false,
  pinOrder: null,
  lastUsedUtc: null,
  terminal: "wt",
  wtProfile: null,
  command: "npm run dev",
  runAsAdmin: false,
  launches: [
    {
      id: "1a",
      label: "Web",
      terminal: "wt",
      wtProfile: null,
      command: "npm run dev",
      runAsAdmin: false,
      isEnabled: true,
      order: 0,
      taskType: "none",
    },
  ],
};

describe("workspace-health", () => {
  it("reports validation failures honestly", () => {
    const report = assessWorkspaceHealth({ ...workspace, name: "" }, DEFAULT_SETTINGS);
    expect(report.ok).toBe(false);
    expect(report.issues.some((issue) => issue.code === "validation")).toBe(true);
  });

  it("joins multiple issues for display", () => {
    const message = formatHealthIssues([
      { code: "a", message: "First problem." },
      { code: "b", message: "Second problem." },
    ]);
    expect(message).toBe("First problem. Second problem.");
  });

  it("flags non-windows platforms for launch", () => {
    const originalPlatform = process.platform;
    Object.defineProperty(process, "platform", { value: "darwin" });
    const report = assessWorkspaceHealth(workspace, DEFAULT_SETTINGS);
    Object.defineProperty(process, "platform", { value: originalPlatform });
    expect(report.issues.some((issue) => issue.code === "platform")).toBe(true);
  });
});
