import {
  Action,
  ActionPanel,
  Alert,
  Clipboard,
  Color,
  Icon,
  LaunchProps,
  List,
  confirmAlert,
  open,
  showToast,
  Toast,
  updateCommandMetadata,
  Keyboard,
} from "@raycast/api";
import { usePromise } from "@raycast/utils";
import { useEffect, useMemo, useState } from "react";
import EditWorkspaceView from "./components/edit-workspace-view";
import WindowsRequiredView from "./components/windows-required-view";
import WorkspaceForm from "./components/workspace-form";
import SetTargetBranchForm from "./components/set-target-branch-form";
import AddSeparatorForm from "./components/add-separator-form";
import { createBlankWorkspace } from "./lib/create-workspace-initial";
import {
  showAuthorizationFailure,
  showHealthFailure,
  showLaunchFailure,
  showLaunchSuccess,
  showStorageFailure,
} from "./lib/failure-feedback";
import { executeWorkspaceLaunch } from "./lib/launch-executor";
import type { LaunchDiagnosticsReport } from "./lib/launch-diagnostics";
import { raycastExec } from "./lib/raycast-exec";
import { buildBrowseSections, buildSearchResults } from "./lib/ranking";
import { getQuickShellStorage, workspaceSubtitle } from "./lib/raycast-storage";
import { hasAbbreviationMatch, searchTaskActions, searchWorkspaces } from "./lib/search";
import { isRecentSectionEnabled, RECENT_SECTION_TITLE } from "./lib/settings";
import type { LaunchEntry, LayoutEntry, QuickShellSettings, Workspace } from "./lib/schema";
import { assessWorkspaceHealthWithPortProbe } from "./lib/workspace-health";
import { buildWorkspaceHealthIndexWithPorts, lookupWorkspaceHealth } from "./lib/workspace-health-index";
import type { WorkspaceHealthIndex } from "./lib/workspace-health-index";
import { WORKSPACE_LIST_ICON } from "./lib/extension-assets";
import { resolveOpenWorkspaceSearchSeed, type OpenWorkspaceLaunchContext } from "./lib/launch-context";
import { isWindowsPlatform } from "./lib/platform";
import { useLoadErrorToast } from "./lib/use-load-error-toast";
import { buildSelectedLaunchWorkspace, buildWorkspaceLaunchPlan } from "./lib/windows-launch";
import { authorize, authorizePostLaunchEffects, createReviewToken, isWorkspaceTrustEnabled, matchesReviewToken } from "./lib/security";
import { evaluateGitLaunchGate, resolveWorktreeKey } from "./lib/git-launch-gate";

type LoadedData = {
  workspaces: Workspace[];
  settings: QuickShellSettings;
  securityById: Record<string, { isTrusted: boolean; revision: number }>;
  layoutEntries: LayoutEntry[];
  branchTargets: Record<string, string>;
  healthIndex: WorkspaceHealthIndex;
  canUndo: boolean;
  canRedo: boolean;
};

type WorkspaceRow = {
  workspace: Workspace;
  launch?: LaunchEntry;
};

type SeparatorRow = {
  kind: "separator";
  id: string;
  title?: string | null;
};

type SectionGroup = {
  title?: string;
  rows: Array<WorkspaceRow | SeparatorRow>;
};

export default function OpenWorkspaceCommand({
  fallbackText,
  launchContext,
}: LaunchProps<{ launchContext?: OpenWorkspaceLaunchContext }>) {
  const [searchText, setSearchText] = useState(() => resolveOpenWorkspaceSearchSeed(fallbackText, launchContext));
  const storage = getQuickShellStorage();

  const { data, isLoading, error, revalidate } = usePromise(async (): Promise<LoadedData> => {
    const [workspaces, settings, layoutEntries, branchTargets] = await Promise.all([
      storage.getWorkspaces(),
      storage.getSettings(),
      storage.getLayoutEntries(),
      storage.getBranchTargets(),
    ]);
    const securityEntries = await Promise.all(
      workspaces.map(async (workspace) => [workspace.id, await storage.getWorkspaceSecurity(workspace.id)] as const),
    );
    const healthIndex = await buildWorkspaceHealthIndexWithPorts(workspaces, settings);
    return {
      workspaces,
      settings,
      layoutEntries,
      branchTargets,
      healthIndex,
      securityById: Object.fromEntries(
        securityEntries.map(([id, security]) => [id, security ?? { isTrusted: true, revision: 1 }]),
      ),
      canUndo: storage.canUndo(),
      canRedo: storage.canRedo(),
    };
  }, []);

  useLoadErrorToast(error, "Failed to load workspaces");

  useEffect(() => {
    const seeded = resolveOpenWorkspaceSearchSeed(fallbackText, launchContext);
    if (seeded) {
      setSearchText(seeded);
    }
  }, [fallbackText, launchContext?.focusWorkspaceId, launchContext?.focusWorkspaceName]);

  const sectionGroups = useMemo((): SectionGroup[] => {
    if (!data) {
      return [];
    }

    const query = searchText.trim();
    if (!query) {
      const sections = buildBrowseSections(data.workspaces, data.settings.recentWorkspaceCount, data.layoutEntries);
      const groups: SectionGroup[] = [];

      if (sections.favorites.length > 0) {
        groups.push({
          title: "Favorites",
          rows: sections.favorites.map((workspace) => ({ workspace })),
        });
      }

      if (isRecentSectionEnabled(data.settings.recentWorkspaceCount) && sections.recents.length > 0) {
        groups.push({
          title: RECENT_SECTION_TITLE,
          rows: sections.recents.map((workspace) => ({ workspace })),
        });
      }

      for (const layoutSection of sections.layoutSections) {
        const rows: SectionGroup["rows"] = [];
        if (layoutSection.separator) {
          rows.push({
            kind: "separator",
            id: layoutSection.separator.id,
            title: layoutSection.separator.title,
          });
        }
        rows.push(...layoutSection.workspaces.map((workspace) => ({ workspace })));
        if (rows.length > 0) {
          groups.push({ title: layoutSection.title, rows });
        }
      }

      return groups;
    }

    if (hasAbbreviationMatch(data.workspaces, query)) {
      const abbreviationMatches = searchWorkspaces(data.workspaces, query);
      const ranked = buildSearchResults(abbreviationMatches, query);
      return [{ rows: ranked.map((workspace) => ({ workspace })) }];
    }

    const taskActions = searchTaskActions(data.workspaces, query);
    if (taskActions.length > 0) {
      return [
        {
          title: "Launch actions",
          rows: taskActions.map((item) => ({ workspace: item.workspace, launch: item.launch })),
        },
      ];
    }

    const matches = searchWorkspaces(data.workspaces, query);
    const ranked = buildSearchResults(matches, query);
    if (ranked.length === 0) {
      return [];
    }

    return [{ rows: ranked.map((workspace) => ({ workspace })) }];
  }, [data, searchText]);

  const isEmpty = !isLoading && !error && sectionGroups.every((group) => group.rows.length === 0);

  useEffect(() => {
    if (!data) {
      return;
    }

    const count = data.workspaces.length;
    void updateCommandMetadata({
      subtitle: count === 0 ? "No workspaces" : count === 1 ? "1 workspace" : `${count} workspaces`,
    });

    return () => {
      void updateCommandMetadata({ subtitle: null });
    };
  }, [data?.workspaces.length]);

  async function handleOpen(
    workspace: Workspace,
    launch?: LaunchEntry,
    options?: { runAsAdmin?: boolean; runAsStandard?: boolean },
  ) {
    if (!data) {
      return;
    }

    const stored = await storage.getStoredWorkspace(workspace.id);
    if (!stored) {
      await showToast({ style: Toast.Style.Failure, title: "Workspace not found" });
      return;
    }

    const authorization = authorize(
      stored,
      launch ? { kind: "launchEntry", launchId: launch.id } : { kind: "terminal" },
    );
    if (!authorization.isAllowed) {
      const diagnostics: LaunchDiagnosticsReport = {
        title: "Launch blocked",
        workspaceName: workspace.name,
        workspaceId: workspace.id,
        directory: authorization.effectiveValues.directory,
        command: authorization.effectiveValues.command,
        elevation: options?.runAsAdmin ? "admin" : options?.runAsStandard ? "standard" : null,
        denialCode: authorization.primaryIssueCode,
        issues: authorization.issues.map((issue) => `${issue.code}: ${issue.message}`),
      };
      await showAuthorizationFailure(authorization.issues.map((issue) => issue.message).join(" "), diagnostics);
      return;
    }
    const launchWorkspace = launch
      ? buildSelectedLaunchWorkspace(stored.content, launch.id, data.settings)
      : { ...stored.content, launches: stored.content.launches.map((entry) => ({ ...entry })) };
    if (!launchWorkspace) {
      await showToast({ style: Toast.Style.Failure, title: "Selected launch not found" });
      return;
    }
    if (authorization.effectiveValues.directory) {
      launchWorkspace.directory = authorization.effectiveValues.directory;
    }

    const health = await assessWorkspaceHealthWithPortProbe(launchWorkspace, data.settings, {
      includeLaunchPlan: true,
      includeDirectoryExists: true,
    });
    if (!health.ok) {
      await showHealthFailure(health.issues, {
        title: "Health check failed",
        workspaceName: workspace.name,
        workspaceId: workspace.id,
        directory: launchWorkspace.directory,
        command: launchWorkspace.command,
        elevation: options?.runAsAdmin ? "admin" : options?.runAsStandard ? "standard" : null,
        issues: health.issues.map((issue) => `${issue.code}: ${issue.message}`),
      });
      return;
    }

    const gate = await evaluateGitLaunchGate(
      launchWorkspace.directory,
      data.settings.blockDirtyBranchSwitch,
      (key) => data.branchTargets[key],
    );
    if (!gate.canProceed) {
      await showToast({
        style: Toast.Style.Failure,
        title: "Git branch gate",
        message: gate.message ?? "Launch blocked by branch target policy.",
      });
      return;
    }

    const plan = buildWorkspaceLaunchPlan(launchWorkspace, data.settings, {
      runAsAdmin: options?.runAsAdmin,
      runAsStandard: options?.runAsStandard,
    });
    const authorizedEffects = authorizePostLaunchEffects(stored, {
      includeCompanion: !launch,
      includeDevServer: !launch,
    });
    const result = await executeWorkspaceLaunch(plan, data.settings, raycastExec, {
      authorizedEffects: authorizedEffects.plan,
      authorizationWarnings: authorizedEffects.warnings,
      openUrl: open,
    });
    if (!result.ok) {
      await showLaunchFailure(result, {
        title: "Launch failed",
        workspaceName: workspace.name,
        workspaceId: workspace.id,
        directory: launchWorkspace.directory,
        command: launchWorkspace.command,
        elevation: options?.runAsAdmin ? "admin" : options?.runAsStandard ? "standard" : null,
        message: result.message,
      });
      return;
    }

    try {
      await storage.markWorkspaceUsed(workspace.id);
      await storage.flushRecentWrites();
      await revalidate();
      const warningSuffix =
        result.ok && result.postLaunchWarnings?.length ? ` ${result.postLaunchWarnings.join(" ")}` : "";
      await showLaunchSuccess(
        launch ? `Launching ${launch.label}` : `Opening ${workspace.name}`,
        `${result.summary}${warningSuffix}`,
      );
    } catch (markError) {
      await showStorageFailure("Update recent workspaces", markError);
    }
  }

  async function handleToggleFavorite(workspace: Workspace) {
    try {
      await storage.setFavorite(workspace.id, !workspace.isPinned);
      await revalidate();
      await showLaunchSuccess(workspace.isPinned ? "Removed from favorites" : "Added to favorites", workspace.name);
    } catch (favoriteError) {
      await showStorageFailure("Favorite update", favoriteError);
    }
  }

  async function handleMoveFavorite(workspace: Workspace, direction: "up" | "down" | "top" | "bottom") {
    try {
      const moved = await storage.moveFavorite(workspace.id, direction);
      await revalidate();
      if (!moved) {
        return;
      }
      const message =
        direction === "up"
          ? "Moved up"
          : direction === "down"
            ? "Moved down"
            : direction === "top"
              ? "Moved to top"
              : "Moved to bottom";
      await showLaunchSuccess(message, workspace.name);
    } catch (favoriteError) {
      await showStorageFailure("Reorder favorite", favoriteError);
    }
  }

  async function handleDuplicate(workspace: Workspace) {
    try {
      const duplicate = await storage.duplicateWorkspace(workspace.id);
      await revalidate();
      await showLaunchSuccess("Workspace duplicated", duplicate.name);
    } catch (duplicateError) {
      await showStorageFailure("Duplicate workspace", duplicateError);
    }
  }

  async function handleDelete(workspace: Workspace) {
    const confirmed = await confirmAlert({
      title: "Delete workspace?",
      message: `Delete "${workspace.name}"? This cannot be undone.`,
      primaryAction: { title: "Delete", style: Alert.ActionStyle.Destructive },
    });
    if (!confirmed) {
      return;
    }

    try {
      await storage.deleteWorkspace(workspace.id);
      await revalidate();
      await showLaunchSuccess("Workspace deleted", workspace.name);
    } catch (deleteError) {
      await showStorageFailure("Delete workspace", deleteError);
    }
  }

  async function handleOpenFolder(workspace: Workspace) {
    const stored = await storage.getStoredWorkspace(workspace.id);
    const authorization = authorize(stored, { kind: "directory" });
    if (!authorization.isAllowed || !authorization.effectiveValues.directory) {
      await showToast({
        style: Toast.Style.Failure,
        title: "Folder opening blocked",
        message: "Trust this workspace and use a valid local folder.",
      });
      return;
    }
    try {
      await open(authorization.effectiveValues.directory);
    } catch (openError) {
      await showStorageFailure("Open folder", openError);
    }
  }

  async function handleOpenUrl(workspace: Workspace, kind: "repo" | "dev") {
    const stored = await storage.getStoredWorkspace(workspace.id);
    const url = kind === "repo" ? stored?.content.repoUrl : stored?.content.devServerUrl;
    const authorization = authorize(stored, { kind: "url", url });
    if (!authorization.isAllowed || !authorization.effectiveValues.url) {
      await showToast({
        style: Toast.Style.Failure,
        title: "Link opening blocked",
        message: "Trust this workspace and use a valid HTTP(S) URL.",
      });
      return;
    }
    try {
      await open(authorization.effectiveValues.url);
    } catch (openError) {
      await showStorageFailure("Open link", openError);
    }
  }

  async function handleTrust(workspace: Workspace) {
    const stored = await storage.getStoredWorkspace(workspace.id);
    const assessment = authorize(stored, { kind: "grantTrust" });
    if (!stored || !assessment.isAllowed) {
      await showToast({
        style: Toast.Style.Failure,
        title: "Workspace needs repair",
        message: assessment.issues.map((issue) => issue.message).join(" "),
      });
      return;
    }
    const token = createReviewToken(stored);
    const confirmed = await confirmAlert({
      title: "Trust workspace?",
      message:
        "Trust applies to this editable local workspace. It can execute arbitrary code, and later command or launch-setting edits remain trusted until you revoke trust.",
      primaryAction: { title: "Trust Workspace" },
    });
    if (!confirmed) {
      return;
    }
    const current = await storage.getStoredWorkspace(workspace.id);
    if (!current || !matchesReviewToken(current, token)) {
      await showToast({
        style: Toast.Style.Failure,
        title: "Workspace changed",
        message: "Review the updated workspace and confirm again.",
      });
      return;
    }
    const result = await storage.grantTrust(workspace.id, token);
    await revalidate();
    await showToast({
      style: result === "granted" ? Toast.Style.Success : Toast.Style.Failure,
      title: result === "granted" ? "Workspace trusted" : "Trust not granted",
      message: result,
    });
  }

  async function handleRevoke(workspace: Workspace) {
    const result = await storage.revokeTrust(workspace.id);
    await revalidate();
    await showToast({
      style: result === "revoked" ? Toast.Style.Success : Toast.Style.Failure,
      title: result === "revoked" ? "Trust revoked" : "Trust not changed",
      message: result,
    });
  }

  async function handleExport() {
    try {
      const json = await storage.exportJson();
      await Clipboard.copy(json);
      await showToast({ style: Toast.Style.Success, title: "Workspaces copied", message: "JSON copied to clipboard." });
    } catch (exportError) {
      await showStorageFailure("Export workspaces", exportError);
    }
  }

  async function handleImportFromClipboard() {
    try {
      const text = await Clipboard.readText();
      const trimmed = text?.trim() ?? "";
      if (!trimmed) {
        await showToast({
          style: Toast.Style.Failure,
          title: "Clipboard empty",
          message: "Copy QuickShell JSON first.",
        });
        return;
      }
      const preview = await storage.summarizeImport(trimmed, "merge");
      if (preview.hasConflicts) {
        const confirmed = await confirmAlert({
          title: "Import name conflicts",
          message: `${preview.renamed} will be renamed with " (imported)", ${preview.skipped} will be skipped. Continue?`,
          primaryAction: { title: "Import (Rename)", style: Alert.ActionStyle.Default },
          dismissAction: { title: "Cancel" },
        });
        if (!confirmed) {
          return;
        }
      }
      const result = await storage.importJson(trimmed, "merge");
      await revalidate();
      await showToast({
        style: Toast.Style.Success,
        title: "Import complete",
        message: isWorkspaceTrustEnabled()
          ? `${result.imported} imported, ${result.skipped} skipped, ${result.renamed} renamed. Imported workspaces are untrusted until reviewed.`
          : `${result.imported} imported, ${result.skipped} skipped, ${result.renamed} renamed.`,
      });
    } catch (importError) {
      await showStorageFailure("Import workspaces", importError);
    }
  }

  async function handleUndo() {
    const changed = await storage.undo();
    if (!changed) {
      return;
    }
    await revalidate();
    await showToast({ style: Toast.Style.Success, title: "Undo", message: "Reverted the last change." });
  }

  async function handleRedo() {
    const changed = await storage.redo();
    if (!changed) {
      return;
    }
    await revalidate();
    await showToast({ style: Toast.Style.Success, title: "Redo", message: "Restored the last undone change." });
  }

  async function handleClearTargetBranch(workspace: Workspace) {
    try {
      const stored = await storage.getStoredWorkspace(workspace.id);
      const authorization = authorize(stored, { kind: "directory" });
      if (!authorization.isAllowed || !authorization.effectiveValues.directory) {
        await showToast({
          style: Toast.Style.Failure,
          title: "Clear target branch blocked",
          message: "Trust this workspace and use a valid local folder.",
        });
        return;
      }
      const worktreeKey = await resolveWorktreeKey(authorization.effectiveValues.directory);
      if (!worktreeKey) {
        await showToast({
          style: Toast.Style.Failure,
          title: "Not a git repository",
          message: workspace.directory,
        });
        return;
      }
      await storage.clearBranchTarget(worktreeKey);
      await revalidate();
      await showToast({ style: Toast.Style.Success, title: "Target branch cleared", message: workspace.name });
    } catch (error) {
      await showStorageFailure("Clear target branch", error);
    }
  }

  async function handleRemoveSeparator(separatorId: string) {
    try {
      await storage.removeLayoutEntry(separatorId);
      await revalidate();
      await showToast({ style: Toast.Style.Success, title: "Separator removed" });
    } catch (error) {
      await showStorageFailure("Remove separator", error);
    }
  }

  if (!isWindowsPlatform()) {
    return <WindowsRequiredView />;
  }

  function renderSeparatorItem(row: SeparatorRow) {
    return (
      <List.Item
        key={`separator:${row.id}`}
        title={row.title?.trim() || "Section separator"}
        subtitle="Layout separator"
        icon={Icon.Minus}
        actions={
          <ActionPanel>
            <Action
              title="Remove Separator"
              icon={Icon.Trash}
              style={Action.Style.Destructive}
              onAction={() => handleRemoveSeparator(row.id)}
            />
            <Action.Push
              title="Add Section Separator"
              icon={Icon.Plus}
              target={
                <AddSeparatorForm
                  onSaved={async () => {
                    await revalidate();
                  }}
                />
              }
            />
          </ActionPanel>
        }
      />
    );
  }

  function renderWorkspaceItem({ workspace, launch }: WorkspaceRow) {
    if (!data) {
      return null;
    }

    const title = launch ? `${workspace.name} - ${launch.label}` : workspace.name;
    const health = lookupWorkspaceHealth(data.healthIndex, workspace, data.settings);
    const accessories: List.Item.Accessory[] = [];
    if (workspace.abbreviation) {
      accessories.push({ text: workspace.abbreviation });
    }
    if (!health.ok) {
      accessories.push({
        icon: { source: Icon.ExclamationMark, tintColor: Color.Orange },
        tooltip: health.issues[0]?.message,
      });
    } else if (health.issues.some((issue) => issue.severity === "warning")) {
      accessories.push({
        icon: { source: Icon.ExclamationMark, tintColor: Color.Yellow },
        tooltip: health.issues.find((issue) => issue.severity === "warning")?.message,
      });
    }
    const security = data.securityById[workspace.id] ?? { isTrusted: true, revision: 1 };
    if (isWorkspaceTrustEnabled() && !security.isTrusted) {
      accessories.push({ icon: { source: Icon.Lock, tintColor: Color.Orange }, tooltip: "Untrusted workspace" });
    }
    if (workspace.isPinned) {
      accessories.push({ icon: Icon.Star, tooltip: "Favorite" });
    }

    const wantsAdmin = workspace.runAsAdmin || launch?.runAsAdmin;

    return (
      <List.Item
        key={launch ? `${workspace.id}:${launch.id}` : workspace.id}
        title={title}
        subtitle={health.ok ? workspaceSubtitle(workspace, launch) : health.issues[0]?.message}
        icon={workspace.isPinned ? Icon.Star : WORKSPACE_LIST_ICON}
        accessories={accessories}
        actions={
          <ActionPanel>
            <ActionPanel.Section title="Open">
              <Action title="Open" icon={Icon.Terminal} onAction={() => handleOpen(workspace, launch)} />
              {wantsAdmin ? (
                <Action
                  title="Run Normally"
                  icon={Icon.Terminal}
                  shortcut={{ modifiers: ["cmd", "shift"], key: "return" }}
                  onAction={() => handleOpen(workspace, launch, { runAsStandard: true })}
                />
              ) : null}
              {wantsAdmin ? null : (
                <Action
                  title="Run as Administrator"
                  icon={Icon.Shield}
                  shortcut={{ modifiers: ["cmd", "shift"], key: "return" }}
                  onAction={() => handleOpen(workspace, launch, { runAsAdmin: true })}
                />
              )}
              <Action
                title="Open Folder"
                icon={Icon.Folder}
                shortcut={Keyboard.Shortcut.Common.OpenWith}
                onAction={() => handleOpenFolder(workspace)}
              />
              {workspace.repoUrl ? (
                <Action title="Open Repository" icon={Icon.Globe} onAction={() => handleOpenUrl(workspace, "repo")} />
              ) : null}
            </ActionPanel.Section>
            {!isWorkspaceTrustEnabled() || security.isTrusted ? (
              <ActionPanel.Section title="Git">
                <Action.Push
                  title="Set Target Branch…"
                  icon={Icon.Code}
                  target={
                    <SetTargetBranchForm
                      directory={workspace.directory}
                      workspaceName={workspace.name}
                      blockDirtyBranchSwitch={data.settings.blockDirtyBranchSwitch}
                      onSaved={async () => {
                        await revalidate();
                      }}
                    />
                  }
                />
                <Action
                  title="Clear Target Branch"
                  icon={Icon.XMarkCircle}
                  onAction={() => handleClearTargetBranch(workspace)}
                />
              </ActionPanel.Section>
            ) : null}
            <ActionPanel.Section title="Manage">
              <Action.Push
                title="Edit Workspace"
                icon={Icon.Pencil}
                shortcut={Keyboard.Shortcut.Common.Edit}
                target={
                  <EditWorkspaceView
                    workspaceId={workspace.id}
                    onSaved={async () => {
                      await revalidate();
                    }}
                  />
                }
              />
              <Action.Push
                title="Add Section Separator"
                icon={Icon.Minus}
                target={
                  <AddSeparatorForm
                    beforeWorkspaceId={workspace.id}
                    onSaved={async () => {
                      await revalidate();
                    }}
                  />
                }
              />
              <Action
                title={workspace.isPinned ? "Remove Favorite" : "Add Favorite"}
                icon={workspace.isPinned ? Icon.StarDisabled : Icon.Star}
                shortcut={{ modifiers: ["cmd"], key: "f" }}
                onAction={() => handleToggleFavorite(workspace)}
              />
              {workspace.isPinned ? (
                <>
                  <Action
                    title="Move Favorite up"
                    icon={Icon.ArrowUp}
                    shortcut={{ modifiers: ["cmd", "opt"], key: "arrowUp" }}
                    onAction={() => handleMoveFavorite(workspace, "up")}
                  />
                  <Action
                    title="Move Favorite Down"
                    icon={Icon.ArrowDown}
                    shortcut={{ modifiers: ["cmd", "opt"], key: "arrowDown" }}
                    onAction={() => handleMoveFavorite(workspace, "down")}
                  />
                  <Action
                    title="Move Favorite to Top"
                    icon={Icon.ArrowUpCircle}
                    onAction={() => handleMoveFavorite(workspace, "top")}
                  />
                  <Action
                    title="Move Favorite to Bottom"
                    icon={Icon.ArrowDownCircle}
                    onAction={() => handleMoveFavorite(workspace, "bottom")}
                  />
                </>
              ) : null}
            {isWorkspaceTrustEnabled() ? (
              security.isTrusted ? (
                <Action title="Revoke Workspace Trust" icon={Icon.Lock} onAction={() => handleRevoke(workspace)} />
              ) : (
                <Action title="Trust Workspace…" icon={Icon.Shield} onAction={() => handleTrust(workspace)} />
              )
            ) : null}
              <Action
                title="Duplicate"
                icon={Icon.Duplicate}
                shortcut={{ modifiers: ["cmd"], key: "d" }}
                onAction={() => handleDuplicate(workspace)}
              />
              <Action
                title="Delete"
                icon={Icon.Trash}
                style={Action.Style.Destructive}
                shortcut={{ modifiers: ["cmd"], key: "backspace" }}
                onAction={() => handleDelete(workspace)}
              />
              <Action.CopyToClipboard
                title="Copy Directory"
                content={workspace.directory}
                shortcut={{ modifiers: ["cmd"], key: "c" }}
              />
            </ActionPanel.Section>
          </ActionPanel>
        }
      />
    );
  }

  function renderRow(row: WorkspaceRow | SeparatorRow) {
    if ("kind" in row && row.kind === "separator") {
      return renderSeparatorItem(row);
    }
    return renderWorkspaceItem(row);
  }

  return (
    <List
      isLoading={isLoading}
      searchText={searchText}
      onSearchTextChange={setSearchText}
      searchBarPlaceholder="Search workspaces (qs, home keyword, name, launch...)"
      throttle
      actions={
        <ActionPanel>
          <ActionPanel.Section title="Workspaces">
            <Action.Push
              title="Create Workspace"
              icon={Icon.Plus}
              target={
                <WorkspaceForm
                  mode="create"
                  initialWorkspace={createBlankWorkspace()}
                  onSaved={async () => {
                    await revalidate();
                  }}
                />
              }
            />
            <Action title="Export to Clipboard" icon={Icon.Upload} onAction={handleExport} />
            <Action title="Import from Clipboard" icon={Icon.Download} onAction={handleImportFromClipboard} />
            <Action.Push
              title="Add Section Separator"
              icon={Icon.Minus}
              target={
                <AddSeparatorForm
                  onSaved={async () => {
                    await revalidate();
                  }}
                />
              }
            />
          </ActionPanel.Section>
          <ActionPanel.Section title="History">
            <Action
              title="Undo"
              icon={Icon.ArrowCounterClockwise}
              shortcut={{ modifiers: ["cmd"], key: "z" }}
              onAction={handleUndo}
            />
            <Action
              title="Redo"
              icon={Icon.ArrowClockwise}
              shortcut={{ modifiers: ["cmd", "shift"], key: "z" }}
              onAction={handleRedo}
            />
          </ActionPanel.Section>
        </ActionPanel>
      }
    >
      {error ? (
        <List.EmptyView icon={Icon.ExclamationMark} title="Failed to load workspaces" description={error.message} />
      ) : null}

      {!error && isEmpty ? (
        <List.EmptyView
          title={searchText.trim() ? "No matching workspaces" : "No workspaces yet"}
          description={
            searchText.trim()
              ? "Try searching by home keyword, name, directory, or launch command."
              : "Create a workspace to get started."
          }
          actions={
            <ActionPanel>
              <Action.Push
                title="Create Workspace"
                icon={Icon.Plus}
                target={
                  <WorkspaceForm
                    mode="create"
                    initialWorkspace={createBlankWorkspace()}
                    onSaved={async () => {
                      await revalidate();
                    }}
                  />
                }
              />
            </ActionPanel>
          }
        />
      ) : null}

      {sectionGroups.map((group, index) => (
        <List.Section key={`section-${index}`} title={group.title}>
          {group.rows.map((row) => renderRow(row))}
        </List.Section>
      ))}
    </List>
  );
}
