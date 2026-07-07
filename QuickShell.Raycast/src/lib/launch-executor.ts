import type { QuickShellSettings } from "./schema";
import { runPostLaunchActions } from "./post-launch-actions";
import {
  buildLaunchArguments,
  buildWorkspaceLaunchPlan,
  type LaunchOptions,
  type LaunchPlan,
  type LaunchPlanEntry,
} from "./windows-launch";

export type ExecFn = (command: string, args: string[]) => Promise<void>;

export type LaunchExecutionResult =
  | { ok: true; summary: string; postLaunchWarnings?: string[] }
  | { ok: false; message: string; cause?: unknown };

type LaunchGroup = {
  hostExecutable: string;
  runAsAdmin: boolean;
  entries: LaunchPlanEntry[];
};

export async function executeWorkspaceLaunch(
  plan: LaunchPlan,
  settings: QuickShellSettings,
  execFn: ExecFn,
  options?: LaunchOptions & { includeCompanion?: boolean; includeDevServer?: boolean },
): Promise<LaunchExecutionResult> {
  if (plan.errors.length > 0) {
    return { ok: false, message: plan.errors.join(" ") };
  }

  if (plan.entries.length === 0) {
    return { ok: false, message: "No enabled launch entries." };
  }

  if (process.platform !== "win32") {
    return { ok: false, message: "Terminal launch requires Windows." };
  }

  try {
    const groups = groupLaunchEntries(plan.entries);
    for (const group of groups) {
      await executeGroup(group, execFn);
    }

    const workspace = plan.entries[0].workspace;
    const postLaunch = await runPostLaunchActions(workspace, {
      includeCompanion: options?.includeCompanion ?? true,
      includeDevServer: options?.includeDevServer ?? true,
    });

    return {
      ok: true,
      summary:
        plan.entries.length === 1
          ? `${plan.entries[0].target.displayName} → ${plan.entries[0].directory}`
          : `${plan.entries.length} launches started`,
      postLaunchWarnings: postLaunch.warnings.length > 0 ? postLaunch.warnings : undefined,
    };
  } catch (error) {
    const message = error instanceof Error ? error.message : "Launch failed.";
    return { ok: false, message, cause: error };
  }
}

export async function executeWorkspace(
  workspace: Parameters<typeof buildWorkspaceLaunchPlan>[0],
  settings: QuickShellSettings,
  execFn: ExecFn,
  options?: LaunchOptions,
): Promise<LaunchExecutionResult> {
  const plan = buildWorkspaceLaunchPlan(workspace, settings, options);
  return executeWorkspaceLaunch(plan, settings, execFn, options);
}

function groupLaunchEntries(entries: LaunchPlanEntry[]): LaunchGroup[] {
  const groups: LaunchGroup[] = [];

  for (const entry of entries) {
    const runAsAdmin = entry.runAsAdmin;
    const canTab = entry.target.kind === "wt";
    const lastGroup = groups[groups.length - 1];

    if (
      canTab &&
      lastGroup &&
      lastGroup.runAsAdmin === runAsAdmin &&
      lastGroup.hostExecutable === entry.target.hostExecutable &&
      lastGroup.entries.every((item) => item.target.kind === "wt")
    ) {
      lastGroup.entries.push(entry);
      continue;
    }

    groups.push({
      hostExecutable: entry.target.hostExecutable,
      runAsAdmin,
      entries: [entry],
    });
  }

  return groups;
}

async function executeGroup(group: LaunchGroup, execFn: ExecFn): Promise<void> {
  if (group.entries.length === 1) {
    const entry = group.entries[0];
    const args = buildLaunchArguments(entry);
    await runProcess(group.hostExecutable, args, group.runAsAdmin, execFn);
    return;
  }

  const args: string[] = [];
  for (let index = 0; index < group.entries.length; index++) {
    const entry = group.entries[index];
    if (index > 0) {
      args.push(";", "new-tab");
    }
    args.push(...buildLaunchArguments(entry));
  }

  await runProcess(group.hostExecutable, args, group.runAsAdmin, execFn);
}

async function runProcess(
  hostExecutable: string,
  args: string[],
  runAsAdmin: boolean,
  execFn: ExecFn,
): Promise<void> {
  if (!runAsAdmin) {
    await execFn(hostExecutable, args);
    return;
  }

  const escapedArgs = args.map((arg) => `'${arg.replace(/'/g, "''")}'`).join(", ");
  const command = `Start-Process -FilePath '${hostExecutable.replace(/'/g, "''")}' -ArgumentList ${escapedArgs} -Verb RunAs`;
  await execFn("powershell.exe", ["-NoProfile", "-Command", command]);
}
