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
    expect(escapeWindowsArgument("C:\\Projects\\My App")).toBe('"C:\\Projects\\My App"');
  });

  it("leaves values without spaces, tabs, or quotes unescaped", () => {
    expect(escapeWindowsArgument("C:\\Projects\\App")).toBe("C:\\Projects\\App");
  });

  it("doubles backslashes immediately preceding an embedded quote", () => {
    // "foo\"bar" -> backslash before the embedded quote must be tripled
    // (2n+1 rule) so the consumer doesn't see it as an escaped quote terminator.
    const expected = '"' + "foo" + "\\".repeat(3) + '"' + "bar" + '"';
    expect(escapeWindowsArgument('foo\\"bar')).toBe(expected);
  });

  it("doubles a trailing backslash that lands right before the closing quote", () => {
    // A lone trailing backslash must become two backslashes once the closing
    // quote is appended, otherwise it would escape that quote instead.
    const expected = '"' + "C:\\Some Path" + "\\".repeat(2) + '"';
    expect(escapeWindowsArgument("C:\\Some Path\\")).toBe(expected);
  });

  it("handles a backslash directly followed by a quote mid-string", () => {
    const expected = '"' + "a" + "\\".repeat(3) + '"' + "b" + '"';
    expect(escapeWindowsArgument('a\\"b')).toBe(expected);
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
    expect(args.join(" ")).toContain("npm run dev");
  });

  it("resolves intelligent terminal targets", () => {
    const target = resolveLaunchTarget("it", "PowerShell");
    expect(target.hostExecutable).toBe("wtai.exe");
    expect(target.displayName).toContain("Intelligent Terminal");
  });

  it("passes package manager commands directly to wt when directory is set separately", () => {
    const plan = buildWorkspaceLaunchPlan(workspace, DEFAULT_SETTINGS);
    const args = buildLaunchArguments(plan.entries[0]);
    expect(args.join(" ")).toContain("npm run dev");
    expect(args.join(" ")).not.toContain("cd /d");
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
    expect(plan.groupedArguments.join(" ")).not.toContain("-w");
  });
});
