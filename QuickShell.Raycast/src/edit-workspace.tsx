import { Action, ActionPanel, Color, Icon, List } from "@raycast/api";
import { usePromise } from "@raycast/utils";
import { useMemo, useState } from "react";
import WorkspaceForm from "./components/workspace-form";
import { getQuickShellStorage, workspaceSubtitle } from "./lib/raycast-storage";
import { assessWorkspaceHealthForList } from "./lib/workspace-health";
import {
  buildWorkspaceHealthIndex,
  lookupWorkspaceHealth,
} from "./lib/workspace-health-index";
import {
  additionalLaunchCount,
  filterWorkspacesForEdit,
} from "./lib/workspace-form-state";
import { WORKSPACE_LIST_ICON } from "./lib/extension-assets";
import type { QuickShellSettings, Workspace } from "./lib/schema";

type EditWorkspaceCommandProps = {
  arguments?: {
    workspaceId?: string;
  };
};

export default function EditWorkspaceCommand({ arguments: args }: EditWorkspaceCommandProps) {
  const [searchText, setSearchText] = useState("");
  const storage = getQuickShellStorage();
  const requestedWorkspaceId = args?.workspaceId?.trim();

  const { data, isLoading, error, revalidate } = usePromise(async () => {
    const [workspaces, settings] = await Promise.all([
      storage.getWorkspaces(),
      storage.getSettings(),
    ]);
    return { workspaces, settings };
  }, []);

  const healthIndex = useMemo(() => {
    if (!data) {
      return null;
    }
    return buildWorkspaceHealthIndex(data.workspaces, data.settings);
  }, [data]);

  const workspaces = useMemo(() => {
    if (!data) {
      return [];
    }
    return filterWorkspacesForEdit(data.workspaces, searchText);
  }, [data, searchText]);

  const preselectedWorkspace = useMemo(() => {
    if (!data || !requestedWorkspaceId) {
      return null;
    }
    return data.workspaces.find((workspace) => workspace.id === requestedWorkspaceId) ?? null;
  }, [data, requestedWorkspaceId]);

  if (preselectedWorkspace) {
    return (
      <WorkspaceForm
        mode="edit"
        initialWorkspace={preselectedWorkspace}
        onSaved={async () => {
          await revalidate();
        }}
      />
    );
  }

  if (requestedWorkspaceId && !isLoading && !preselectedWorkspace) {
    return (
      <List>
        <List.EmptyView
          icon={Icon.ExclamationMark}
          title="Workspace not found"
          description={`No workspace matches ID ${requestedWorkspaceId}.`}
        />
      </List>
    );
  }

  return (
    <List
      isLoading={isLoading}
      searchText={searchText}
      onSearchTextChange={setSearchText}
      searchBarPlaceholder="Search workspaces to edit..."
      throttle
    >
      {error ? (
        <List.EmptyView
          icon={Icon.ExclamationMark}
          title="Failed to load workspaces"
          description={error.message}
        />
      ) : null}

      {!error && workspaces.length === 0 ? (
        <List.EmptyView
          title={searchText.trim() ? "No matching workspaces" : "No workspaces yet"}
          description={
            searchText.trim()
              ? "Try another name, abbreviation, or directory."
              : "Create a workspace first, then edit it here."
          }
        />
      ) : null}

      {workspaces.map((workspace) =>
        renderWorkspacePickerItem(workspace, data?.settings, healthIndex, async () => {
          await revalidate();
        }),
      )}
    </List>
  );
}

function renderWorkspacePickerItem(
  workspace: Workspace,
  settings: QuickShellSettings | undefined,
  healthIndex: ReturnType<typeof buildWorkspaceHealthIndex> | null,
  onSaved: () => Promise<void>,
) {
  const health =
    settings && healthIndex
      ? lookupWorkspaceHealth(healthIndex, workspace, settings)
      : assessWorkspaceHealthForList(workspace, settings ?? { terminalApplication: "wt", defaultProfile: "__default__", recentWorkspaceCount: 8 });
  const extraLaunches = additionalLaunchCount(workspace);
  const accessories: List.Item.Accessory[] = [];
  if (workspace.abbreviation) {
    accessories.push({ text: workspace.abbreviation });
  }
  if (extraLaunches > 0) {
    accessories.push({ text: `+${extraLaunches} launch${extraLaunches === 1 ? "" : "es"}` });
  }
  if (!health.ok) {
    accessories.push({
      icon: { source: Icon.ExclamationMark, tintColor: Color.Orange },
      tooltip: health.issues[0]?.message,
    });
  }

  return (
    <List.Item
      key={workspace.id}
      title={workspace.name}
      subtitle={health.ok ? workspaceSubtitle(workspace) : health.issues[0]?.message}
      icon={workspace.isPinned ? Icon.Star : WORKSPACE_LIST_ICON}
      accessories={accessories}
      actions={
        <ActionPanel>
          <Action.Push
            title="Edit Workspace"
            icon={Icon.Pencil}
            target={<WorkspaceForm mode="edit" initialWorkspace={workspace} onSaved={onSaved} />}
          />
        </ActionPanel>
      }
    />
  );
}
