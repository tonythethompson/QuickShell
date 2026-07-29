import { Action, ActionPanel, Icon, List, showToast, Toast, useNavigation } from "@raycast/api";
import { usePromise } from "@raycast/utils";
import { useEffect, useMemo, useState } from "react";
import WorkspaceForm from "./workspace-form";
import UnsupportedPlatformView from "./unsupported-platform-view";
import { detectCompanionSeed } from "../lib/companion-detection";
import { detectDevServerUrl } from "../lib/detect-dev-server-url";
import { createWorkspaceFromDiscoveredGitRepo } from "../lib/discovered-workspace-seed";
import { resolveWorkspaceSetupSuggestions } from "../lib/suggest-commands";
import {
  discoverGitReposCached,
  discoverGitReposForQueryAsync,
  type GitRepoCandidate,
} from "../lib/git-repo-discovery";
import { searchRootsFromWorkspaces } from "../lib/git-repo-search-roots";
import { getQuickShellStorage } from "../lib/raycast-storage";
import { showStorageFailure } from "../lib/failure-feedback";
import { isSupportedPlatform } from "../lib/platform";
import { useLoadErrorToast } from "../lib/use-load-error-toast";
import type { Workspace } from "../lib/schema";

type ReviewWorkspaceFormProps = {
  directory: string;
  name: string;
  remoteUrl?: string | null;
  onCreated: (workspace: Workspace) => Promise<void>;
};

/** Full seed for every repository selected from Discover Git Repos. */
async function buildWorkspaceFromRepo(directory: string, name: string, remoteUrl?: string | null): Promise<Workspace> {
  const resolved = await resolveWorkspaceSetupSuggestions(directory);
  return createWorkspaceFromDiscoveredGitRepo({
    directory,
    name,
    remoteUrl,
    devServerUrl: detectDevServerUrl(directory),
    tasks: resolved.tasks,
    companionSeed: detectCompanionSeed(directory),
  });
}

function ReviewWorkspaceForm({ directory, name, remoteUrl, onCreated }: ReviewWorkspaceFormProps) {
  const {
    data: initialWorkspace,
    isLoading,
    error,
  } = usePromise(async () => buildWorkspaceFromRepo(directory, name, remoteUrl));

  useLoadErrorToast(error, "Failed to prepare workspace");

  if (error) {
    return (
      <List>
        <List.EmptyView icon={Icon.ExclamationMark} title="Workspace prep failed" description={error.message} />
      </List>
    );
  }

  if (!initialWorkspace) {
    return (
      <List isLoading={isLoading}>
        <List.EmptyView title="Preparing workspace…" description="Loading suggestions for this repository." />
      </List>
    );
  }

  return (
    <WorkspaceForm mode="create" initialWorkspace={initialWorkspace} directorySeedMode="full" onCreated={onCreated} />
  );
}

type DiscoverGitReposViewProps = {
  onWorkspaceAdded?: (workspace: Workspace) => Promise<void> | void;
  /** When false, stay mounted after add (hub root discover). Default true for Action.Push. */
  popOnAdd?: boolean;
};

export default function DiscoverGitReposView({ onWorkspaceAdded, popOnAdd = true }: DiscoverGitReposViewProps) {
  const [searchText, setSearchText] = useState("");
  const [targetedSearch, setTargetedSearch] = useState<{ query: string; repos: GitRepoCandidate[] } | null>(null);
  const [targetedLoadingQuery, setTargetedLoadingQuery] = useState<string | null>(null);
  const { pop } = useNavigation();
  const storage = getQuickShellStorage();

  const { data, isLoading, error, revalidate } = usePromise(async () => {
    const existing = await storage.getWorkspaces();
    const extraRoots = searchRootsFromWorkspaces(existing.map((workspace) => workspace.directory));
    const repos = await discoverGitReposCached(extraRoots);
    const existingDirs = new Set(existing.map((workspace) => workspace.directory.toLowerCase()));
    return repos.filter((repo) => !existingDirs.has(repo.directory.toLowerCase()));
  }, []);

  useLoadErrorToast(error, "Failed to scan git repositories");

  useEffect(() => {
    const query = searchText.trim();
    setTargetedLoadingQuery(null);
    if (!query) {
      setTargetedSearch(null);
      return;
    }

    let cancelled = false;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      setTargetedLoadingQuery(query);
      void (async () => {
        try {
          const existing = await storage.getWorkspaces();
          const existingDirs = new Set(existing.map((workspace) => workspace.directory.toLowerCase()));
          const extraRoots = searchRootsFromWorkspaces(existing.map((workspace) => workspace.directory));
          const repos = (await discoverGitReposForQueryAsync(query, extraRoots, { signal: controller.signal })).filter(
            (repo) => !existingDirs.has(repo.directory.toLowerCase()),
          );
          if (!cancelled) {
            setTargetedSearch({ query, repos });
          }
        } catch (searchError) {
          if (!cancelled) {
            setTargetedSearch({ query, repos: [] });
            await showStorageFailure("Search git repositories", searchError);
          }
        } finally {
          if (!cancelled) {
            setTargetedLoadingQuery(null);
          }
        }
      })();
    }, 250);

    return () => {
      cancelled = true;
      controller.abort();
      clearTimeout(timer);
    };
  }, [searchText]);

  const filtered = useMemo(() => {
    const cached = data ?? [];
    const query = searchText.trim().toLowerCase();
    if (!query) {
      return cached;
    }
    const cachedMatches = cached.filter(
      (repo) =>
        repo.name.toLowerCase().includes(query) ||
        repo.directory.toLowerCase().includes(query) ||
        (repo.remoteUrl ?? "").toLowerCase().includes(query),
    );
    const targetedMatches = targetedSearch?.query === searchText.trim() ? targetedSearch.repos : [];
    const seen = new Set(cachedMatches.map((repo) => repo.directory.toLowerCase()));
    return [
      ...cachedMatches,
      ...targetedMatches.filter((repo) => {
        const key = repo.directory.toLowerCase();
        if (seen.has(key)) {
          return false;
        }
        seen.add(key);
        return true;
      }),
    ];
  }, [data, searchText, targetedSearch]);

  async function finishAdd(workspace: Workspace) {
    await revalidate();
    await onWorkspaceAdded?.(workspace);
    if (popOnAdd) {
      pop();
    }
  }

  async function handleQuickAdd(directory: string, name: string, remoteUrl?: string | null) {
    try {
      const workspace = await buildWorkspaceFromRepo(directory, name, remoteUrl);
      await storage.upsertWorkspace(workspace);
      await showToast({
        style: Toast.Style.Success,
        title: "Workspace added",
        message: workspace.name,
      });
      await finishAdd(workspace);
    } catch (addError) {
      await showStorageFailure("Add workspace", addError);
    }
  }

  if (!isSupportedPlatform()) {
    return <UnsupportedPlatformView />;
  }

  return (
    <List
      isLoading={isLoading || targetedLoadingQuery === searchText.trim()}
      searchText={searchText}
      onSearchTextChange={setSearchText}
      searchBarPlaceholder="Search discovered git repositories..."
      throttle
    >
      {error && filtered.length === 0 ? (
        <List.EmptyView icon={Icon.ExclamationMark} title="Discovery failed" description={error.message} />
      ) : null}

      {!error && filtered.length === 0 ? (
        <List.EmptyView
          title={
            isLoading || targetedLoadingQuery === searchText.trim() ? "Searching folders..." : "No repositories found"
          }
          description="Type a repository name or an absolute path to run a targeted search beyond the cached results."
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
                  <ReviewWorkspaceForm
                    key={repo.directory}
                    directory={repo.directory}
                    name={repo.name}
                    remoteUrl={repo.remoteUrl}
                    onCreated={finishAdd}
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
