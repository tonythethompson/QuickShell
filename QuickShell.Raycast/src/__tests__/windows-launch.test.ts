import { describe, expect, it } from "vitest";
import {
  buildLaunchArguments,
  buildWorkspaceLaunchPlan,
  escapeWindowsArgument,
  resolveLaunchTarget,
} from "../lib/windows-launch";
import type { Workspace } from "../lib/schema";
import { DEFAULT_SETTINGS } from "../lib/schema";

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
      wtProfile: "PowerShell",
      command: "npm run dev",
      runAsAdmin: false,
      isEnabled: true,
      order: 0,
      taskType: "none",
    },
  ],
};

describe("windows-launch", () => {
  it("escapes arguments with spaces", () => {
    expect(escapeWindowsArgument("C:\\Projects\\My App")).toBe(
      '"C:\\Projects\\My App"',
    );
  });

  it("resolves windows terminal targets", () => {
    const target = resolveLaunchTarget("wt", "PowerShell");
    expect(target.kind).toBe("wt");
    expect(target.hostExecutable).toBe("wt.exe");
    expect(target.profileOrDistro).toBe("PowerShell");
  });

  it("builds wt launch arguments with profile and directory", () => {
    const plan = buildWorkspaceLaunchPlan(workspace, DEFAULT_SETTINGS);
    const args = buildLaunchArguments(plan.entries[0]);
    expect(args).toContain("-p");
    expect(args).toContain("PowerShell");
    expect(args).toContain("-d");
    expect(args).toContain("C:\\Projects\\web");
    expect(args).toContain("npm run dev");
  });

  it("groups multiple launches for windows terminal", () => {
    const multi: Workspace = {
      ...workspace,
      launches: [
        workspace.launches[0],
        {
          ...workspace.launches[0],
          id: "1b",
          label: "API",
          command: "dotnet run",
          order: 1,
        },
      ],
    };

    const plan = buildWorkspaceLaunchPlan(multi, DEFAULT_SETTINGS);
    expect(plan.entries).toHaveLength(2);
    expect(plan.groupedArguments.join(" ")).toContain("new-tab");
  });
});
