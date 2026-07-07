import { Action, ActionPanel, Clipboard, Form, Icon, showToast, Toast } from "@raycast/api";
import { useEffect, useMemo, useState } from "react";
import { getQuickShellStorage } from "./lib/raycast-storage";
import { showStorageFailure } from "./lib/failure-feedback";
import { recentCountFromEnabled } from "./lib/settings";
import type { QuickShellSettings } from "./lib/schema";
import { discoverDefaultProfileChoices } from "./lib/terminal-catalog";
import {
  TERMINAL_APPLICATION_CHOICES,
  normalizeDefaultProfile,
  settingsSummary,
} from "./lib/terminal-options";

export default function SettingsCommand() {
  const storage = getQuickShellStorage();
  const [isLoading, setIsLoading] = useState(true);
  const [settings, setSettings] = useState<QuickShellSettings | null>(null);
  const [terminalApplication, setTerminalApplication] = useState<QuickShellSettings["terminalApplication"]>("wt");
  const [defaultProfile, setDefaultProfile] = useState("__default__");
  const [showRecents, setShowRecents] = useState(true);
  const [canUndo, setCanUndo] = useState(false);
  const [canRedo, setCanRedo] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const loaded = await storage.getSettings();
        if (!cancelled) {
          setSettings(loaded);
          setTerminalApplication(loaded.terminalApplication);
          setDefaultProfile(loaded.defaultProfile);
          setShowRecents(loaded.recentWorkspaceCount > 0);
          setCanUndo(storage.canUndo());
          setCanRedo(storage.canRedo());
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [storage]);

  const profileChoices = useMemo(
    () => discoverDefaultProfileChoices(terminalApplication),
    [terminalApplication],
  );

  async function handleSave() {
    const next: QuickShellSettings = {
      terminalApplication,
      defaultProfile: normalizeDefaultProfile(terminalApplication, defaultProfile),
      recentWorkspaceCount: recentCountFromEnabled(showRecents),
    };

    try {
      await storage.updateSettings(next);
      setSettings(next);
      setCanUndo(storage.canUndo());
      setCanRedo(storage.canRedo());
      await showToast({
        style: Toast.Style.Success,
        title: "Settings saved",
        message: settingsSummary(next),
      });
    } catch (error) {
      await showStorageFailure("Save settings", error);
    }
  }

  async function handleExport() {
    try {
      const json = await storage.exportJson();
      await Clipboard.copy(json);
      await showToast({ style: Toast.Style.Success, title: "Exported", message: "Workspaces JSON copied to clipboard." });
    } catch (error) {
      await showStorageFailure("Export workspaces", error);
    }
  }

  async function handleImport() {
    try {
      const text = await Clipboard.readText();
      if (!text.trim()) {
        await showToast({ style: Toast.Style.Failure, title: "Clipboard empty", message: "Copy QuickShell JSON first." });
        return;
      }
      const result = await storage.importJson(text, "merge");
      setCanUndo(storage.canUndo());
      setCanRedo(storage.canRedo());
      await showToast({
        style: Toast.Style.Success,
        title: "Import complete",
        message: `${result.imported} imported, ${result.skipped} skipped.`,
      });
    } catch (error) {
      await showStorageFailure("Import workspaces", error);
    }
  }

  async function handleUndo() {
    const changed = await storage.undo();
    if (!changed) {
      return;
    }
    setCanUndo(storage.canUndo());
    setCanRedo(storage.canRedo());
    await showToast({ style: Toast.Style.Success, title: "Undo", message: "Reverted the last workspace change." });
  }

  async function handleRedo() {
    const changed = await storage.redo();
    if (!changed) {
      return;
    }
    setCanUndo(storage.canUndo());
    setCanRedo(storage.canRedo());
    await showToast({ style: Toast.Style.Success, title: "Redo", message: "Restored the last undone change." });
  }

  return (
    <Form
      isLoading={isLoading}
      actions={
        <ActionPanel>
          <Action.SubmitForm title="Save Settings" icon={Icon.Check} onSubmit={handleSave} />
          <Action title="Export Workspaces" icon={Icon.Upload} onAction={handleExport} />
          <Action title="Import from Clipboard" icon={Icon.Download} onAction={handleImport} />
          <Action title="Undo" icon={Icon.ArrowCounterClockwise} onAction={handleUndo} />
          <Action title="Redo" icon={Icon.ArrowClockwise} onAction={handleRedo} />
        </ActionPanel>
      }
    >
      <Form.Description
        title="Current"
        text={settings ? settingsSummary(settings) : "Loading QuickShell defaults..."}
      />
      <Form.Dropdown
        id="terminalApplication"
        title="Default Terminal App"
        value={terminalApplication}
        onChange={(value) => {
          const nextApp = value as QuickShellSettings["terminalApplication"];
          setTerminalApplication(nextApp);
          const choices = discoverDefaultProfileChoices(nextApp);
          if (!choices.some((choice) => choice.id === defaultProfile)) {
            setDefaultProfile("__default__");
          }
        }}
      >
        {TERMINAL_APPLICATION_CHOICES.map((choice) => (
          <Form.Dropdown.Item key={choice.id} value={choice.id} title={choice.title} />
        ))}
      </Form.Dropdown>
      <Form.Dropdown
        id="defaultProfile"
        title="Default Profile"
        value={defaultProfile}
        onChange={setDefaultProfile}
      >
        {profileChoices.map((choice) => (
          <Form.Dropdown.Item key={choice.id} value={choice.id} title={choice.title} />
        ))}
      </Form.Dropdown>
      <Form.Checkbox
        id="showRecents"
        label="Show Recent workspaces"
        value={showRecents}
        onChange={setShowRecents}
      />
      <Form.Description
        title="Recents"
        text="When enabled, Open Workspace shows up to 8 recent workspaces above older items."
      />
      <Form.Description
        title="History"
        text={`Undo: ${canUndo ? "available" : "none"} • Redo: ${canRedo ? "available" : "none"}`}
      />
      <Form.Description
        title="Root search"
        text='Type "qs" or a home keyword in Raycast root search to jump straight to Open Workspace matches.'
      />
    </Form>
  );
}
