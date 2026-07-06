import { Action, ActionPanel, Form, Icon, showToast, Toast, useNavigation } from "@raycast/api";
import { useMemo, useState } from "react";
import { getQuickShellStorage } from "../lib/raycast-storage";
import { showStorageFailure, showWorkspaceValidationFailure } from "../lib/failure-feedback";
import { createStableId } from "../lib/ids";
import type { Workspace } from "../lib/schema";
import {
  TERMINAL_APPLICATION_CHOICES,
  WORKSPACE_TERMINAL_CHOICES,
  getWorkspaceProfileChoices,
} from "../lib/terminal-options";
import { normalizeWorkspace, validateWorkspace } from "../lib/validation";

export type WorkspaceFormProps = {
  mode: "create" | "edit";
  initialWorkspace: Workspace;
  onSaved?: () => Promise<void> | void;
};

export default function WorkspaceForm({ mode, initialWorkspace, onSaved }: WorkspaceFormProps) {
  const { pop } = useNavigation();
  const storage = getQuickShellStorage();
  const [name, setName] = useState(initialWorkspace.name);
  const [abbreviation, setAbbreviation] = useState(initialWorkspace.abbreviation ?? "");
  const [directory, setDirectory] = useState(initialWorkspace.directory);
  const [terminal, setTerminal] = useState(initialWorkspace.terminal || "default");
  const [wtProfile, setWtProfile] = useState(initialWorkspace.wtProfile ?? "");
  const [command, setCommand] = useState(
    initialWorkspace.launches.find((entry) => entry.isEnabled)?.command ??
      initialWorkspace.command ??
      "",
  );
  const [isPinned, setIsPinned] = useState(initialWorkspace.isPinned);
  const [runAsAdmin, setRunAsAdmin] = useState(initialWorkspace.runAsAdmin);
  const [launchLabel, setLaunchLabel] = useState(
    initialWorkspace.launches.find((entry) => entry.isEnabled)?.label ?? "Launch",
  );

  const profileChoices = useMemo(() => getWorkspaceProfileChoices(terminal), [terminal]);
  const showProfileField = profileChoices.length > 0;

  function buildWorkspace(): Workspace {
    const launchId = initialWorkspace.launches[0]?.id ?? createStableId();
    return normalizeWorkspace({
      ...initialWorkspace,
      name: name.trim(),
      abbreviation: abbreviation.trim() || null,
      directory: directory.trim(),
      terminal,
      wtProfile: showProfileField && wtProfile.trim() ? wtProfile.trim() : null,
      command: command.trim() || null,
      isPinned,
      runAsAdmin,
      launches: [
        {
          id: launchId,
          label: launchLabel.trim() || name.trim() || "Launch",
          terminal,
          wtProfile: showProfileField && wtProfile.trim() ? wtProfile.trim() : null,
          command: command.trim() || null,
          runAsAdmin,
          isEnabled: true,
          order: 0,
          taskType: "none",
        },
      ],
    });
  }

  async function handleSave() {
    const workspace = buildWorkspace();
    const validation = validateWorkspace(workspace);
    if (!validation.ok) {
      await showWorkspaceValidationFailure(validation.message);
      return;
    }

    try {
      await storage.upsertWorkspace(workspace);
      await onSaved?.();
      await showToast({
        style: Toast.Style.Success,
        title: mode === "create" ? "Workspace created" : "Workspace saved",
        message: workspace.name,
      });
      if (mode === "edit") {
        pop();
      } else {
        setName("");
        setAbbreviation("");
        setDirectory("");
        setCommand("");
        setLaunchLabel("Launch");
        setIsPinned(false);
        setRunAsAdmin(false);
      }
    } catch (error) {
      await showStorageFailure(mode === "create" ? "Create workspace" : "Save workspace", error);
    }
  }

  return (
    <Form
      actions={
        <ActionPanel>
          <Action
            title={mode === "create" ? "Create Workspace" : "Save Workspace"}
            icon={Icon.Check}
            onAction={handleSave}
          />
        </ActionPanel>
      }
    >
      <Form.TextField id="name" title="Name" value={name} onChange={setName} autoFocus />
      <Form.TextField
        id="abbreviation"
        title="Abbreviation"
        value={abbreviation}
        onChange={setAbbreviation}
        placeholder="api"
      />
      <Form.FilePicker
        id="directory"
        title="Directory"
        value={directory ? [directory] : []}
        onChange={(paths) => setDirectory(paths[0] ?? "")}
        canChooseDirectories
        canChooseFiles={false}
      />
      <Form.TextField
        id="launchLabel"
        title="Launch Label"
        value={launchLabel}
        onChange={setLaunchLabel}
        placeholder="Web"
      />
      <Form.Dropdown id="terminal" title="Terminal" value={terminal} onChange={setTerminal}>
        {WORKSPACE_TERMINAL_CHOICES.map((choice) => (
          <Form.Dropdown.Item key={choice.id} value={choice.id} title={choice.title} />
        ))}
      </Form.Dropdown>
      {showProfileField ? (
        <Form.Dropdown
          id="profile"
          title="Profile"
          value={wtProfile || profileChoices[0]?.id || ""}
          onChange={setWtProfile}
        >
          {profileChoices.map((choice) => (
            <Form.Dropdown.Item key={choice.id} value={choice.id} title={choice.title} />
          ))}
        </Form.Dropdown>
      ) : null}
      <Form.TextField
        id="command"
        title="Startup Command"
        value={command}
        onChange={setCommand}
        placeholder="npm run dev"
      />
      <Form.Checkbox id="favorite" label="Favorite" value={isPinned} onChange={setIsPinned} />
      <Form.Checkbox id="admin" label="Run as administrator" value={runAsAdmin} onChange={setRunAsAdmin} />
      <Form.Description
        title="Defaults"
        text={`Workspace terminals set to "default" use ${TERMINAL_APPLICATION_CHOICES.find((choice) => choice.id === "wt")?.title ?? "your QuickShell settings"}.`}
      />
    </Form>
  );
}

export function createBlankWorkspace(): Workspace {
  const id = createStableId();
  return normalizeWorkspace({
    id,
    name: "",
    abbreviation: null,
    directory: "",
    isPinned: false,
    pinOrder: null,
    lastUsedUtc: null,
    terminal: "default",
    wtProfile: null,
    command: null,
    runAsAdmin: false,
    launches: [
      {
        id: createStableId(),
        label: "Launch",
        terminal: "default",
        wtProfile: null,
        command: null,
        runAsAdmin: false,
        isEnabled: true,
        order: 0,
        taskType: "none",
      },
    ],
  });
}
