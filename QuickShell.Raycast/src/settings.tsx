import { Action, ActionPanel, Form, Icon, showToast, Toast } from "@raycast/api";
import { useEffect, useMemo, useState } from "react";
import { getQuickShellStorage } from "./lib/raycast-storage";
import { showStorageFailure } from "./lib/failure-feedback";
import { recentCountFromEnabled } from "./lib/settings";
import type { QuickShellSettings } from "./lib/schema";
import {
  TERMINAL_APPLICATION_CHOICES,
  getDefaultProfileChoices,
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
    () => getDefaultProfileChoices(terminalApplication),
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
      await showToast({
        style: Toast.Style.Success,
        title: "Settings saved",
        message: settingsSummary(next),
      });
    } catch (error) {
      await showStorageFailure("Save settings", error);
    }
  }

  return (
    <Form
      isLoading={isLoading}
      actions={
        <ActionPanel>
          <Action title="Save Settings" icon={Icon.Check} onAction={handleSave} />
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
          const choices = getDefaultProfileChoices(nextApp);
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
    </Form>
  );
}
