import { Action, ActionPanel, Alert, Color, Icon, List, confirmAlert, open } from "@raycast/api";
import { usePromise } from "@raycast/utils";
import { useMemo, useState } from "react";
import EditWorkspaceView from "./components/edit-workspace-view";
import {
  showHealthFailure,
  showLaunchFailure,
  showLaunchSuccess,
  showStorageFailure,
} from "./lib/failure-feedback";
import { executeWorkspaceLaunch } from "./lib/launch-executor";
import { raycastExec } from "./lib/raycast-exec";
import { buildBrowseSections, buildSearchResults } from "./lib/ranking";
import { getQuickShellStorage, workspaceSubtitle } from "./lib/raycast-storage";
import { searchTaskActions, searchWorkspaces } from "./lib/search";
import { isRecentSectionEnabled, RECENT_SECTION_TITLE } from "./lib/settings";
import type { LaunchEntry, QuickShellSettings, Workspace } from "./lib/schema";
import { assessWorkspaceHealth } from "./lib/workspace-health";
import { buildWorkspaceLaunchPlan } from "./lib/windows-launch";

type LoadedData = {
  workspaces: Workspace[];
  settings: QuickShellSettings;
};

type WorkspaceRow = {
  workspace: Workspace;
  launch?: LaunchEntry;
};

type SectionGroup = {
  title?: string;
  rows: WorkspaceRow[];
};

export default function OpenWorkspaceCommand() {
  const [searchText, setSearchText] = useState("");
  const storage = getQuickShellStorage();

  const { data, isLoading, error, revalidate } = usePromise(async (): Promise<LoadedData> => {
    const [workspaces, settings] = await Promise.all([
      storage.getWorkspaces(),
      storage.getSettings(),
    ]);
    return { workspaces, settings };
  }, []);

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

  async function handleOpen(workspace: Workspace, launch?: LaunchEntry) {
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

    const health = assessWorkspaceHealth(launchWorkspace, data.settings);
    if (!health.ok) {
      await showHealthFailure(health.issues);
      return;
    }

    const plan = buildWorkspaceLaunchPlan(launchWorkspace, data.settings);
    const result = await executeWorkspaceLaunch(plan, data.settings, raycastExec);
    if (!result.ok) {
      await showLaunchFailure(result);
      return;
    }

    try {
      await storage.markWorkspaceUsed(workspace.id);
      await revalidate();
      await showLaunchSuccess(
        launch ? `Launching ${launch.label}` : `Opening ${workspace.name}`,
        result.summary,
      );
    } catch (markError) {
      await showStorageFailure("Update recent workspaces", markError);
    }
  }

  async function handleToggleFavorite(workspace: Workspace) {
    try {
      await storage.setFavorite(workspace.id, !workspace.isPinned);
      await revalidate();
      await showLaunchSuccess(
        workspace.isPinned ? "Removed from favorites" : "Added to favorites",
        workspace.name,
      );
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

  function renderWorkspaceItem({ workspace, launch }: WorkspaceRow) {
    if (!data) {
      return null;
    }

    const title = launch ? `${workspace.name} — ${launch.label}` : workspace.name;
    const health = assessWorkspaceHealth(workspace, data.settings);
    const accessories: List.Item.Accessory[] = [];
    if (workspace.abbreviation) {
      accessories.push({ text: workspace.abbreviation });
    }
    if (!health.ok) {
      accessories.push({ icon: { source: Icon.ExclamationMark, tintColor: Color.Orange }, tooltip: health.issues[0]?.message });
    }
    if (workspace.isPinned) {
      accessories.push({ icon: Icon.Star, tooltip: "Favorite" });
    }

    return (
      <List.Item
        key={launch ? `${workspace.id}:${launch.id}` : workspace.id}
        title={title}
        subtitle={health.ok ? workspaceSubtitle(workspace, launch) : health.issues[0]?.message}
        icon={workspace.isPinned ? Icon.Star : Icon.Folder}
        accessories={accessories}
        actions={
          <ActionPanel>
            <ActionPanel.Section title="Open">
              <Action title="Open" icon={Icon.Terminal} onAction={() => handleOpen(workspace, launch)} />
              <Action
                title="Open Folder"
                icon={Icon.Folder}
                shortcut={{ modifiers: ["cmd", "shift"], key: "o" }}
                onAction={() => handleOpenFolder(workspace)}
              />
            </ActionPanel.Section>
            <ActionPanel.Section title="Manage">
              <Action.Push
                title="Edit Workspace"
                icon={Icon.Pencil}
                shortcut={{ modifiers: ["cmd"], key: "e" }}
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
      searchBarPlaceholder="Search workspaces by name, abbreviation, directory, or launch..."
      throttle
    >
      {error ? (
        <List.EmptyView
          icon={Icon.ExclamationMark}
          title="Failed to load workspaces"
          description={error.message}
        />
      ) : null}

      {!error && isEmpty ? (
        <List.EmptyView
          title={searchText.trim() ? "No matching workspaces" : "No workspaces yet"}
          description={
            searchText.trim()
              ? "Try searching by name, abbreviation, directory, or launch command."
              : "Create a workspace to get started."
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
