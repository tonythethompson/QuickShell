import { Action, ActionPanel, Form, Icon, showToast, Toast, useNavigation } from "@raycast/api";
import { useMemo, useState } from "react";
import { getQuickShellStorage } from "../lib/raycast-storage";
import { showStorageFailure, showWorkspaceValidationFailure } from "../lib/failure-feedback";
import type { Workspace } from "../lib/schema";
import {
  TERMINAL_APPLICATION_CHOICES,
  WORKSPACE_TERMINAL_CHOICES,
  getWorkspaceProfileChoices,
} from "../lib/terminal-options";
import { createStableId } from "../lib/ids";
import {
  additionalLaunchCount,
  buildWorkspaceFromFormState,
  workspaceFormStateFromWorkspace,
} from "../lib/workspace-form-state";
import { normalizeWorkspace, validateWorkspace } from "../lib/validation";

export type WorkspaceFormProps = {
  mode: "create" | "edit";
  initialWorkspace: Workspace;
  onSaved?: () => Promise<void> | void;
};

export default function WorkspaceForm({ mode, initialWorkspace, onSaved }: WorkspaceFormProps) {
  const { pop } = useNavigation();
  const storage = getQuickShellStorage();
  const initialState = workspaceFormStateFromWorkspace(initialWorkspace);
  const [name, setName] = useState(initialState.name);
  const [abbreviation, setAbbreviation] = useState(initialState.abbreviation);
  const [directory, setDirectory] = useState(initialState.directory);
  const [terminal, setTerminal] = useState(initialState.terminal);
  const [wtProfile, setWtProfile] = useState(initialState.wtProfile);
  const [command, setCommand] = useState(initialState.command);
  const [isPinned, setIsPinned] = useState(initialState.isPinned);
  const [runAsAdmin, setRunAsAdmin] = useState(initialState.runAsAdmin);
  const [launchLabel, setLaunchLabel] = useState(initialState.launchLabel);

  const profileChoices = useMemo(() => getWorkspaceProfileChoices(terminal), [terminal]);
  const showProfileField = profileChoices.length > 0;
  const extraLaunches = mode === "edit" ? additionalLaunchCount(initialWorkspace) : 0;

  function buildWorkspace(): Workspace {
    return buildWorkspaceFromFormState(
      initialWorkspace,
      {
        name,
        abbreviation,
        directory,
        terminal,
        wtProfile,
        command,
        isPinned,
        runAsAdmin,
        launchLabel,
      },
      { showProfileField },
    );
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
      {extraLaunches > 0 ? (
        <Form.Description
          title="Additional launches"
          text={`This workspace has ${extraLaunches} more enabled launch ${extraLaunches === 1 ? "entry" : "entries"}. Saving updates the primary launch only; the others are preserved.`}
        />
      ) : null}
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
