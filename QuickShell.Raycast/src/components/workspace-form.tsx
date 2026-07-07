import { Action, ActionPanel, Form, Icon, showToast, Toast, useNavigation } from "@raycast/api";
import { useMemo, useState } from "react";
import { getQuickShellStorage } from "../lib/raycast-storage";
import { deriveAbbreviationFromName, deriveNameFromDirectory } from "../lib/directory-helpers";
import { showStorageFailure, showWorkspaceValidationFailure } from "../lib/failure-feedback";
import { createStableId } from "../lib/ids";
import { buildProjectSetupSuggestions } from "../lib/project-setup-suggestion";
import type { Workspace } from "../lib/schema";
import {
  choiceForTerminalState,
  discoverWorkspaceTerminalChoices,
} from "../lib/terminal-catalog";
import { TERMINAL_APPLICATION_CHOICES } from "../lib/terminal-options";
import {
  buildWorkspaceFromFormState,
  launchRowsFromSuggestions,
  type LaunchFormRow,
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
  const terminalChoices = useMemo(() => discoverWorkspaceTerminalChoices(), []);

  const [name, setName] = useState(initialState.name);
  const [abbreviation, setAbbreviation] = useState(initialState.abbreviation);
  const [directory, setDirectory] = useState(initialState.directory);
  const [terminalChoiceId, setTerminalChoiceId] = useState(
    choiceForTerminalState(initialState.terminal, initialState.wtProfile, terminalChoices),
  );
  const [isPinned, setIsPinned] = useState(initialState.isPinned);
  const [runAsAdmin, setRunAsAdmin] = useState(initialState.runAsAdmin);
  const [launches, setLaunches] = useState<LaunchFormRow[]>(initialState.launches);
  const [devServerUrl, setDevServerUrl] = useState(initialState.devServerUrl);
  const [openDevServerOnLaunch, setOpenDevServerOnLaunch] = useState(initialState.openDevServerOnLaunch);
  const [repoUrl, setRepoUrl] = useState(initialState.repoUrl);
  const [openCompanionAppOnLaunch, setOpenCompanionAppOnLaunch] = useState(initialState.openCompanionAppOnLaunch);
  const [companionAppPath, setCompanionAppPath] = useState(initialState.companionAppPath);
  const [companionAppArguments, setCompanionAppArguments] = useState(initialState.companionAppArguments);
  const [nameCustomized, setNameCustomized] = useState(mode === "edit" && Boolean(initialState.name));
  const [abbreviationCustomized, setAbbreviationCustomized] = useState(
    mode === "edit" && Boolean(initialState.abbreviation),
  );
  const [commandsCustomized, setCommandsCustomized] = useState(
    mode === "edit" && initialState.launches.some((launch) => launch.command.trim()),
  );

  const selectedTerminal = terminalChoices.find((choice) => choice.id === terminalChoiceId) ?? terminalChoices[0];

  function applyDirectorySuggestions(nextDirectory: string) {
    if (!nameCustomized && nextDirectory.trim()) {
      const derivedName = deriveNameFromDirectory(nextDirectory);
      setName(derivedName);
      if (!abbreviationCustomized && derivedName) {
        setAbbreviation(deriveAbbreviationFromName(derivedName));
      }
    }

    if (!commandsCustomized && nextDirectory.trim()) {
      const suggestions = buildProjectSetupSuggestions(nextDirectory);
      if (suggestions.length > 0) {
        setLaunches(launchRowsFromSuggestions(suggestions, selectedTerminal?.terminal ?? "default"));
      }
    }
  }

  function handleDirectoryChange(paths: string[]) {
    const nextDirectory = paths[0] ?? "";
    setDirectory(nextDirectory);
    applyDirectorySuggestions(nextDirectory);
  }

  function updateLaunch(index: number, patch: Partial<LaunchFormRow>) {
    setCommandsCustomized(true);
    setLaunches((current) =>
      current.map((row, rowIndex) => (rowIndex === index ? { ...row, ...patch } : row)),
    );
  }

  function addLaunchRow() {
    setCommandsCustomized(true);
    setLaunches((current) => [
      ...current,
      {
        id: createStableId(),
        command: "",
        terminal: selectedTerminal?.terminal ?? "default",
        wtProfile: selectedTerminal?.wtProfile ?? null,
        runAsAdmin,
        isEnabled: true,
        label: `Launch ${current.length + 1}`,
      },
    ]);
  }

  function removeLaunchRow(index: number) {
    setCommandsCustomized(true);
    setLaunches((current) => {
      if (current.length <= 1) {
        return current;
      }
      return current.filter((_, rowIndex) => rowIndex !== index);
    });
  }

  function updateLaunchTerminal(index: number, choiceId: string) {
    const choice = terminalChoices.find((item) => item.id === choiceId);
    if (!choice) {
      return;
    }
    updateLaunch(index, {
      terminal: choice.terminal,
      wtProfile: choice.wtProfile ?? null,
    });
  }

  function buildWorkspace(): Workspace {
    return buildWorkspaceFromFormState(initialWorkspace, {
      name,
      abbreviation,
      directory,
      terminal: selectedTerminal?.terminal ?? "default",
      wtProfile: selectedTerminal?.wtProfile ?? null,
      isPinned,
      runAsAdmin,
      launches,
      devServerUrl,
      openDevServerOnLaunch,
      repoUrl,
      openCompanionAppOnLaunch,
      companionAppPath,
      companionAppArguments,
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
        setLaunches([
          {
            id: createStableId(),
            command: "",
            terminal: "default",
            wtProfile: null,
            runAsAdmin: false,
            isEnabled: true,
            label: "Launch",
          },
        ]);
        setNameCustomized(false);
        setAbbreviationCustomized(false);
        setCommandsCustomized(false);
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
          <Action.SubmitForm
            title={mode === "create" ? "Create Workspace" : "Save Workspace"}
            icon={Icon.Check}
            onSubmit={handleSave}
          />
          <Action title="Add Command" icon={Icon.Plus} onAction={addLaunchRow} />
          {launches.length > 1 ? (
            <ActionPanel.Section title="Remove command">
              {launches.map((launch, index) => (
                <Action
                  key={`remove-${launch.id}`}
                  title={`Remove Command ${index + 1}`}
                  icon={Icon.Minus}
                  onAction={() => removeLaunchRow(index)}
                />
              ))}
            </ActionPanel.Section>
          ) : null}
        </ActionPanel>
      }
    >
      <Form.FilePicker
        id="directory"
        title="Directory"
        value={directory ? [directory] : []}
        onChange={handleDirectoryChange}
        canChooseDirectories
        canChooseFiles={false}
      />
      <Form.TextField
        id="name"
        title="Name"
        value={name}
        onChange={(value) => {
          setNameCustomized(true);
          setName(value);
        }}
      />
      <Form.TextField
        id="abbreviation"
        title="Home keyword"
        info="Type this in Open Workspace for a fast match (for example: home, api, fe)."
        value={abbreviation}
        onChange={(value) => {
          setAbbreviationCustomized(true);
          setAbbreviation(value);
        }}
        placeholder="home"
      />
      <Form.Dropdown
        id="terminal"
        title="Terminal"
        value={terminalChoiceId}
        onChange={setTerminalChoiceId}
      >
        {terminalChoices.map((choice) => (
          <Form.Dropdown.Item key={choice.id} value={choice.id} title={choice.title} />
        ))}
      </Form.Dropdown>
      {launches.map((launch, index) => (
        <Form.TextField
          key={launch.id}
          id={`command-${launch.id}`}
          title={launches.length === 1 ? "Startup Command" : `Command ${index + 1}`}
          value={launch.command}
          onChange={(value) => updateLaunch(index, { command: value })}
          placeholder={index === 0 ? "npm run dev" : "dotnet watch"}
        />
      ))}
      {launches.length > 1
        ? launches.map((launch, index) => (
            <Form.Dropdown
              key={`terminal-${launch.id}`}
              id={`terminal-${launch.id}`}
              title={`Terminal ${index + 1}`}
              value={choiceForTerminalState(launch.terminal, launch.wtProfile, terminalChoices)}
              onChange={(value) => updateLaunchTerminal(index, value)}
            >
              {terminalChoices.map((choice) => (
                <Form.Dropdown.Item key={`${launch.id}-${choice.id}`} value={choice.id} title={choice.title} />
              ))}
            </Form.Dropdown>
          ))
        : null}
      {launches.length > 1 ? (
        <Form.Description
          title="Multiple commands"
          text="Each command opens as its own launch entry. Use Actions → Remove command to delete a row."
        />
      ) : null}
      <Form.Checkbox id="favorite" label="Favorite" value={isPinned} onChange={setIsPinned} />
      <Form.Checkbox id="admin" label="Run as administrator" value={runAsAdmin} onChange={setRunAsAdmin} />
      <Form.Separator />
      <Form.TextField
        id="devServerUrl"
        title="Dev Server URL"
        value={devServerUrl}
        onChange={setDevServerUrl}
        placeholder="http://localhost:5173"
      />
      <Form.Checkbox
        id="openDevServerOnLaunch"
        label="Open dev server link on launch"
        value={openDevServerOnLaunch}
        onChange={setOpenDevServerOnLaunch}
      />
      <Form.TextField id="repoUrl" title="Repository URL" value={repoUrl} onChange={setRepoUrl} placeholder="https://github.com/org/repo" />
      <Form.TextField
        id="companionAppPath"
        title="Companion App"
        value={companionAppPath}
        onChange={setCompanionAppPath}
        placeholder="C:\\Program Files\\Microsoft VS Code\\Code.exe"
      />
      <Form.TextField
        id="companionAppArguments"
        title="Companion Arguments"
        info="Use {folder} or . for the workspace directory."
        value={companionAppArguments}
        onChange={setCompanionAppArguments}
        placeholder="{folder}"
      />
      <Form.Checkbox
        id="openCompanionAppOnLaunch"
        label="Open companion app on launch"
        value={openCompanionAppOnLaunch}
        onChange={setOpenCompanionAppOnLaunch}
      />
      <Form.Description
        title="Defaults"
        text={`Commands and names auto-fill from the selected folder when possible. Terminals marked "default" use ${TERMINAL_APPLICATION_CHOICES.find((choice) => choice.id === "wt")?.title ?? "your QuickShell settings"}.`}
      />
    </Form>
  );
}

export { createBlankWorkspace } from "../lib/create-workspace-initial";
