import { Action, ActionPanel, Alert, Icon, List, Toast, confirmAlert, open, showToast } from "@raycast/api";
import { usePromise } from "@raycast/utils";
import { useMemo, useState } from "react";
import EditWorkspaceView from "./components/edit-workspace-view";
import { buildBrowseSections, buildSearchResults } from "./lib/ranking";
import { searchTaskActions, searchWorkspaces } from "./lib/search";
import { isRecentSectionEnabled, RECENT_SECTION_TITLE } from "./lib/settings";
import { getQuickShellStorage, workspaceSubtitle } from "./lib/raycast-storage";
import type { LaunchEntry, QuickShellSettings, Workspace } from "./lib/schema";
import { buildWorkspaceLaunchPlan, formatLaunchPlanSummary } from "./lib/windows-launch";

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

    const plan = buildWorkspaceLaunchPlan(
      launch
        ? {
            ...workspace,
            launches: workspace.launches.map((entry) =>
              entry.id === launch.id ? { ...entry, isEnabled: true } : { ...entry, isEnabled: false },
            ),
          }
        : workspace,
      data.settings,
    );

    if (plan.errors.length > 0) {
      await showToast({ style: Toast.Style.Failure, title: "Cannot open workspace", message: plan.errors[0] });
      return;
    }

    try {
      await storage.markWorkspaceUsed(workspace.id);
      await revalidate();
      await showToast({
        style: Toast.Style.Success,
        title: launch ? `Launching ${launch.label}` : `Opening ${workspace.name}`,
        message: formatLaunchPlanSummary(plan),
      });
    } catch (launchError) {
      const message = launchError instanceof Error ? launchError.message : "Launch failed.";
      await showToast({ style: Toast.Style.Failure, title: "Open failed", message });
    }
  }

  async function handleToggleFavorite(workspace: Workspace) {
    try {
      await storage.setFavorite(workspace.id, !workspace.isPinned);
      await revalidate();
      await showToast({
        style: Toast.Style.Success,
        title: workspace.isPinned ? "Removed from favorites" : "Added to favorites",
      });
    } catch (favoriteError) {
      const message = favoriteError instanceof Error ? favoriteError.message : "Favorite update failed.";
      await showToast({ style: Toast.Style.Failure, title: "Favorite failed", message });
    }
  }

  async function handleDuplicate(workspace: Workspace) {
    try {
      const duplicate = await storage.duplicateWorkspace(workspace.id);
      await revalidate();
      await showToast({ style: Toast.Style.Success, title: "Workspace duplicated", message: duplicate.name });
    } catch (duplicateError) {
      const message = duplicateError instanceof Error ? duplicateError.message : "Duplicate failed.";
      await showToast({ style: Toast.Style.Failure, title: "Duplicate failed", message });
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
      await showToast({ style: Toast.Style.Success, title: "Workspace deleted" });
    } catch (deleteError) {
      const message = deleteError instanceof Error ? deleteError.message : "Delete failed.";
      await showToast({ style: Toast.Style.Failure, title: "Delete failed", message });
    }
  }

  async function handleOpenFolder(workspace: Workspace) {
    try {
      await open(workspace.directory);
    } catch (openError) {
      const message = openError instanceof Error ? openError.message : "Could not open folder.";
      await showToast({ style: Toast.Style.Failure, title: "Open folder failed", message });
    }
  }

  function renderWorkspaceItem({ workspace, launch }: WorkspaceRow) {
    const title = launch ? `${workspace.name} — ${launch.label}` : workspace.name;
    const accessories: List.Item.Accessory[] = [];
    if (workspace.abbreviation) {
      accessories.push({ text: workspace.abbreviation });
    }
    if (workspace.isPinned) {
      accessories.push({ icon: Icon.Star, tooltip: "Favorite" });
    }

    return (
      <List.Item
        key={launch ? `${workspace.id}:${launch.id}` : workspace.id}
        title={title}
        subtitle={workspaceSubtitle(workspace, launch)}
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
