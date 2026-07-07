import { showToast, Toast } from "@raycast/api";
import { showFailureToast } from "@raycast/utils";
import type { WorkspaceHealthIssue } from "./workspace-health";
import { formatHealthIssues } from "./workspace-health";
import type { LaunchExecutionResult } from "./launch-executor";

export async function showWorkspaceValidationFailure(message: string): Promise<void> {
  await showFailureToast(new Error(message), { title: "Workspace is not ready" });
}

export async function showHealthFailure(
  issues: WorkspaceHealthIssue[],
): Promise<void> {
  const message = formatHealthIssues(issues);
  await showFailureToast(new Error(message), { title: "Cannot open workspace" });
}

export async function showLaunchFailure(
  result: Extract<LaunchExecutionResult, { ok: false }>,
): Promise<void> {
  await showFailureToast(result.cause ?? new Error(result.message), { title: "Launch failed" });
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

export async function showStorageFailure(action: string, error: unknown): Promise<void> {
  await showFailureToast(error, { title: `${action} failed` });
}
