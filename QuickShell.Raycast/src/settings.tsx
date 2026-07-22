import {
  Action,
  ActionPanel,
  Alert,
  Clipboard,
  Icon,
  List,
  confirmAlert,
  openExtensionPreferences,
  showToast,
  Toast,
} from "@raycast/api";
import { usePromise } from "@raycast/utils";
import { useState } from "react";
import WindowsRequiredView from "./components/windows-required-view";
import { getQuickShellSettingsFromPreferences } from "./lib/extension-preferences";
import { getQuickShellStorage } from "./lib/raycast-storage";
import { showStorageFailure } from "./lib/failure-feedback";
import { isWindowsPlatform } from "./lib/platform";
import { useLoadErrorToast } from "./lib/use-load-error-toast";
import { isRecentSectionEnabled } from "./lib/settings";
import { settingsSummary, getDefaultProfileChoices } from "./lib/terminal-options";
import { discoverWorkspaceTerminalChoices, invalidateTerminalCatalogCache } from "./lib/terminal-catalog";

export default function SettingsCommand() {
  const storage = getQuickShellStorage();
  const [searchText, setSearchText] = useState("");
  const preferences = getQuickShellSettingsFromPreferences();

  const { data, isLoading, error, revalidate } = usePromise(async () => {
    const [workspaceCount, canUndo, canRedo] = await Promise.all([
      storage.getWorkspaces().then((workspaces) => workspaces.length),
      Promise.resolve(storage.canUndo()),
      Promise.resolve(storage.canRedo()),
    ]);
    return { workspaceCount, canUndo, canRedo };
  }, []);

  useLoadErrorToast(error, "Failed to load workspace data");

  async function handleExport() {
    try {
      const json = await storage.exportJson();
      await Clipboard.copy(json);
      await showToast({
        style: Toast.Style.Success,
        title: "Exported",
        message: "Workspaces JSON copied to clipboard.",
      });
    } catch (exportError) {
      await showStorageFailure("Export workspaces", exportError);
    }
  }

  async function handleImport() {
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
        message: `${result.imported} imported, ${result.skipped} skipped, ${result.renamed} renamed.`,
      });
    } catch (importError) {
      await showStorageFailure("Import workspaces", importError);
    }
  }

  async function handleUndo() {
    try {
      const changed = await storage.undo();
      if (!changed) {
        return;
      }
      await revalidate();
      await showToast({ style: Toast.Style.Success, title: "Undo", message: "Reverted the last workspace change." });
    } catch (undoError) {
      await showStorageFailure("Undo workspace change", undoError);
    }
  }

  async function handleRedo() {
    try {
      const changed = await storage.redo();
      if (!changed) {
        return;
      }
      await revalidate();
      await showToast({ style: Toast.Style.Success, title: "Redo", message: "Restored the last undone change." });
    } catch (redoError) {
      await showStorageFailure("Redo workspace change", redoError);
    }
  }

  async function handleRefreshTerminals() {
    try {
      invalidateTerminalCatalogCache();
      const terminals = discoverWorkspaceTerminalChoices();
      const profiles = getDefaultProfileChoices(preferences.terminalApplication);
      await showToast({
        style: Toast.Style.Success,
        title: "Terminal list refreshed",
        message: `${terminals.length} terminals, ${profiles.length} profiles`,
      });
    } catch (refreshError) {
      await showStorageFailure("Refresh terminal list", refreshError);
    }
  }

  if (!isWindowsPlatform()) {
    return <WindowsRequiredView />;
  }

  const workspaceCount = data?.workspaceCount ?? 0;
  const recentsEnabled = isRecentSectionEnabled(preferences.recentWorkspaceCount);

  return (
    <List
      isLoading={isLoading}
      searchText={searchText}
      onSearchTextChange={setSearchText}
      searchBarPlaceholder="Search manage actions..."
      throttle
    >
      {error ? (
        <List.EmptyView icon={Icon.ExclamationMark} title="Failed to load workspace data" description={error.message} />
      ) : null}

      <List.Section title="Defaults">
        <List.Item
          title="Extension Preferences"
          subtitle={settingsSummary(preferences)}
          icon={Icon.Gear}
          accessories={[{ text: recentsEnabled ? "Recents on" : "Recents off" }]}
          actions={
            <ActionPanel>
              <Action title="Open Extension Preferences" icon={Icon.Gear} onAction={() => openExtensionPreferences()} />
            </ActionPanel>
          }
        />
        <List.Item
          title="Refresh Terminal / Profile List"
          subtitle="Re-scan installed terminals and profiles"
          icon={Icon.ArrowClockwise}
          actions={
            <ActionPanel>
              <Action
                title="Refresh Terminal / Profile List"
                icon={Icon.ArrowClockwise}
                onAction={handleRefreshTerminals}
              />
            </ActionPanel>
          }
        />
      </List.Section>

      <List.Section title="Workspace Data">
        <List.Item
          title="Export Workspaces"
          subtitle={`${workspaceCount} workspace${workspaceCount === 1 ? "" : "s"} to clipboard JSON`}
          icon={Icon.Upload}
          actions={
            <ActionPanel>
              <Action title="Export Workspaces" icon={Icon.Upload} onAction={handleExport} />
            </ActionPanel>
          }
        />
        <List.Item
          title="Import from Clipboard"
          subtitle="Merge QuickShell or CmdPal JSON into this extension"
          icon={Icon.Download}
          actions={
            <ActionPanel>
              <Action title="Import from Clipboard" icon={Icon.Download} onAction={handleImport} />
            </ActionPanel>
          }
        />
      </List.Section>

      <List.Section title="History">
        <List.Item
          title="Undo Last Change"
          subtitle={data?.canUndo ? "Available" : "Nothing to undo"}
          icon={Icon.ArrowCounterClockwise}
          actions={
            <ActionPanel>
              <Action title="Undo" icon={Icon.ArrowCounterClockwise} onAction={handleUndo} />
            </ActionPanel>
          }
        />
        <List.Item
          title="Redo Last Change"
          subtitle={data?.canRedo ? "Available" : "Nothing to redo"}
          icon={Icon.ArrowClockwise}
          actions={
            <ActionPanel>
              <Action title="Redo" icon={Icon.ArrowClockwise} onAction={handleRedo} />
            </ActionPanel>
          }
        />
      </List.Section>

      <List.Section title="Tips">
        <List.Item
          title="Root Search"
          subtitle='Type "qs" or a home keyword in Raycast root search'
          icon={Icon.MagnifyingGlass}
        />
        <List.Item
          title="Fallback Command"
          subtitle="Register Open Workspace as a fallback command to honor root-search text"
          icon={Icon.Terminal}
        />
      </List.Section>
    </List>
  );
}
