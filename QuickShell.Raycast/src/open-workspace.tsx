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
import { createBlankWorkspace } from "./lib/create-workspace-initial";
import { showHealthFailure, showLaunchFailure, showLaunchSuccess, showStorageFailure } from "./lib/failure-feedback";
import { executeWorkspaceLaunch } from "./lib/launch-executor";
import { raycastExec } from "./lib/raycast-exec";
import { buildBrowseSections, buildSearchResults } from "./lib/ranking";
import { getQuickShellStorage, workspaceSubtitle } from "./lib/raycast-storage";
import { hasAbbreviationMatch, searchTaskActions, searchWorkspaces } from "./lib/search";
import { isRecentSectionEnabled, RECENT_SECTION_TITLE } from "./lib/settings";
import type { LaunchEntry, QuickShellSettings, Workspace } from "./lib/schema";
import { assessWorkspaceHealthForLaunch } from "./lib/workspace-health";
import { buildWorkspaceHealthIndex, lookupWorkspaceHealth } from "./lib/workspace-health-index";
import { WORKSPACE_LIST_ICON } from "./lib/extension-assets";
import { resolveOpenWorkspaceSearchSeed, type OpenWorkspaceLaunchContext } from "./lib/launch-context";
import { isWindowsPlatform } from "./lib/platform";
import { useLoadErrorToast } from "./lib/use-load-error-toast";
import { buildWorkspaceLaunchPlan } from "./lib/windows-launch";

type LoadedData = {
  workspaces: Workspace[];
  settings: QuickShellSettings;
  canUndo: boolean;
  canRedo: boolean;
};

type WorkspaceRow = {
  workspace: Workspace;
  launch?: LaunchEntry;
};

type SectionGroup = {
  title?: string;
  rows: WorkspaceRow[];
};

export default function OpenWorkspaceCommand({
  fallbackText,
  launchContext,
}: LaunchProps<{ launchContext?: OpenWorkspaceLaunchContext }>) {
  const [searchText, setSearchText] = useState(() => resolveOpenWorkspaceSearchSeed(fallbackText, launchContext));
  const storage = getQuickShellStorage();

  const { data, isLoading, error, revalidate } = usePromise(async (): Promise<LoadedData> => {
    const [workspaces, settings] = await Promise.all([storage.getWorkspaces(), storage.getSettings()]);
    return {
      workspaces,
      settings,
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

  const healthIndex = useMemo(() => {
    if (!data) {
      return null;
    }
    return buildWorkspaceHealthIndex(data.workspaces, data.settings);
  }, [data]);

  const sectionGroups = useMemo((): SectionGroup[] => {
    if (!data) {
      return [];
    }

    const query = searchText.trim();
    if (!query) {
      const sections = buildBrowseSections(data.workspaces, data.settings.recentWorkspaceCount);
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

      if (sections.workspaces.length > 0) {
        groups.push({
          title: sections.favorites.length > 0 ? "Workspaces" : undefined,
          rows: sections.workspaces.map((workspace) => ({ workspace })),
        });
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

    const launchWorkspace = launch
      ? {
          ...workspace,
          launches: workspace.launches.map((entry) =>
            entry.id === launch.id ? { ...entry, isEnabled: true } : { ...entry, isEnabled: false },
          ),
        }
      : workspace;

    const health = assessWorkspaceHealthForLaunch(launchWorkspace, data.settings);
    if (!health.ok) {
      await showHealthFailure(health.issues);
      return;
    }

    const plan = buildWorkspaceLaunchPlan(launchWorkspace, data.settings, {
      runAsAdmin: options?.runAsAdmin,
      runAsStandard: options?.runAsStandard,
    });
    const result = await executeWorkspaceLaunch(plan, data.settings, raycastExec, {
      includeCompanion: !launch,
      includeDevServer: !launch,
    });
    if (!result.ok) {
      await showLaunchFailure(result);
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
    try {
      await open(workspace.directory);
    } catch (openError) {
      await showStorageFailure("Open folder", openError);
    }
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
      const result = await storage.importJson(trimmed, "merge");
      await revalidate();
      await showToast({
        style: Toast.Style.Success,
        title: "Import complete",
        message: `${result.imported} imported, ${result.skipped} skipped, ${result.renamed} renamed.`,
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

  if (!isWindowsPlatform()) {
    return <WindowsRequiredView />;
  }

  function renderWorkspaceItem({ workspace, launch }: WorkspaceRow) {
    if (!data || !healthIndex) {
      return null;
    }

    const title = launch ? `${workspace.name} — ${launch.label}` : workspace.name;
    const health = lookupWorkspaceHealth(healthIndex, workspace, data.settings);
    const accessories: List.Item.Accessory[] = [];
    if (workspace.abbreviation) {
      accessories.push({ text: workspace.abbreviation });
    }
    if (!health.ok) {
      accessories.push({
        icon: { source: Icon.ExclamationMark, tintColor: Color.Orange },
        tooltip: health.issues[0]?.message,
      });
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
                <Action.OpenInBrowser title="Open Repository" url={workspace.repoUrl} icon={Icon.Globe} />
              ) : null}
            </ActionPanel.Section>
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
              <Action
                title={workspace.isPinned ? "Remove Favorite" : "Add Favorite"}
                icon={workspace.isPinned ? Icon.StarDisabled : Icon.Star}
                shortcut={{ modifiers: ["cmd"], key: "f" }}
                onAction={() => handleToggleFavorite(workspace)}
              />
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
        <List.Section key={group.title ?? `section-${index}`} title={group.title}>
          {group.rows.map((row) => renderWorkspaceItem(row))}
        </List.Section>
      ))}
    </List>
  );
}
