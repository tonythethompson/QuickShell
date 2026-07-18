import { existsSync } from "node:fs";
import { isAbsolute, resolve } from "node:path";
import type { StoredWorkspace, Workspace } from "./schema";
import { validateWorkspace, validateCommand, isAbsoluteDirectory } from "./validation";

export type WorkspaceAction =
  | "LaunchTerminal"
  | "LaunchEntry"
  | "StartCompanion"
  | "OpenUrl"
  | "OpenDevServer"
  | "OpenDirectory"
  | "CopyPath"
  | "GrantTrust"
  | "RevokeTrust";

export type WorkspaceIssueCode =
  | "WorkspaceNotFound"
  | "WorkspaceUntrusted"
  | "InvalidDirectory"
  | "DirectoryMissing"
  | "InvalidCommand"
  | "InvalidLaunch"
  | "InvalidUrl"
  | "InvalidCompanion"
  | "CompanionExecutableUnavailable"
  | "DirectoryOpenNotAllowed"
  | "ActionNotAllowed"
  | "WorkspaceChangedSinceReview";

export type WorkspaceIssue = { code: WorkspaceIssueCode; message: string; blocking: boolean };
export type WorkspaceRisk = { code: string; description: string };
export type WorkspaceEffectiveValues = {
  directory: string | null;
  url: string | null;
  executablePath: string | null;
  workingDirectory: string | null;
  arguments: string | null;
  command: string | null;
};

export type WorkspaceAuthorizationResult = {
  isAllowed: boolean;
  primaryIssueCode: WorkspaceIssueCode | null;
  issues: WorkspaceIssue[];
  risks: WorkspaceRisk[];
  effectiveValues: WorkspaceEffectiveValues;
  revision: number;
};

export type WorkspaceReviewToken = { workspaceId: string; revision: number; digest: string };

const EXTERNAL_ACTIONS: WorkspaceAction[] = [
  "LaunchTerminal",
  "LaunchEntry",
  "StartCompanion",
  "OpenUrl",
  "OpenDevServer",
  "OpenDirectory",
];

export function authorize(
  workspace: StoredWorkspace | null,
  action: WorkspaceAction,
  urlOverride?: string | null,
): WorkspaceAuthorizationResult {
  if (!workspace) {
    return result(false, "WorkspaceNotFound", [{ code: "WorkspaceNotFound", message: "Workspace was not found.", blocking: true }], [], null, null, null, null, null, null, 0);
  }

  const content = workspace.content;
  const issues: WorkspaceIssue[] = [];
  const risks: WorkspaceRisk[] = [];
  const directory = canonicalDirectory(content.directory);
  if (!directory) {
    issues.push({ code: "InvalidDirectory", message: "Workspace directory is not a valid absolute path.", blocking: true });
  } else if ((action === "LaunchTerminal" || action === "LaunchEntry" || action === "GrantTrust")
    && isLocalDirectory(directory)
    && !existsSync(directory)) {
    issues.push({ code: "DirectoryMissing", message: "Workspace directory does not exist.", blocking: true });
  }

  const commandResult = validateCommand(content.command);
  if (!commandResult.ok) {
    issues.push({ code: "InvalidCommand", message: commandResult.message, blocking: true });
  }
  if (content.command) {
    risks.push({ code: "command", description: "This workspace can execute arbitrary code." });
  }
  if (content.runAsAdmin || content.launches.some((launch) => launch.runAsAdmin)) {
    risks.push({ code: "elevation", description: "This workspace can request elevation and UAC." });
  }
  if (content.companionApps?.length || content.companionAppPath) {
    risks.push({ code: "companions", description: "This workspace can start companion processes." });
  }
  if (content.devServerUrl || content.repoUrl) {
    risks.push({ code: "urls", description: "This workspace can open external URLs." });
  }

  for (const configuredUrl of [content.devServerUrl, content.repoUrl]) {
    if (configuredUrl && !isHttpUrl(configuredUrl)) {
      issues.push({ code: "InvalidUrl", message: "Only absolute HTTP(S) URLs may be opened.", blocking: true });
    }
  }

  for (const configuredCompanion of content.companionApps ?? []) {
    if (!configuredCompanion.path || configuredCompanion.path.length > 1024 || /[\r\n\0]/.test(configuredCompanion.path)) {
      issues.push({ code: "InvalidCompanion", message: "Companion executable configuration is invalid.", blocking: true });
    }
    if (configuredCompanion.arguments && /[\r\n\0]/.test(configuredCompanion.arguments)) {
      issues.push({ code: "InvalidCompanion", message: "Companion arguments contain control characters.", blocking: true });
    }
  }

  const structural = validateWorkspace(content);
  if (!structural.ok && action !== "CopyPath") {
    issues.push({ code: structural.message.includes("command") ? "InvalidCommand" : "InvalidLaunch", message: structural.message, blocking: true });
  }

  const effectiveUrl = urlOverride ?? (action === "OpenDevServer" ? content.devServerUrl : content.repoUrl);
  let url: string | null = null;
  if (action === "OpenUrl" || action === "OpenDevServer") {
    if (!effectiveUrl || !isHttpUrl(effectiveUrl)) {
      issues.push({ code: "InvalidUrl", message: "Only absolute HTTP(S) URLs may be opened.", blocking: true });
    } else {
      const candidate = effectiveUrl.trim();
      if (!isHttpUrl(candidate)) {
        issues.push({ code: "InvalidUrl", message: "Only absolute HTTP(S) URLs may be opened.", blocking: true });
      } else {
        url = candidate;
      }
    }
  }

  let executablePath: string | null = null;
  const companion = (action === "StartCompanion"
    ? content.companionApps?.[0]
    : content.companionApps?.find((entry) => entry.openOnLaunch)) ??
    (content.companionAppPath ? { path: content.companionAppPath, arguments: content.companionAppArguments } : null);
  if (companion?.path) {
    executablePath = resolveExecutablePath(companion.path);
    if (action === "StartCompanion" && !executablePath) {
      issues.push({ code: "CompanionExecutableUnavailable", message: "The companion executable could not be resolved.", blocking: true });
    }
  } else if (action === "StartCompanion") {
    issues.push({ code: "InvalidCompanion", message: "No companion executable is configured.", blocking: true });
  }

  if (!workspace.security.isTrusted && EXTERNAL_ACTIONS.includes(action)) {
    issues.push({ code: "WorkspaceUntrusted", message: "Trust this workspace before starting external processes or opening it.", blocking: true });
  }

  if (action === "OpenDirectory") {
    if (!workspace.security.isTrusted) {
      issues.push({ code: "DirectoryOpenNotAllowed", message: "Untrusted workspaces cannot open directories.", blocking: true });
    } else if (!directory || !isLocalDirectory(directory) || !existsSync(directory)) {
      issues.push({ code: "DirectoryOpenNotAllowed", message: "Only existing rooted local drive directories can be opened.", blocking: true });
    }
  }

  const primary = primaryIssue(issues);
  const allowed = action === "CopyPath"
    ? !issues.some((issue) => issue.code === "InvalidDirectory")
    : action === "RevokeTrust"
      ? true
      : issues.length === 0;
  return result(allowed, primary, issues, risks, directory, url, executablePath, directory, companion?.arguments ?? null, content.command ?? null, workspace.revision);
}

export function createReviewToken(workspace: StoredWorkspace): WorkspaceReviewToken {
  return { workspaceId: workspace.content.id, revision: workspace.revision, digest: digest(workspace.content) };
}

export function matchesReviewToken(workspace: StoredWorkspace, token: WorkspaceReviewToken): boolean {
  return workspace.content.id.toLowerCase() === token.workspaceId.toLowerCase()
    && workspace.revision === token.revision
    && digest(workspace.content) === token.digest;
}

export function digest(workspace: Workspace): string {
  return JSON.stringify({
    id: workspace.id,
    directory: workspace.directory,
    command: workspace.command,
    runAsAdmin: workspace.runAsAdmin,
    launches: workspace.launches,
    devServerUrl: workspace.devServerUrl,
    repoUrl: workspace.repoUrl,
    companionApps: workspace.companionApps,
  });
}

function result(
  isAllowed: boolean,
  primaryIssueCode: WorkspaceIssueCode | null,
  issues: WorkspaceIssue[],
  risks: WorkspaceRisk[],
  directory: string | null,
  url: string | null,
  executablePath: string | null,
  workingDirectory: string | null,
  effectiveArguments: string | null,
  command: string | null,
  revision = 0,
): WorkspaceAuthorizationResult {
  return {
    isAllowed,
    primaryIssueCode,
    issues,
    risks,
    effectiveValues: { directory, url, executablePath, workingDirectory, arguments: effectiveArguments, command },
    revision,
  };
}

function primaryIssue(issues: WorkspaceIssue[]): WorkspaceIssueCode | null {
  const precedence: WorkspaceIssueCode[] = [
    "WorkspaceNotFound",
    "InvalidDirectory",
    "DirectoryMissing",
    "InvalidCommand",
    "InvalidLaunch",
    "InvalidUrl",
    "InvalidCompanion",
    "CompanionExecutableUnavailable",
    "WorkspaceUntrusted",
    "DirectoryOpenNotAllowed",
    "ActionNotAllowed",
  ];
  return precedence.find((code) => issues.some((issue) => issue.code === code)) ?? null;
}

function canonicalDirectory(value: string): string | null {
  const trimmed = value.trim();
  if (!trimmed || trimmed.length > 1024 || /[\r\n\0]/.test(trimmed) || !isAbsoluteDirectory(trimmed)) {
    return null;
  }
  if (
    trimmed.startsWith("\\\\")
    || trimmed.startsWith("\\\\.\\")
    || trimmed.startsWith("\\\\?\\")
    || trimmed.includes("%")
    || trimmed.toLowerCase().startsWith("shell:")
  ) {
    return null;
  }
  return trimmed;
}

function isLocalDirectory(directory: string): boolean {
  return /^[a-zA-Z]:[\\/]/.test(directory) && !directory.startsWith("\\\\") && !directory.includes("%");
}

function resolveExecutablePath(path: string): string | null {
  const trimmed = path.trim();
  if (!trimmed) {
    return null;
  }
  const candidate = isAbsolute(trimmed) ? trimmed : resolve(trimmed);
  return existsSync(candidate) ? candidate : null;
}

function isHttpUrl(value: string): boolean {
  const candidate = value.trim();
  return /^https?:\/\/[^/\s]+(?:\/[^\s]*)?$/i.test(candidate)
    && !/\s|%(?![0-9a-f]{2})/i.test(candidate);
}
