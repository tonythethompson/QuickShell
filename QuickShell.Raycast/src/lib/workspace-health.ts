import { existsSync } from "node:fs";
import type { QuickShellSettings, Workspace } from "./schema";
import { isAbsoluteDirectory, validateWorkspace } from "./validation";
import { validateLaunchPlanErrors } from "./windows-launch";

export type WorkspaceHealthIssue = {
  code: string;
  message: string;
  severity?: "error" | "warning";
};

export type WorkspaceHealthReport = {
  ok: boolean;
  issues: WorkspaceHealthIssue[];
};

export function assessWorkspaceHealthForList(
  workspace: Workspace,
  settings: QuickShellSettings,
): WorkspaceHealthReport {
  return assessWorkspaceHealth(workspace, settings, { includeLaunchPlan: false, includeDirectoryExists: true });
}

export function assessWorkspaceHealthForLaunch(
  workspace: Workspace,
  settings: QuickShellSettings,
): WorkspaceHealthReport {
  return assessWorkspaceHealth(workspace, settings, { includeLaunchPlan: true, includeDirectoryExists: true });
}

export function assessWorkspaceHealth(
  workspace: Workspace,
  settings: QuickShellSettings,
  options?: { includeLaunchPlan?: boolean; includeDirectoryExists?: boolean },
): WorkspaceHealthReport {
  const includeLaunchPlan = options?.includeLaunchPlan ?? true;
  const includeDirectoryExists = options?.includeDirectoryExists ?? true;
  const issues: WorkspaceHealthIssue[] = [];

  const validation = validateWorkspace(workspace);
  if (!validation.ok) {
    issues.push({ code: "validation", message: validation.message, severity: "error" });
  }

  const directory = workspace.directory.trim();
  if (directory && !isAbsoluteDirectory(directory)) {
    issues.push({ code: "directory_relative", message: "Directory must be an absolute path.", severity: "error" });
  }

  if (
    includeDirectoryExists &&
    directory &&
    process.platform === "win32" &&
    !directory.startsWith("\\\\wsl$\\") &&
    !existsSync(directory)
  ) {
    issues.push({ code: "directory_missing", message: `Directory not found: ${directory}`, severity: "error" });
  }

  if (includeLaunchPlan) {
    for (const error of validateLaunchPlanErrors(workspace, settings)) {
      issues.push({ code: "launch_plan", message: error, severity: "error" });
    }
  }

  if (workspace.openCompanionAppOnLaunch && workspace.companionAppPath?.trim()) {
    if (process.platform === "win32" && !existsSync(workspace.companionAppPath.trim())) {
      issues.push({
        code: "companion_missing",
        message: `Companion app not found: ${workspace.companionAppPath.trim()}`,
        severity: "warning",
      });
    }
  }

  if (process.platform !== "win32") {
    issues.push({
      code: "platform",
      message: "Terminal launch requires Windows. You can still edit workspaces on this machine.",
      severity: "error",
    });
  }

  const blocking = issues.filter((issue) => issue.severity !== "warning");
  return { ok: blocking.length === 0, issues };
}

export function formatHealthIssues(issues: WorkspaceHealthIssue[]): string {
  return issues.map((issue) => issue.message).join(" ");
}

export function primaryHealthIssue(issues: WorkspaceHealthIssue[]): string | undefined {
  return issues[0]?.message;
}
