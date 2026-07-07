import { describe, expect, it, vi } from "vitest";
import type { QuickShellSettings, Workspace } from "../lib/schema";
import { executeWorkspaceLaunch, type ExecFn } from "../lib/launch-executor";

const runPostLaunchActions = vi.fn(async () => ({
  companionOpened: false,
  devServerOpened: false,
  warnings: [] as string[],
}));

vi.mock("../lib/post-launch-actions", () => ({
  runPostLaunchActions: (...args: unknown[]) => runPostLaunchActions(...args),
}));

const settings: QuickShellSettings = {
  terminalApplication: "wt",
  defaultProfile: "__default__",
  recentWorkspaceCount: 8,
};

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

describe("launch-executor", () => {
  it("skips post-launch actions when companion and dev server are disabled", async () => {
    runPostLaunchActions.mockClear();
    const originalPlatform = process.platform;
    Object.defineProperty(process, "platform", { value: "win32" });
    const execFn: ExecFn = async () => undefined;

    const workspaceWithHooks: Workspace = {
      ...workspace,
      openDevServerOnLaunch: true,
      devServerUrl: "http://localhost:5173",
      openCompanionAppOnLaunch: true,
      companionAppPath: "C:\\Program Files\\Code.exe",
    };
    const { buildWorkspaceLaunchPlan } = await import("../lib/windows-launch");
    const plan = buildWorkspaceLaunchPlan(workspaceWithHooks, settings);
    const result = await executeWorkspaceLaunch(plan, settings, execFn, {
      includeCompanion: false,
      includeDevServer: false,
    });

    Object.defineProperty(process, "platform", { value: originalPlatform });
    expect(result.ok).toBe(true);
    expect(runPostLaunchActions).toHaveBeenCalledWith(
      workspaceWithHooks,
      expect.objectContaining({ includeCompanion: false, includeDevServer: false }),
    );
  });

  it("refuses launch on non-windows platforms", async () => {
    const originalPlatform = process.platform;
    Object.defineProperty(process, "platform", { value: "darwin" });
    const { buildWorkspaceLaunchPlan } = await import("../lib/windows-launch");
    const plan = buildWorkspaceLaunchPlan(workspace, settings);
    const execFn: ExecFn = async () => undefined;

    const result = await executeWorkspaceLaunch(plan, settings, execFn);

    Object.defineProperty(process, "platform", { value: originalPlatform });
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.message).toContain("Windows");
    }
  });

  it("executes a single windows terminal launch on win32", async () => {
    const originalPlatform = process.platform;
    Object.defineProperty(process, "platform", { value: "win32" });
    const calls: Array<{ command: string; args: string[] }> = [];
    const execFn: ExecFn = async (command, args) => {
      calls.push({ command, args });
    };

    const { buildWorkspaceLaunchPlan } = await import("../lib/windows-launch");
    const plan = buildWorkspaceLaunchPlan(workspace, settings);
    const result = await executeWorkspaceLaunch(plan, settings, execFn);

    Object.defineProperty(process, "platform", { value: originalPlatform });
    expect(result.ok).toBe(true);
    expect(calls).toHaveLength(1);
    expect(calls[0].command).toBe("wt.exe");
    expect(calls[0].args).toContain("-d");
    expect(calls[0].args).toContain("C:\\Projects\\web");
  });

  it("uses elevated powershell wrapper for admin launches", async () => {
    const originalPlatform = process.platform;
    Object.defineProperty(process, "platform", { value: "win32" });
    const calls: Array<{ command: string; args: string[] }> = [];
    const execFn: ExecFn = async (command, args) => {
      calls.push({ command, args });
    };

    const adminWorkspace: Workspace = {
      ...workspace,
      runAsAdmin: true,
      launches: [{ ...workspace.launches[0], runAsAdmin: true }],
    };
    const { buildWorkspaceLaunchPlan } = await import("../lib/windows-launch");
    const plan = buildWorkspaceLaunchPlan(adminWorkspace, settings);
    await executeWorkspaceLaunch(plan, settings, execFn);

    Object.defineProperty(process, "platform", { value: originalPlatform });
    expect(calls[0].command).toBe("powershell.exe");
    expect(calls[0].args.join(" ")).toContain("RunAs");
  });
});
