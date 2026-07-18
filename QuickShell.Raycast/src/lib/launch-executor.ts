import type { QuickShellSettings } from "./schema";
import { groupLaunchEntries } from "./launch-grouping";
import { runPostLaunchActions, type OpenUrlFn } from "./post-launch-actions";
import type { AuthorizedPostLaunchEffectsPlan } from "./security";
import {
  buildLaunchArguments,
  buildGroupedWindowsTerminalArguments,
  buildWorkspaceLaunchPlan,
  type LaunchOptions,
  type LaunchPlan,
  type LaunchPlanEntry,
} from "./windows-launch";

export type ExecFn = (command: string, args: string[]) => Promise<void>;

export type LaunchExecutionResult =
  { ok: true; summary: string; postLaunchWarnings?: string[] } | { ok: false; message: string; cause?: unknown };
export type LaunchExecutionOptions = LaunchOptions & {
  authorizedEffects?: AuthorizedPostLaunchEffectsPlan;
  authorizationWarnings?: string[];
  openUrl?: OpenUrlFn;
};

export async function executeWorkspaceLaunch(
  plan: LaunchPlan,
  settings: QuickShellSettings,
  execFn: ExecFn,
  options?: LaunchExecutionOptions,
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

  const separateWindows = settings.multiLaunchPresentation === "separateWindows";

  try {
    const groups = groupLaunchEntries(plan.entries, settings, separateWindows);
    for (const group of groups) {
      await executeGroup(group.tabHostExecutable, group.runAsAdmin, group.entries, execFn);
    }

    const postLaunch = await runPostLaunchActions(
      options?.authorizedEffects ?? { companions: [], devServerUrl: null },
      { openUrl: options?.openUrl },
    );
    const postLaunchWarnings = [...(options?.authorizationWarnings ?? []), ...postLaunch.warnings];

    return {
      ok: true,
      summary:
        plan.entries.length === 1
          ? `${plan.entries[0].target.displayName} → ${plan.entries[0].directory}`
          : `${plan.entries.length} launches started`,
      postLaunchWarnings: postLaunchWarnings.length > 0 ? postLaunchWarnings : undefined,
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

async function executeGroup(
  tabHostExecutable: string | null,
  runAsAdmin: boolean,
  entries: LaunchPlanEntry[],
  execFn: ExecFn,
): Promise<void> {
  if (entries.length === 1) {
    const entry = entries[0];
    const host = tabHostExecutable ?? entry.target.hostExecutable;
    const args =
      tabHostExecutable && entry.target.kind !== "wt"
        ? buildGroupedWindowsTerminalArguments([entry])
        : buildLaunchArguments(entry);
    await runProcess(host, args, runAsAdmin, execFn);
    return;
  }

  const host = tabHostExecutable ?? entries[0].target.hostExecutable;
  const args = buildGroupedWindowsTerminalArguments(entries);
  await runProcess(host, args, runAsAdmin, execFn);
}

async function runProcess(hostExecutable: string, args: string[], runAsAdmin: boolean, execFn: ExecFn): Promise<void> {
  if (!runAsAdmin) {
    await execFn(hostExecutable, args);
    return;
  }

  const escapedArgs = args.map((arg) => `'${arg.replace(/'/g, "''")}'`).join(", ");
  const command = `Start-Process -FilePath '${hostExecutable.replace(/'/g, "''")}' -ArgumentList ${escapedArgs} -Verb RunAs`;
  await execFn("powershell.exe", ["-NoProfile", "-Command", command]);
}
