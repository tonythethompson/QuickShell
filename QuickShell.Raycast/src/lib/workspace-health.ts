import { existsSync } from "node:fs";
import type { QuickShellSettings, Workspace } from "./schema";
import { isAbsoluteDirectory, validateWorkspace } from "./validation";
import { buildWorkspaceLaunchPlan } from "./windows-launch";

export type WorkspaceHealthIssue = {
  code: string;
  message: string;
};

export type WorkspaceHealthReport = {
  ok: boolean;
  issues: WorkspaceHealthIssue[];
};

export function assessWorkspaceHealth(
  workspace: Workspace,
  settings: QuickShellSettings,
): WorkspaceHealthReport {
  const issues: WorkspaceHealthIssue[] = [];

  const validation = validateWorkspace(workspace);
  if (!validation.ok) {
    issues.push({ code: "validation", message: validation.message });
  }

  const directory = workspace.directory.trim();
  if (directory && !isAbsoluteDirectory(directory)) {
    issues.push({
      code: "directory_relative",
      message: "Directory must be an absolute path.",
    });
  }

  if (
    directory &&
    process.platform === "win32" &&
    !directory.startsWith("\\\\wsl$\\") &&
    !existsSync(directory)
  ) {
    issues.push({
      code: "directory_missing",
      message: `Directory not found: ${directory}`,
    });
  }

  const plan = buildWorkspaceLaunchPlan(workspace, settings);
  for (const error of plan.errors) {
    issues.push({ code: "launch_plan", message: error });
  }

  if (process.platform !== "win32") {
    issues.push({
      code: "platform",
      message:
        "Terminal launch requires Windows. You can still edit workspaces on this machine.",
    });
  }

  return { ok: issues.length === 0, issues };
}

export function formatHealthIssues(issues: WorkspaceHealthIssue[]): string {
  return issues.map((issue) => issue.message).join(" ");
}

export function primaryHealthIssue(
  issues: WorkspaceHealthIssue[],
): string | undefined {
  return issues[0]?.message;
}
