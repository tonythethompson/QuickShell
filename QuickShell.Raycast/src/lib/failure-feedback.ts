import { showToast, Toast } from "@raycast/api";
import type { WorkspaceHealthIssue } from "./workspace-health";
import { formatHealthIssues } from "./workspace-health";
import type { LaunchExecutionResult } from "./launch-executor";

export async function showWorkspaceValidationFailure(
  message: string,
): Promise<void> {
  await showToast({
    style: Toast.Style.Failure,
    title: "Workspace is not ready",
    message,
  });
}

export async function showHealthFailure(
  issues: WorkspaceHealthIssue[],
): Promise<void> {
  const message = formatHealthIssues(issues);
  await showToast({
    style: Toast.Style.Failure,
    title: "Cannot open workspace",
    message,
  });
}

export async function showLaunchFailure(
  result: Extract<LaunchExecutionResult, { ok: false }>,
): Promise<void> {
  await showToast({
    style: Toast.Style.Failure,
    title: "Launch failed",
    message: result.message,
  });
}

export async function showLaunchSuccess(
  title: string,
  message: string,
): Promise<void> {
  await showToast({
    style: Toast.Style.Success,
    title,
    message,
  });
}

export async function showStorageFailure(
  action: string,
  error: unknown,
): Promise<void> {
  const message = error instanceof Error ? error.message : `${action} failed.`;
  await showToast({
    style: Toast.Style.Failure,
    title: `${action} failed`,
    message,
  });
}
