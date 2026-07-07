import { Action, ActionPanel, Icon, List, showToast, Toast } from "@raycast/api";
import { usePromise } from "@raycast/utils";
import { useMemo, useState } from "react";
import WorkspaceForm from "./components/workspace-form";
import WindowsRequiredView from "./components/windows-required-view";
import { buildProjectSetupSuggestions } from "./lib/project-setup-suggestion";
import { discoverGitReposCached } from "./lib/git-repo-discovery";
import { deriveAbbreviationFromName, deriveNameFromDirectory } from "./lib/directory-helpers";
import { getQuickShellStorage } from "./lib/raycast-storage";
import { showStorageFailure } from "./lib/failure-feedback";
import { isWindowsPlatform } from "./lib/platform";
import { useLoadErrorToast } from "./lib/use-load-error-toast";
import { launchRowsFromSuggestions } from "./lib/workspace-form-state";
import { normalizeWorkspace } from "./lib/validation";
import { createStableId } from "./lib/ids";
import type { Workspace } from "./lib/schema";

export default function DiscoverGitReposCommand() {
  const [searchText, setSearchText] = useState("");
  const storage = getQuickShellStorage();

  const { data, isLoading, error, revalidate } = usePromise(async () => {
    const [repos, existing] = await Promise.all([discoverGitReposCached(), storage.getWorkspaces()]);
    const existingDirs = new Set(existing.map((workspace) => workspace.directory.toLowerCase()));
    return repos.filter((repo) => !existingDirs.has(repo.directory.toLowerCase()));
  }, []);

  useLoadErrorToast(error, "Failed to scan git repositories");

  const filtered = useMemo(() => {
    if (!data) {
      return [];
    }
    const query = searchText.trim().toLowerCase();
    if (!query) {
      return data;
    }
    return data.filter(
      (repo) =>
        repo.name.toLowerCase().includes(query) ||
        repo.directory.toLowerCase().includes(query) ||
        (repo.remoteUrl ?? "").toLowerCase().includes(query),
    );
  }, [data, searchText]);

  function buildWorkspaceFromRepo(directory: string, name: string, remoteUrl?: string | null): Workspace {
    const suggestions = buildProjectSetupSuggestions(directory);
    const rows = launchRowsFromSuggestions(suggestions);
    const launchEntries =
      rows.length > 0
        ? rows.map((row, index) => ({
            id: row.id,
            label: row.label,
            terminal: row.terminal,
            wtProfile: row.wtProfile ?? null,
            command: row.command || null,
            runAsAdmin: row.runAsAdmin,
            isEnabled: row.isEnabled,
            order: index,
            taskType: "none" as const,
          }))
        : [
            {
              id: createStableId(),
              label: "Launch",
              terminal: "default" as const,
              wtProfile: null,
              command: null,
              runAsAdmin: false,
              isEnabled: true,
              order: 0,
              taskType: "none" as const,
            },
          ];
    const derivedName = name || deriveNameFromDirectory(directory);
    return normalizeWorkspace({
      id: createStableId(),
      name: derivedName,
      abbreviation: deriveAbbreviationFromName(derivedName),
      directory,
      isPinned: false,
      pinOrder: null,
      lastUsedUtc: null,
      terminal: "default",
      wtProfile: null,
      command: null,
      runAsAdmin: false,
      repoUrl: remoteUrl ?? null,
      launches: launchEntries,
    });
  }

  async function handleQuickAdd(directory: string, name: string, remoteUrl?: string | null) {
    try {
      const workspace = buildWorkspaceFromRepo(directory, name, remoteUrl);
      await storage.upsertWorkspace(workspace);
      await revalidate();
      await showToast({
        style: Toast.Style.Success,
        title: "Workspace added",
        message: workspace.name,
      });
    } catch (addError) {
      await showStorageFailure("Add workspace", addError);
    }
  }

  if (!isWindowsPlatform()) {
    return <WindowsRequiredView />;
  }

  return (
    <List
      isLoading={isLoading}
      searchText={searchText}
      onSearchTextChange={setSearchText}
      searchBarPlaceholder="Search discovered git repositories..."
      throttle
    >
      {error ? (
        <List.EmptyView icon={Icon.ExclamationMark} title="Discovery failed" description={error.message} />
      ) : null}

      {!error && filtered.length === 0 ? (
        <List.EmptyView
          title={isLoading ? "Scanning folders..." : "No repositories found"}
          description="QuickShell scans common project folders under your profile."
        />
      ) : null}

      {filtered.map((repo) => (
        <List.Item
          key={repo.directory}
          title={repo.name}
          subtitle={repo.directory}
          icon={Icon.Folder}
          actions={
            <ActionPanel>
              <Action
                title="Add Workspace"
                icon={Icon.Plus}
                onAction={() => handleQuickAdd(repo.directory, repo.name, repo.remoteUrl)}
              />
              <Action.Push
                title="Review Before Adding"
                icon={Icon.Pencil}
                target={
                  <WorkspaceForm
                    mode="create"
                    initialWorkspace={buildWorkspaceFromRepo(repo.directory, repo.name, repo.remoteUrl)}
                    onSaved={async () => {
                      await revalidate();
                    }}
                  />
                }
              />
            </ActionPanel>
          }
        />
      ))}
    </List>
  );
}
